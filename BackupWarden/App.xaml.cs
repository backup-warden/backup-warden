using BackupWarden.Abstractions.Services.UI;
using BackupWarden.Core.Abstractions.Services.Business;
using BackupWarden.Core.Abstractions.Services.UI;
using BackupWarden.Core.Services.Business;
using BackupWarden.Core.ViewModels;
using BackupWarden.Logging;
using BackupWarden.Services.Business;
using BackupWarden.Services.UI;
using BackupWarden.Utils;
using BackupWarden.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Serilog;
using System;
using System.IO.Abstractions;

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

        public static T GetService<T>()
        where T : class
        {
            if ((Current as App)!._host.Services.GetService(typeof(T)) is not T service)
            {
                throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
            }

            return service;
        }

        private readonly IHost _host;

        public App()
        {
            InitializeComponent();

            var isMsix = RuntimeHelper.IsMSIX;

            SerilogConfigurator.Configure(isMsix);

            _host = Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    if (isMsix)
                    {
                        services.AddSingleton<IAppSettingsService, MsixAppSettingsService>();
                    }
                    else
                    {
                        services.AddSingleton<IAppSettingsService, UnpackagedAppSettingsService>();
                    }
                    services.AddSingleton<IYamlConfigService, YamlConfigService>();
                    services.AddSingleton<IBackupSyncService, BackupSyncService>();
                    services.AddSingleton<IFileSystem, FileSystem>();
                    services.AddSingleton<IFileSystemOperations, FileSystemOperations>();

                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<IPickerService, PickerService>();
                    
                    // Register Navigation services
                    services.AddSingleton<IPageService, PageService>();
                    services.AddSingleton<INavigationService, NavigationService>();

                    // Register ViewModels
                    services.AddTransient<MainViewModel>();
                    
                    // Register Pages
                    services.AddTransient<MainPage>();
                    services.AddTransient<ShellPage>();
                })
                .Build();
            
            UnhandledException += App_UnhandledException;

            Log.Information("Application started");
        }

        private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Log.Error(e.Exception, "Unhandled exception");

            if (RuntimeHelper.IsMSIX)
            {
                var notification = new AppNotificationBuilder()
                    .AddText("An exception was thrown.")
                    .AddText($"Type: {e.Exception.GetType()}")
                    .AddText($"Message: {e.Message}\r\n" +
                             $"HResult: {e.Exception.HResult}")
                    .BuildNotification();

                //Show the notification
                AppNotificationManager.Default.Show(notification);
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Use ShellPage as the container for the app's content
            var shellPage = _host.Services.GetRequiredService<ShellPage>();
            MainWindow.Content = shellPage;
            MainWindow.Activate();
        }
    }
}
