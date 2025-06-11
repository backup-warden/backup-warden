using BackupWarden.Core.Abstractions.Services.UI;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace BackupWarden.Services.UI
{
    public class DialogService : IDialogService
    {
        public async Task ShowErrorAsync(string message, string? title = "Error")
        {
            var dialog = new ContentDialog
            {
                Title = title ?? "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
