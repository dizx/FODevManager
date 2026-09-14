using FODevManager.Messages;
using FODevManager.Shared.Utils;
using FODevManager.Operations;
using FODevManager.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FODevManager.McpProtocolHost")]

namespace FODevManager.Mcp;

public static class McpHost
{
    public static IMcpServerBuilder RegisterTools(IMcpServerBuilder server) => server
        .WithTools<ProfileTools>().WithTools<ModelTools>().WithTools<RepositoryTools>()
        .WithTools<PackageTools>().WithTools<EnvironmentTools>().WithTools<ApplicationTools>();

    public static Task<int> RunAsync(CancellationToken cancellationToken = default) => RunAsync(null, cancellationToken);

    internal static async Task<int> RunAsync(Action<HostApplicationBuilder>? configure, CancellationToken cancellationToken = default)
    {
        var config = new AppConfig();
        var redactor = new SecretRedactor(config);
        var previousGitPromptPolicy = GitHelper.DisableInteractivePrompts;
        try
        {
            Singleton<Engine>.Instance.EnvironmentType = EnvironmentType.Console;
            GitHelper.DisableInteractivePrompts = true;
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            var settings = new SettingsOperations(config, AppContext.BaseDirectory, builder.Environment.EnvironmentName);
            settings.Reload();
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FODevManager", "Logs");
            Directory.CreateDirectory(logDirectory);
            using var fileLogger = new LoggerConfiguration().WriteTo.File(Path.Combine(logDirectory, "fodev-mcp-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10).CreateLogger();
            using var logging = new RedactedLogging(redactor, fileLogger);
            using var subscription = MessageBus.Subscribe(message => logging.Write(message.Type + ": " + message.Content));
            builder.Logging.AddProvider(logging);
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton<OperationRunner>();
            builder.Services.AddSingleton<ToolExecution>();
            RegisterTools(builder.Services.AddMcpServer().WithStdioServerTransport());
            configure?.Invoke(builder);
            using var host = builder.Build();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(redactor.Redact("MCP host failed: " + ex.Message));
            return 1;
        }
        finally { GitHelper.DisableInteractivePrompts = previousGitPromptPolicy; }
    }

    private sealed class RedactedLogging(SecretRedactor redactor, Serilog.ILogger fileLogger) : ILoggerProvider
    {
        private readonly object sync = new();
        public void Write(string message)
        {
            var safe = redactor.Redact(message);
            lock (sync)
            {
                Console.Error.WriteLine(safe);
                fileLogger.Information("{Diagnostic}", safe);
            }
        }
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }
        private sealed class Logger(RedactedLogging owner) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            // Never format protocol trace/debug bodies, which can contain input-only credentials
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) owner.Write(formatter(state, exception));
            }
        }
    }
}
