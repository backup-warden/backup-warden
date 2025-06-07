using BackupWarden.Logging;
using BackupWarden.Services.Business;
using BackupWarden.Services.UI;
using BackupWarden.ViewModels;
using BackupWarden.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
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
                    // Register Business services
                    services.AddSingleton<IAppSettingsService, AppSettingsService>();
                    services.AddSingleton<IYamlConfigService, YamlConfigService>();
                    services.AddSingleton<IBackupSyncService, BackupSyncService>();
                    services.AddSingleton<IFileSystemOperations, FileSystemOperations>();

                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<IPickerService, PickerService>();

                    // Register MainWindow and ViewModel
                    services.AddTransient<MainViewModel>();
                    services.AddSingleton<MainPage>();
                })
                .Build();

            UnhandledException += App_UnhandledException;

            Log.Information("Application started");
        }

        private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Log.Error(e.Exception, "Unhandled exception");
            var notification = new AppNotificationBuilder()
                .AddText("An exception was thrown.")
                .AddText($"Type: {e.Exception.GetType()}")
                .AddText($"Message: {e.Message}\r\n" +
                         $"HResult: {e.Exception.HResult}")
                .BuildNotification();

            //Show the notification
            AppNotificationManager.Default.Show(notification);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var mainPage = _host.Services.GetRequiredService<MainPage>();
            MainWindow.Content = mainPage;
            MainWindow.Activate();
        }
    }
}
