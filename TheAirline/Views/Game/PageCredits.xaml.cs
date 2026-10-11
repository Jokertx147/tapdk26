using TheAirline.ViewModels.Game;
using Prism.Ioc;

namespace TheAirline.Views.Game
{
    /// <summary>
    ///     Interaction logic for PageCredits.xaml
    /// </summary>
    public sealed partial class PageCredits
    {
        public PageCredits()
        {
            InitializeComponent();
            ViewModel = ContainerLocator.Container.Resolve<PageCreditsViewModel>();
        }

        public PageCreditsViewModel ViewModel
        {
            get { return DataContext as PageCreditsViewModel; }
            set { DataContext = value; }
        }
    }
}