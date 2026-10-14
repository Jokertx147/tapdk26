using System;
using System.Windows;
using System.Windows.Controls;
using NLog;
using Prism.Events;
using Prism.Navigation.Regions;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Enums;
using TheAirline.Infrastructure.Events;
using TheAirline.Models.General;
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

            TheAirline.Infrastructure.Services.UiScale.Attach(this, RootGrid);

            // the older Page based screens navigate through PageNavigator
            GUIModel.HelpersModel.PageNavigator.MainWindow = this;

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

            // the window is maximized from XAML; forcing the size to the screen made it overflow the display
            WindowState = WindowState.Maximized;

            Loaded += (o, args) =>
            {
                Logger.Debug("Navigating to default header and start menu.");
                regionManager.RequestNavigate("HeaderContentRegion", new Uri("/PageHeader", UriKind.Relative));
                regionManager.RequestNavigate("MainContentRegion", mainPage);
            };

            PreviewKeyDown += async (o, args) =>
            {
                // Escape asks to exit; it still closes an open drop-down list and works as normal in text boxes
                if (args.Key == System.Windows.Input.Key.Escape)
                {
                    object focused = System.Windows.Input.Keyboard.FocusedElement;

                    if (args.IsRepeat
                        || _askingToExit
                        || focused is System.Windows.Controls.Primitives.TextBoxBase
                        || focused is PasswordBox
                        || (focused is ComboBox openCombo && openCombo.IsDropDownOpen))
                    {
                        return;
                    }

                    args.Handled = true;
                    _askingToExit = true;
                    try
                    {
                        var answer = GraphicsModel.UserControlModel.MessageBoxModel.WPFMessageBox.Show(
                            "Exit",
                            GameObject.GetInstance().HumanAirline == null
                                ? "Exit The Airline Project?"
                                : "Exit The Airline Project? Your game will be autosaved.",
                            GraphicsModel.UserControlModel.MessageBoxModel.WPFMessageBoxButtons.YesNo);

                        if (answer == GraphicsModel.UserControlModel.MessageBoxModel.WPFMessageBoxResult.Yes)
                        {
                            Close();
                        }
                    }
                    finally
                    {
                        _askingToExit = false;
                    }

                    return;
                }

                if (args.Key != System.Windows.Input.Key.Space || args.IsRepeat || GameObject.GetInstance().HumanAirline == null)
                {
                    return;
                }

                // let the space bar type normally in text boxes
                if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
                    || System.Windows.Input.Keyboard.FocusedElement is PasswordBox
                    || (System.Windows.Input.Keyboard.FocusedElement is ComboBox combo && combo.IsEditable))
                {
                    return;
                }

                args.Handled = true;
                await TogglePause();
            };

            Closing += async (o, args) =>
            {
                state.SaveState();

                // autosave a running game when the window is closed
                if (_autosaveDone || GameObject.GetInstance().HumanAirline == null)
                {
                    return;
                }

                args.Cancel = true;
                if (_autosaving)
                {
                    return;
                }

                _autosaving = true;
                try
                {
                    Title = "Saving...";
                    SavingOverlay.Visibility = Visibility.Visible;
                    // let the overlay and its animation draw before the save starts
                    await System.Threading.Tasks.Task.Delay(100);
                    GameObjectWorker.GetInstance().RequestCancel();
                    for (int i = 0; i < 300 && GameObjectWorker.GetInstance().IsBusy(); i++)
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                    }

                    await System.Threading.Tasks.Task.Run(() => SerializedLoadSaveHelpers.SaveGame("autosave"));
                }
                catch (Exception ex)
                {
                    StartupLog.Write("Autosave on close failed: " + ex);
                }

                _autosaveDone = true;
                Close();
            };

            //Setup.SetupGame();
        }

        private bool _showingSaving;

        /// <summary>
        ///     Runs a save on a background thread behind the "Saving your game..." screen. The screen stays up at least
        ///     a moment, so it doesn't flash on a quick save.
        /// </summary>
        public async System.Threading.Tasks.Task RunWithSavingScreen(Action work, string title, string subtitle)
        {
            if (_showingSaving)
            {
                return;
            }

            _showingSaving = true;
            try
            {
                SavingTitle.Text = title;
                SavingSubtitle.Text = subtitle ?? string.Empty;
                SavingSubtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
                SavingOverlay.Visibility = Visibility.Visible;

                var shown = System.Diagnostics.Stopwatch.StartNew();

                // let the overlay and its animation draw before the save starts
                await System.Threading.Tasks.Task.Delay(100);
                await System.Threading.Tasks.Task.Run(work);

                if (shown.ElapsedMilliseconds < 800)
                {
                    await System.Threading.Tasks.Task.Delay(800 - (int) shown.ElapsedMilliseconds);
                }
            }
            finally
            {
                SavingOverlay.Visibility = Visibility.Collapsed;
                SavingTitle.Text = "Saving your game...";
                SavingSubtitle.Text = "The game closes when the save is finished.";
                SavingSubtitle.Visibility = Visibility.Visible;
                _showingSaving = false;
            }
        }

        private bool _askingToExit;

        private bool _togglingPause;

        //pauses a running game or resumes a paused one (the space bar)
        private async System.Threading.Tasks.Task TogglePause()
        {
            if (_togglingPause)
            {
                return;
            }

            _togglingPause = true;
            try
            {
                var worker = GameObjectWorker.GetInstance();

                if (worker.IsPaused)
                {
                    worker.Restart();
                }
                else
                {
                    // ask the simulation to stop and wait without blocking the window thread
                    worker.RequestCancel();
                    for (int i = 0; i < 300 && worker.IsBusy(); i++)
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                    }

                    worker.IsPaused = true;
                }
            }
            catch (Exception ex)
            {
                StartupLog.Write("Pause toggle failed: " + ex);
            }
            finally
            {
                _togglingPause = false;
            }
        }

        private bool _autosaveDone;
        private bool _autosaving;

        public MainWindowViewModel ViewModel
        {
            get { return DataContext as MainWindowViewModel; }
            set { DataContext = value; }
        }

        public bool CanGoBack()
        {
            return frmContent.NavigationService.CanGoBack;
        }

        public bool ClearNavigator()
        {
            while (frmContent.NavigationService.CanGoBack)
            {
                frmContent.NavigationService.RemoveBackEntry();
            }
            return true;
        }

        public void NavigateForward()
        {
            if (frmContent.NavigationService.CanGoForward)
            {
                frmContent.NavigationService.GoForward();
            }
        }

        public void NavigateBack()
        {
            if (frmContent.NavigationService.CanGoBack)
            {
                frmContent.NavigationService.GoBack();
            }
        }

        public void NavigateTo(Page page)
        {
            // pages without their own background would show the start menu through them
            if (page.Background == null)
            {
                page.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0xC4, 0xC6));
            }
            frmContent.Visibility = Visibility.Visible;
            frmContent.Navigate(page);
            if (frmContent.NavigationService.CanGoBack)
            {
                frmContent.NavigationService.RemoveBackEntry();
            }
        }

        /// <summary>Hides the Page host and shows the start menu region again.</summary>
        public void ShowStartMenu()
        {
            ClearNavigator();
            frmContent.Content = null;
            frmContent.Visibility = Visibility.Collapsed;
        }
    }
}