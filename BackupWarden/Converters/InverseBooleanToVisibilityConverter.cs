using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace BackupWarden.Converters
{
    public sealed partial class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return (value is Visibility v && v == Visibility.Collapsed);
        }
    }
}