using System.Threading.Tasks;

namespace BackupWarden.Abstractions.Activation
{
    public interface IActivationHandler
    {
        bool CanHandle(object args);

        Task HandleAsync(object args);
    }
}

