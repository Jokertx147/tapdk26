using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TheAirline.Desktop.Views
{
    public partial class CompetitorsView : UserControl
    {
        public CompetitorsView()
        {
            InitializeComponent();
        }
    }

    /// <summary>A competitor flag: red for a warning (a rival is cheaper, no free gates), grey otherwise.</summary>
    public class CompetitorBadgeBrush : IValueConverter
    {
        public static readonly CompetitorBadgeBrush Instance = new CompetitorBadgeBrush();
        private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#A23B3B"));
        private static readonly IBrush Plain = new SolidColorBrush(Color.Parse("#5A6470"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Warning : Plain;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
