using BackupWarden.Services;
using BackupWarden.ViewModels;
using BackupWarden.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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

        private readonly IServiceProvider _services;

        public App()
        {
            var services = new ServiceCollection();

            // Register services
            services.AddSingleton<IAppSettingsService, AppSettingsService>();
            services.AddSingleton<IYamlConfigService, YamlConfigService>();
            services.AddSingleton<IBackupSyncService, BackupSyncService>();

            // Register MainWindow and ViewModel
            services.AddTransient<MainViewModel>();
            services.AddSingleton<MainPage>();


            this._services = services.BuildServiceProvider();
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var mainPage = _services.GetRequiredService<MainPage>();
            MainWindow.Content = mainPage;
            MainWindow.Activate();
        }
    }
}
