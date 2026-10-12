using System;
using Avalonia.Controls;
using Avalonia.Threading;
using TheAirline.Presentation;

namespace TheAirline.Desktop.Views
{
    public partial class AirlineView : UserControl
    {
        private readonly DispatcherTimer _timer;

        public AirlineView()
        {
            InitializeComponent();

            // the game clock runs on another thread, so the screen polls for changes
            _timer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(500)};
            _timer.Tick += (s, e) => (DataContext as AirlineViewModel)?.Refresh();
            AttachedToVisualTree += (s, e) => _timer.Start();
            DetachedFromVisualTree += (s, e) => _timer.Stop();
        }
    }
}
