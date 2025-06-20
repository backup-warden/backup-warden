using BackupWarden.Core.Models;
using BackupWarden.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BackupWarden.Views
{
    public sealed partial class MainPage
    {
        public MainViewModel ViewModel { get; }

        public MainPage()
        {
            ViewModel = App.GetService<MainViewModel>();
            DataContext = ViewModel;
            InitializeComponent();
        }


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
