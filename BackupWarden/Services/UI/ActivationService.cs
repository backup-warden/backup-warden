using BackupWarden.Abstractions.Activation;
using BackupWarden.Abstractions.Services.UI;
using BackupWarden.Activation;
using BackupWarden.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BackupWarden.Services.UI
{
    public class ActivationService : IActivationService
    {
        private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
        private readonly IEnumerable<IActivationHandler> _activationHandlers;
        private readonly UIElement _shell;

        public ActivationService(ShellPage shellPage, ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers)
        {
            _shell = shellPage;
            _defaultHandler = defaultHandler;
            _activationHandlers = activationHandlers;
        }

        public async Task ActivateAsync(object activationArgs)
        {
            // Execute tasks before activation.
            await InitializeAsync();

            // Set the MainWindow Content.
            if (App.MainWindow.Content == null)
            {
                App.MainWindow.Content = _shell;
            }

            // Handle activation via ActivationHandlers.
            await HandleActivationAsync(activationArgs);

            // Activate the MainWindow.
            App.MainWindow.Activate();

            // Execute tasks after activation.
            await StartupAsync();
        }

        private async Task HandleActivationAsync(object activationArgs)
        {
            var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

            if (activationHandler != null)
            {
                await activationHandler.HandleAsync(activationArgs);
            }

            if (_defaultHandler.CanHandle(activationArgs))
            {
                await _defaultHandler.HandleAsync(activationArgs);
            }
        }

        private static Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        private static Task StartupAsync()
        {
            return Task.CompletedTask;
        }
    }
}
