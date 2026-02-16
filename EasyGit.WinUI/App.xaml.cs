using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Services.EasyGit;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EasyGit.WinUI
{
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; private set; }
        public static Window? MainWindow { get; private set; }

        public App()
        {
            InitializeComponent();
            RegisterGlobalExceptionHandlers();
            UnhandledException += App_UnhandledException;
            Services = ConfigureServices();
        }

        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    MessageLogger.Error($"AppDomain unhandled exception:{Environment.NewLine}{exception}");
                    TryWriteCrashLog(exception.ToString());
                }
                else
                {
                    var payload = args.ExceptionObject?.ToString() ?? "Unknown unhandled exception payload.";
                    MessageLogger.Error($"AppDomain unhandled exception: {payload}");
                    TryWriteCrashLog(payload);
                }
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                MessageLogger.Error($"Unobserved task exception:{Environment.NewLine}{args.Exception}");
                TryWriteCrashLog(args.Exception.ToString());
                args.SetObserved();
            };
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                MessageLogger.Error($"Unhandled exception occurred:{Environment.NewLine}{e.Exception}");
                TryWriteCrashLog(e.Exception?.ToString() ?? "Unknown UI unhandled exception.");
            }
            finally
            {
                e.Handled = true;
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (Services == null)
                throw new InvalidOperationException("EasyGit services are not initialized.");

            try
            {
                var window = ActivatorUtilities.CreateInstance<MainWindow>(Services);
                MainWindow = window;
                window.Activate();
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"Startup failed:{Environment.NewLine}{exception}");
                TryWriteCrashLog(exception.ToString());
                throw;
            }
        }

        private static void TryWriteCrashLog(string content)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "easygit-crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {content}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Never crash from crash logging.
            }
        }

        private static ServiceProvider ConfigureServices()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();

            var appConfig = new AppConfig(configuration);
            services.AddSingleton(appConfig);
            services.AddSingleton<AppConfigWriter>();
            services.AddSingleton<FileService>();
            services.AddSingleton<ProfileService>();

            services.AddSingleton<IAiGitAssistant, AzureOpenAiGitAssistant>();
            services.AddSingleton<IAzureDevOpsPrService, AzureDevOpsPrService>();
            services.AddSingleton<IEasyGitWorkflowService, EasyGitWorkflowService>();

            services.AddSingleton<MainWindow>();

            MessageLogger.LogOnly("EasyGit services initialized.");
            return services.BuildServiceProvider();
        }
    }
}
