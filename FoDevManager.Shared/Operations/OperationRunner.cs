using FODevManager.Messages;
using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class OperationRunner
{
    private static readonly AsyncLocal<Action<Message>?> CurrentCapture = new();
    // A single routing subscriber avoids subscription races between runner instances
    private static readonly IDisposable CaptureSubscription = MessageBus.Subscribe(message => CurrentCapture.Value?.Invoke(message));
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object historySync = new();
    private readonly Dictionary<string, Entry> operations = new();
    private readonly Queue<string> completed = new();
    private readonly ApplicationOperationLock operationLock;
    private readonly SecretRedactor redactor;
    private readonly int historyCapacity;
    private readonly int diagnosticCapacity;

    public OperationRunner(AppConfig config) : this(config, new ApplicationOperationLock()) { }

    public OperationRunner(AppConfig config, ApplicationOperationLock operationLock, int historyCapacity = 100, int diagnosticCapacity = 100)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(operationLock);
        ArgumentOutOfRangeException.ThrowIfLessThan(historyCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(diagnosticCapacity, 1);
        this.operationLock = operationLock;
        this.historyCapacity = historyCapacity;
        this.diagnosticCapacity = diagnosticCapacity;
        redactor = new SecretRedactor(config);
    }

    public OperationResult? GetStatus(string operationId)
    {
        lock (historySync) return operations.TryGetValue(operationId, out var entry) ? entry.Snapshot() : null;
    }

    /// <summary>Retains bounded completed history plus all currently queued and running operations</summary>
    public IReadOnlyList<OperationResult> ListOperations()
    {
        lock (historySync) return operations.Values.Select(entry => entry.Snapshot()).ToArray();
    }

    public async Task<OperationResult> RunAsync(string name, bool mutating, Func<CancellationToken, Task<object?>> action,
        CancellationToken cancellationToken = default, string? requestId = null, IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        if (CurrentCapture.Value is not null)
            throw new InvalidOperationException("Nested operation runners are not supported; invoke shared services within the existing operation");
        var entry = new Entry(new OperationResult
        {
            OperationId = Guid.NewGuid().ToString("N"), Name = Limit(redactor.Redact(name)),
            RequestId = requestId is null ? null : Limit(redactor.Redact(requestId)),
            Status = "queued", CreatedAt = DateTimeOffset.UtcNow
        }, diagnosticCapacity, redactor);
        lock (historySync) operations.Add(entry.Result.OperationId, entry);
        var acquired = false;
        var started = false;
        var outcome = "succeeded";
        object? data = null;
        IDisposable? lease = null;
        var previousCapture = CurrentCapture.Value;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (mutating)
            {
                lease = operationLock.TryAcquire();
                if (lease is null)
                {
                    outcome = "busy";
                    entry.Add("Another application operation owns the machine mutation lock");
                }
            }
            if (!mutating || lease is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                started = true;
                entry.Start();
                CurrentCapture.Value = message => entry.Capture(message, progress);
                // Do not declare non-cooperative work cancelled after it successfully completed
                data = redactor.RedactData(await action(cancellationToken).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            entry.Add("Operation cancelled cooperatively");
        }
        catch (Exception exception)
        {
            outcome = "failed";
            entry.Add(exception.GetType().Name + ": " + exception.Message, important: true);
        }
        finally
        {
            CurrentCapture.Value = previousCapture;
            entry.StopCapture();
            try { lease?.Dispose(); }
            catch (Exception exception)
            {
                outcome = "failed";
                entry.Add("Could not release operation lease: " + exception.Message, important: true);
            }
            finally { if (acquired) gate.Release(); }
        }
        return Complete(entry, outcome, data, mutating, started);
    }

    private OperationResult Complete(Entry entry, string outcome, object? data, bool mutating, bool started)
    {
        lock (historySync)
        {
            entry.Finish(outcome, data, mutating, started);
            completed.Enqueue(entry.Result.OperationId);
            while (completed.Count > historyCapacity) operations.Remove(completed.Dequeue());
            return entry.Snapshot();
        }
    }

    private static string Limit(string value) => value.Length <= 4096 ? value : value[..4096] + " [truncated]";

    private sealed class Entry(OperationResult result, int capacity, SecretRedactor redactor)
    {
        private readonly object sync = new();
        private readonly List<(string Text, bool Important)> diagnostics = new();
        private bool error;
        private bool capturing = true;
        private int dropped;
        public OperationResult Result { get; private set; } = result;

        public OperationResult Snapshot()
        {
            lock (sync) return Result with
            {
                Diagnostics = Array.AsReadOnly(diagnostics.Select(item => item.Text).ToArray()), DroppedDiagnosticCount = dropped
            };
        }

        public void Start() { lock (sync) Result = Result with { Status = "running", StartedAt = DateTimeOffset.UtcNow }; }
        public void StopCapture() { lock (sync) capturing = false; }

        public void Add(string text, bool important = false)
        {
            lock (sync)
            {
                if (diagnostics.Count == capacity)
                {
                    dropped++;
                    var index = diagnostics.FindIndex(item => !item.Important);
                    if (index < 0 && !important) return;
                    diagnostics.RemoveAt(index < 0 ? 0 : index);
                }
                diagnostics.Add((Limit(redactor.Redact(text)), important));
            }
        }

        public void Capture(Message message, IProgress<string>? progress)
        {
            string text;
            lock (sync)
            {
                if (!capturing) return;
                // Record failure before touching observers because MessageBus swallows their exceptions
                if (message.Type == MessageType.Error) error = true;
                try
                {
                    text = Limit(redactor.Redact(message.Content));
                    Add(message.Type + ": " + text, important: message.Type == MessageType.Error);
                }
                catch
                {
                    error = true;
                    Add("Diagnostic capture failed", important: true);
                    return;
                }
            }
            try { progress?.Report(text); }
            catch { lock (sync) { if (capturing) Add("Progress observer failed"); } }
        }

        public void Finish(string outcome, object? data, bool mutating, bool started)
        {
            lock (sync)
            {
                // Publish the terminal snapshot only after execution and cleanup have finished
                var status = error ? "failed" : outcome;
                Result = Result with
                {
                    Status = status, Data = data, CompletedAt = DateTimeOffset.UtcNow,
                    PartialChangesPossible = mutating && started && status != "succeeded"
                };
            }
        }
    }
}
