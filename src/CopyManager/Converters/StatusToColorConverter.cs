using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CopyManager.Models;

namespace CopyManager.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is JobStatus status)
        {
            return status switch
            {
                JobStatus.Running => new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                JobStatus.Completed => new SolidColorBrush(Color.FromRgb(16, 124, 16)),
                JobStatus.Failed => new SolidColorBrush(Color.FromRgb(196, 43, 28)),
                JobStatus.Cancelled => new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                JobStatus.Paused => new SolidColorBrush(Color.FromRgb(202, 80, 16)),
                JobStatus.Pending => new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                _ => new SolidColorBrush(Colors.Black)
            };
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
