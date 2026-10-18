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
