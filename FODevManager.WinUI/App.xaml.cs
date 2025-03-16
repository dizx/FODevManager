using FODevManager.Services;
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FODevManager.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider _serviceProvider;


        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            _serviceProvider = ConfigureServices();
        }

        private ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            var config = new AppConfig(new ConfigurationBuilder().AddJsonFile("appsettings.json").Build());

            services.AddSingleton(config);
            services.AddSingleton<ProfileService>();
            services.AddSingleton<ModelDeploymentService>();
            services.AddSingleton<VisualStudioSolutionService>();
            return services.BuildServiceProvider();
        }


        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var profileService = _serviceProvider.GetRequiredService<ProfileService>();
            var deploymentService = _serviceProvider.GetRequiredService<ModelDeploymentService>();

            var mainWindow = new MainWindow(profileService, deploymentService);
            mainWindow.Activate();

            //m_window = new MainWindow();
            //m_window.Activate();
        }

        //private Window? m_window;
    }
}
