using TheAirline.ViewModels.Game;
using Prism.Ioc;

namespace TheAirline.Views.Game
{
    /// <summary>
    ///     Interaction logic for PageStartMenu.xaml
    /// </summary>
    public sealed partial class PageStartMenu
    {
        #region Constructors and Destructors

        public PageStartMenu()
        {
            InitializeComponent();
            ViewModel = ContainerLocator.Container.Resolve<PageStartMenuViewModel>();
        }

        #endregion

        public PageStartMenuViewModel ViewModel
        {
            get { return DataContext as PageStartMenuViewModel; }
            set { DataContext = value; }
        }
    }
}