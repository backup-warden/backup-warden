using BackupWarden.ViewModels;
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
        public ShellViewModel ViewModel { get; }

        public ShellPage()
        {
            ViewModel = App.GetService<ShellViewModel>();
            InitializeComponent();
            DataContext = ViewModel;
            ViewModel.SetFrame(ContentFrame);
            
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