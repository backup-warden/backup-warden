using BackupWarden.Logging;
using BackupWarden.Services;
using BackupWarden.ViewModels;
using BackupWarden.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BackupWarden
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window MainWindow { get; } = new MainWindow();

        private readonly IHost _host;

        public App()
        {
            InitializeComponent();

            SerilogConfigurator.Configure();

            _host = Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    // Register services
                    services.AddSingleton<IAppSettingsService, AppSettingsService>();
                    services.AddSingleton<IYamlConfigService, YamlConfigService>();
                    services.AddSingleton<IBackupSyncService, BackupSyncService>();

                    // Register MainWindow and ViewModel
                    services.AddTransient<MainViewModel>();
                    services.AddSingleton<MainPage>();
                })
                .Build();


            UnhandledException += App_UnhandledException;

            Log.Information("Application started");
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled exception");
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var mainPage = _host.Services.GetRequiredService<MainPage>();
            MainWindow.Content = mainPage;
            MainWindow.Activate();
        }
    }
}
