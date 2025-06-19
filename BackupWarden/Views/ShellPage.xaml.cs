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
        public ShellPage()
        {
            this.InitializeComponent();

            // Initialize with MainPage content
            NavigateToMainPage();
            
            // Initially hide the back button as we start with the MainPage
            BackButton.Visibility = Visibility.Visible;
        }

        public void NavigateToMainPage()
        {
            // Get the MainPage from the DI container
            var mainPage = App.GetService<MainPage>();
            ContentFrame.Content = mainPage;
            
            // Hide back button when on main page
            BackButton.Visibility = Visibility.Visible;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // For now, just navigate back to the MainPage
            // This will be expanded when you implement more complex navigation
            NavigateToMainPage();
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