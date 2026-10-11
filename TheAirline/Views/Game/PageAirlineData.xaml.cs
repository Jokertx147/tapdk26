using Prism.Ioc;
using TheAirline.ViewModels.Game;

namespace TheAirline.Views.Game
{
    /// <summary>
    ///     Interaction logic for PageAirlineData.xaml
    /// </summary>
    public partial class PageAirlineData
    {
        public PageAirlineData()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<PageAirlineDataViewModel>();
        }
    }
}
