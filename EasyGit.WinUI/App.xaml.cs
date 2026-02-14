using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Services.EasyGit;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;

namespace EasyGit.WinUI
{
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; private set; }

        public App()
        {
            InitializeComponent();
            Services = ConfigureServices();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (Services == null)
                throw new InvalidOperationException("EasyGit services are not initialized.");

            var window = ActivatorUtilities.CreateInstance<MainWindow>(Services);
            window.Activate();
        }

        private static ServiceProvider ConfigureServices()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();

            var appConfig = new AppConfig(configuration);
            services.AddSingleton(appConfig);
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
