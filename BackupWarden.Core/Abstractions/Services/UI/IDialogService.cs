using System.Threading.Tasks;

namespace BackupWarden.Core.Abstractions.Services.UI
{
    public interface IDialogService
    {
        Task ShowErrorAsync(string message, string? title = "Error");
    }
}
