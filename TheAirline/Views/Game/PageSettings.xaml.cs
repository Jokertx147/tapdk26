using TheAirline.ViewModels.Game;
using Prism.Ioc;

namespace TheAirline.Views.Game
{
    /// <summary>
    ///     Interaction logic for PageSettings.xaml
    /// </summary>
    public partial class PageSettings
    {
        #region Constructors and Destructors

        public PageSettings()
        {
            InitializeComponent();
            ViewModel = ContainerLocator.Container.Resolve<PageSettingsViewModel>();
        }

        #endregion

        public PageSettingsViewModel ViewModel
        {
            get { return DataContext as PageSettingsViewModel; }
            set { DataContext = value; }
        }
    }
}