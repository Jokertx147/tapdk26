using Avalonia.Controls;

namespace TheAirline.Desktop
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
            UiScale.Attach(this, this.FindControl<LayoutTransformControl>("ZoomRoot"));
        }
    }
}
