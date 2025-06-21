using BackupWarden.Abstractions.Services.UI;
using BackupWarden.Core.Abstractions.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace BackupWarden.ViewModels
{
    public partial class ShellViewModel : BaseViewModel<ShellViewModel>
    {
        private readonly INavigationService _navigationService;

        public IRelayCommand GoBackCommand { get; }

        public ShellViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            GoBackCommand = new RelayCommand(GoBack, CanGoBack);
            MonitorPropertyChanged();
        }

        public void SetFrame(Frame? frame)
        {
            _navigationService.Frame = frame;
        }

        private void MonitorPropertyChanged()
        {
            _navigationService.Navigated += (s, e) =>
            {
                GoBackCommand.NotifyCanExecuteChanged();
            };
        }

        private void GoBack()
        {
            _navigationService.GoBack();
        }

        private bool CanGoBack()
        {
            return _navigationService.CanGoBack;
        }
    }
}
