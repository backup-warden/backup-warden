using BackupWarden.Services;
using BackupWarden.ViewModels;
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
        private readonly IServiceProvider _services;

        public App()
        {
            var services = new ServiceCollection();

            // Register services
            services.AddSingleton<IAppSettingsService, AppSettingsService>();
            services.AddSingleton<IYamlConfigService, YamlConfigService>();
            services.AddSingleton<IBackupSyncService, BackupSyncService>();

            // Register MainWindow and ViewModel
            services.AddSingleton<MainWindow>();
            services.AddTransient<MainViewModel>();

            this._services = services.BuildServiceProvider();
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var window = _services.GetRequiredService<MainWindow>();
            window.Activate();
        }
    }
}
