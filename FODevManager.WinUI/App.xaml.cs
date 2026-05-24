using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Shared.Utils;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinRT;
using WinRT.FODevManager_WinUIVtableClasses;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FODevManager.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; set; }
        public static Window MainWindow { get; private set; } = null!;
        private static readonly string StartupDiagnosticsDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FODevManager",
            "Logs");
        private static readonly string StartupCrashLogPath = System.IO.Path.Combine(
            StartupDiagnosticsDirectory,
            "startup-crash.log");

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            try
            {
                this.InitializeComponent();

                AppSettingsMigration.RunOnStartup();

                ConfigureLogger();
                RegisterGlobalExceptionHandlers();

                UnhandledException += App_UnhandledException;
                Log.Information("Configuring services");
                Services = ConfigureServices();
                Log.Information("Services configured");
            }
            catch (Exception ex)
            {
                WriteStartupCrashLog("App constructor failed", ex);
                throw;
            }
        }

        private void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    MessageLogger.Error($"AppDomain unhandled exception:{Environment.NewLine}{exception}");

                    Log.Fatal(exception, "AppDomain unhandled exception");
                }
                else
                {
                    Log.Fatal("AppDomain unhandled exception: {ExceptionObject}",
                        args.ExceptionObject);
                }
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                MessageLogger.Error(
                    $"Unobserved task exception:{Environment.NewLine}{args.Exception}");

                Log.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };
        }


        private void ConfigureLogger()
        {
            Directory.CreateDirectory(StartupDiagnosticsDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    path: System.IO.Path.Combine(StartupDiagnosticsDirectory, "fodev-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Logger initialized");
        }
       

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                // Full exception (ToString includes inner exceptions + stack)
                MessageLogger.Error($"Unhandled exception occurred:{Environment.NewLine}{e.Exception}");
                Log.Error(e.Exception, "Unhandled exception occurred");
                WriteStartupCrashLog("WinUI unhandled exception", e.Exception);
            }
            finally
            {
                // TEMPORARY while diagnosing freezes/crashes
                e.Handled = true;
            }
        }


        private ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();


            var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
                .Build();

            var config = new AppConfig(configuration);

            services.AddSingleton(config);
            services.AddSingleton<ProfileService>();
            services.AddSingleton<ProfilesContainer>();
            services.AddSingleton<FileService>();
            services.AddSingleton<ModelDeploymentService>();
            services.AddSingleton<DeployablePackageService>();
            services.AddSingleton<ModelVersionService>();
            services.AddSingleton<VisualStudioSolutionService>();
            services.AddSingleton<AppConfigWriter>();
            return services.BuildServiceProvider();
        }


        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                Log.Information("OnLaunched start");

                if (Services == null)
                {
                    throw new InvalidOperationException("Service provider is not initialized");
                }

                var profileService = Services.GetRequiredService<ProfileService>();
                var fileService = Services.GetRequiredService<FileService>();
                var deploymentService = Services.GetRequiredService<ModelDeploymentService>();
                var modelVersionService = Services.GetRequiredService<ModelVersionService>();
                var appConfig = Services.GetRequiredService<AppConfig>();

                Log.Information("Services resolved");

                InitializeW3CState();
                Log.Information("W3C state initialized");

                var mainWindow = new MainWindow(profileService, fileService, deploymentService, modelVersionService, appConfig);
                MainWindow = mainWindow;
                Log.Information("Main window created");
                mainWindow.Activate();
                Log.Information("Main window activated");
            }
            catch (Exception ex)
            {
                WriteStartupCrashLog("OnLaunched failed", ex);
                Log.Fatal(ex, "Application startup failed");
                throw;
            }

        }

        internal static void WriteStartupCrashLog(string stage, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(StartupDiagnosticsDirectory);

                var content = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {stage}{Environment.NewLine}{ex}";
                File.WriteAllText(StartupCrashLogPath, content);
            }
            catch
            {
                // Never throw while trying to record a crash
            }
        }

        public static void InitializeW3CState()
        {
            var w3cServiceState = Singleton<W3cServiceState>.Instance;

            try
            {
                var isRunning = ServiceHelper.IsW3cRunning();

                lock (w3cServiceState.SyncRoot)
                {
                    w3cServiceState.IsRunning = isRunning;
                }
                
            }
            catch (Exception exception)
            {
                lock (w3cServiceState.SyncRoot)
                {
                    w3cServiceState.IsRunning = false;
                }

                MessageLogger.Warning($"W3C: could not read initial service state. Defaulting to Stopped. {exception.Message}");
            }
        }

    }
}

