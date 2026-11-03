namespace FODevManager.Operations;

/// <summary>An exclusive, crash-released lease that can be disposed on any async continuation thread</summary>
public sealed class ApplicationOperationLock
{
    // All hosts coordinate machine resources, even when their profile stores differ
    public static string DefaultLockPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FODevManager", "application-operation.lock");

    public string LockPath { get; }

    public ApplicationOperationLock(string? lockPath = null)
    {
        LockPath = Path.GetFullPath(lockPath ?? DefaultLockPath);
    }

    /// <summary>Returns null on contention; access and filesystem failures are surfaced to the caller</summary>
    public IDisposable? TryAcquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LockPath)!);
        try
        {
            return new Lease(new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            return null;
        }
    }

    private sealed class Lease(FileStream stream) : IDisposable
    {
        private FileStream? stream = stream;

        // Never delete the lock file: deletion could split ownership across two file identities
        public void Dispose() => Interlocked.Exchange(ref stream, null)?.Dispose();
    }
}
