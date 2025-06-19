using BackupWarden.Abstractions.Services.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace BackupWarden.Views
{
    /// <summary>
    /// Shell page that acts as the container for the app's content, providing navigation UI and app-wide chrome.
    /// </summary>
    public sealed partial class ShellPage : Page
    {
        private readonly INavigationService _navigationService;

        public ShellPage()
        {
            InitializeComponent();
            
            _navigationService = App.GetService<INavigationService>();
            _navigationService.Frame = ContentFrame;
            _navigationService.Navigated += OnNavigated;
        }

        private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            // Update back button visibility when navigation occurs
            BackButton.Visibility = _navigationService.CanGoBack 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_navigationService.CanGoBack)
            {
                _navigationService.GoBack();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings page (will be implemented later)
            // For now, just show a message
            ShowSettingsFlyout();
        }

        private void ShowSettingsFlyout()
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Settings",
                Content = "Settings page will be implemented in the future.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            // Use non-awaited pattern since this is a void method
            IAsyncOperation<ContentDialogResult> asyncOperation = dialog.ShowAsync();
        }
    }
}