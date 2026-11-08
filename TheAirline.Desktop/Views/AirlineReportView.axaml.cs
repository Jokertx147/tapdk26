using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TheAirline.Desktop.Views
{
    public partial class AirlineReportView : UserControl
    {
        public AirlineReportView()
        {
            InitializeComponent();
        }
    }

    /// <summary>A change on the month before: green better, red worse, grey otherwise.</summary>
    public class ReportTrendBrush : IValueConverter
    {
        public static readonly ReportTrendBrush Instance = new ReportTrendBrush();
        private static readonly IBrush Up = new SolidColorBrush(Color.Parse("#5DBB63"));
        private static readonly IBrush Down = new SolidColorBrush(Color.Parse("#E06C6C"));
        private static readonly IBrush Same = new SolidColorBrush(Color.Parse("#9AA3AD"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int t = value is int i ? i : 0;
            return t > 0 ? Up : t < 0 ? Down : Same;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Income green, costs plain.</summary>
    public class ReportMoneyBrush : IValueConverter
    {
        public static readonly ReportMoneyBrush Instance = new ReportMoneyBrush();
        private static readonly IBrush Income = new SolidColorBrush(Color.Parse("#5DBB63"));
        private static readonly IBrush Cost = new SolidColorBrush(Color.Parse("#D8D8D8"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Income : Cost;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>A losing route or a cheaper rival in red, otherwise the usual text colour.</summary>
    public class ReportWarningBrush : IValueConverter
    {
        public static readonly ReportWarningBrush Instance = new ReportWarningBrush();
        private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#E06C6C"));
        private static readonly IBrush Plain = new SolidColorBrush(Color.Parse("#E8E8E8"));

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
