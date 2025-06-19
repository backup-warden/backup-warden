using System.Threading.Tasks;

namespace BackupWarden.Abstractions.Services.UI
{
    public interface IActivationService
    {
        Task ActivateAsync(object activationArgs);
    }
}
