using Avalonia.Controls;

namespace TheAirline.Desktop.Views
{
    public partial class WorldMapView : UserControl
    {
        public WorldMapView()
        {
            InitializeComponent();
        }

        private WorldMapControl Map => this.FindControl<WorldMapControl>("MapControl");

        private void ZoomIn(object sender, Avalonia.Interactivity.RoutedEventArgs e) => Map?.ZoomBy(1.4);

        private void ZoomOut(object sender, Avalonia.Interactivity.RoutedEventArgs e) => Map?.ZoomBy(1 / 1.4);

        private void FitNetwork(object sender, Avalonia.Interactivity.RoutedEventArgs e) => Map?.FitView();

        private void FindSelected(object sender, Avalonia.Interactivity.RoutedEventArgs e) => Map?.CenterOnSelected();
    }
}

namespace TheAirline.Desktop.Views
{
    /// <summary>A "#rrggbb" colour from the view model as a brush.</summary>
    public class HexBrush : Avalonia.Data.Converters.IValueConverter
    {
        public static readonly HexBrush Instance = new HexBrush();

        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try
            {
                return value is string s ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(s)) : null;
            }
            catch (System.FormatException)
            {
                return null;
            }
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotSupportedException();
        }
    }
}
