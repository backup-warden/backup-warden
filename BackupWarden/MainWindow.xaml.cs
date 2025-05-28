using BackupWarden.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using System;
using Windows.Graphics;
using Microsoft.UI.Windowing;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BackupWarden
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            var newWidth = CalculateWidthForAspectRatio(AppWindow.Size, 4, 3);
            AppWindow.Resize(new SizeInt32(newWidth, AppWindow.Size.Height));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumHeight = AppWindow.Size.Height;
                presenter.PreferredMinimumWidth = newWidth;
            }
            CenterWindow();
        }
        private void CenterWindow()
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
            if (area == null) return;
            AppWindow.Move(new PointInt32((area.Value.Width - AppWindow.Size.Width) / 2, (area.Value.Height - AppWindow.Size.Height) / 2));
        }

        private static int CalculateWidthForAspectRatio(SizeInt32 currentSize, int aspectWidth = 4, int aspectHeight = 3)
        {
            // Calculate new width based on the current height and desired aspect ratio
            return (currentSize.Height * aspectWidth) / aspectHeight;
        }

    }
}
