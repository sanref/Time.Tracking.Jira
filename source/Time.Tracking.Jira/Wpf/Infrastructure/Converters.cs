using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Time.Tracking.Jira.Wpf.Infrastructure
{
    /// <summary>True -&gt; Visible, False -&gt; Collapsed. Pass "invert" as parameter to flip.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            if ("invert".Equals(parameter))
                flag = !flag;

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
