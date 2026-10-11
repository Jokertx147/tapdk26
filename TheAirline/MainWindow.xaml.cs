using System;
using System.Windows;
using System.Windows.Controls;
using NLog;
using Prism.Events;
using Prism.Navigation.Regions;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Enums;
using TheAirline.Infrastructure.Events;
using TheAirline.ViewModels;
using WPFLocalizeExtension.Engine;

namespace TheAirline
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public sealed partial class MainWindow
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly LocalizeDictionary _dictionary = LocalizeDictionary.Instance;

        public MainWindow(IEventAggregator eventAggregator, IRegionManager regionManager, AppState state, MainWindowViewModel viewModel)
        {
            Uri mainPage;

            InitializeComponent();

            DataContext = viewModel;

            // Subscribes to the CloseGameEvent and closes the window when triggered.
            eventAggregator.GetEvent<CloseGameEvent>().Subscribe(a => Close());

            if (state.Mode == ScreenMode.FullScreen)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                Focus();
            }

            if (string.IsNullOrEmpty(state.Language))
            {
                mainPage = new Uri("/PageSelectLanguage", UriKind.Relative);
            }
            else
            {
                _dictionary.SetCultureCommand.Execute(state.Language);
                mainPage = new Uri("/PageStartMenu", UriKind.Relative);
            }

            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            Loaded += (o, args) =>
            {
                Logger.Debug("Navigating to default header and start menu.");
                regionManager.RequestNavigate("HeaderContentRegion", new Uri("/PageHeader", UriKind.Relative));
                regionManager.RequestNavigate("MainContentRegion", mainPage);
            };

            Closing += (o, args) =>
            {
                state.SaveState();
            };

            //Setup.SetupGame();
        }

        public MainWindowViewModel ViewModel
        {
            get { return DataContext as MainWindowViewModel; }
            set { DataContext = value; }
        }

        public bool CanGoBack()
        {
            return false;
        }
        public bool ClearNavigator() { return false; }
        public void NavigateForward() { }
        public void NavigateBack() { }
        public void NavigateTo(Page page) { }
    }
}