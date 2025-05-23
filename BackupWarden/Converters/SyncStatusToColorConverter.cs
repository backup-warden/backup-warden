using BackupWarden.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace BackupWarden.Converters
{
    public sealed partial class SyncStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is SyncStatus status)
            {
                return status switch
                {
                    SyncStatus.InSync => (SolidColorBrush)Application.Current.Resources["SystemFillColorSuccessBrush"],
                    SyncStatus.OutOfSync => (SolidColorBrush)Application.Current.Resources["SystemFillColorCautionBrush"],
                    SyncStatus.Syncing => (SolidColorBrush)Application.Current.Resources["SystemFillColorCautionBrush"],
                    SyncStatus.Failed => (SolidColorBrush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                    _ => (SolidColorBrush)Application.Current.Resources["SystemFillColorNeutralBrush"],
                };
            }
            return (SolidColorBrush)Application.Current.Resources["SystemFillColorNeutralBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
