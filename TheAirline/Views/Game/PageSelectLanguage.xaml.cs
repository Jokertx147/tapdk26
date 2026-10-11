using TheAirline.ViewModels.Game;
using Prism.Ioc;

namespace TheAirline.Views.Game
{
    /// <summary>
    ///     Interaction logic for PageSelectLanguage.xaml
    /// </summary>
    public sealed partial class PageSelectLanguage
    {
        public PageSelectLanguage()
        {
            InitializeComponent();
            ViewModel = ContainerLocator.Container.Resolve<PageSelectLanguageViewModel>();
        }

        public PageSelectLanguageViewModel ViewModel
        {
            get { return DataContext as PageSelectLanguageViewModel; }
            set { DataContext = value; }
        }
    }
}