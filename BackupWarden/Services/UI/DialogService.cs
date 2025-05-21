using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Services.UI
{
    public interface IDialogService
    {
        Task ShowErrorAsync(string message, string? title = "Error");
    }

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
