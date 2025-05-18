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

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);

            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new SizeInt32(1280, 720)); 
        }
    }
}
