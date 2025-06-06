using BackupWarden.Models;
using BackupWarden.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BackupWarden.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage(MainViewModel mainViewModel)
        {
            ViewModel = mainViewModel;
            InitializeComponent();
        }

        public MainViewModel ViewModel { get; }

        private void SyncModeHelpButton_Click(object sender, RoutedEventArgs e)
        {
            if (SyncModeTeachingTip is not null)
            {
                SyncModeTeachingTip.IsOpen = true;
            }
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel.SelectedApps.Clear();
            foreach (var item in ((ListView)sender).SelectedItems)
            {
                ViewModel.SelectedApps.Add((AppConfig)item);
            }
        }
    }
}
