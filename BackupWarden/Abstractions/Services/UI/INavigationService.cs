using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BackupWarden.Abstractions.Services.UI
{
    public interface INavigationService
    {
        event NavigatedEventHandler Navigated;

        bool CanGoBack
        {
            get;
        }

        Frame? Frame
        {
            get; set;
        }

        bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false);

        bool GoBack();
    }
}
