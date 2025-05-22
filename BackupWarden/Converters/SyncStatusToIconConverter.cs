using BackupWarden.Models;
using Microsoft.UI.Xaml.Data;
using System;

namespace BackupWarden.Converters
{
    public sealed partial class SyncStatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is SyncStatus status)
            {
                // Use Segoe Fluent Icons (Windows 11)
                return status switch
                {
                    SyncStatus.InSync => "\uE73E",      // Checkmark
                    SyncStatus.OutOfSync => "\uEA6A",   // Cancel
                    SyncStatus.Syncing => "\uE8F6",     // Sync
                    SyncStatus.Failed => "\uEA39",      // Error
                    _ => "\uE9CE",                      // Help/Unknown
                };
            }
            return "\uE9CE";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
