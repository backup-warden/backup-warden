using Microsoft.UI.Xaml.Controls;

namespace BackupWarden.Services.UI.Extensions
{
    /// <summary>
    /// Provides extension methods for Frame objects
    /// </summary>
    public static class FrameExtensions
    {
        /// <summary>
        /// Gets the view model associated with the current page in the frame
        /// </summary>
        /// <param name="frame">The frame containing the page</param>
        /// <returns>The view model if found; otherwise null</returns>
        public static object? GetPageViewModel(this Frame frame)
        {
            return (frame.Content as Page)?.DataContext;
        }
    }
}