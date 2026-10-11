using System.Collections.ObjectModel;
using System.ComponentModel;
using System;
using System.Windows;
using System.Windows.Controls;
using TheAirline.GraphicsModel.UserControlModel.MessageBoxModel;
using TheAirline.GUIModel.CustomControlsModel;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.General;
using TheAirline.Models.General.Holidays;
using TheAirline.Views.Airline;

namespace TheAirline.GUIModel.PagesModel.OptionsPageModel
{
    /// <summary>
    ///     Interaction logic for PageLoadGame.xaml
    /// </summary>
    public partial class PageLoadGame : Page
    {
        #region Constructors and Destructors

        public PageLoadGame()
        {
            Saves = new ObservableCollection<string>();

            InitializeComponent();

            foreach (string savedFile in LoadSaveHelpers.GetSavedGames())
            {
                Saves.Add(savedFile);
            }
        }

        #endregion

        #region Public Properties

        public ObservableCollection<string> Saves { get; set; }

        #endregion

        #region Methods

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            GUIModel.HelpersModel.PageNavigator.MainWindow.ShowStartMenu();
        }

        private void btnDeleteGame_Click(object sender, RoutedEventArgs e)
        {
            var file = (string)lbSaves.SelectedItem;
            WPFMessageBoxResult result = WPFMessageBox.Show(
                Translator.GetInstance().GetString("MessageBox", "1009"),
                Translator.GetInstance().GetString("MessageBox", "1009", "message"),
                WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                SerializedLoadSaveHelpers.DeleteSavedGame(file);

                Saves.Remove(file);
            }
        }

        private void btnLoadGame_Click(object sender, RoutedEventArgs e)
        {
            // the wait for the running game to stop happens on the background thread below; spinning here would
            // block the UI thread, and the game worker may need the UI thread to finish
            GameObjectWorker.GetInstance().Cancel();

            var file = (string)lbSaves.SelectedItem;

            WPFMessageBoxResult result = WPFMessageBox.Show(
                Translator.GetInstance().GetString("MessageBox", "1002"),
                Translator.GetInstance().GetString("MessageBox", "1002", "message"),
                WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                if (file != null)
                {
                    var scLoading = UIHelpers.FindChild<SplashControl>(this, "scLoading");

                    try
                    {
                        // the game data (airports, airliners, countries ...) must be loaded before a save can be restored
                        TheAirline.Infrastructure.GameDataLoader.EnsureLoaded();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not load the game data:\n" + ex, "TheAirline");
                        return;
                    }

                    scLoading.Visibility = Visibility.Visible;

                    var bgWorker = new BackgroundWorker();
                    bgWorker.DoWork += (s, x) =>
                    {
                        TheAirline.Infrastructure.StartupLog.Write("Load: waiting for the running game to stop");
                        var waited = System.Diagnostics.Stopwatch.StartNew();
                        while (GameObjectWorker.GetInstance().IsBusy() && waited.Elapsed < TimeSpan.FromSeconds(30))
                        {
                            System.Threading.Thread.Sleep(50);
                        }
                        TheAirline.Infrastructure.StartupLog.Write("Load: game stopped after " + waited.ElapsedMilliseconds + " ms, loading");
                        SerializedLoadSaveHelpers.LoadGame(file);
                    };
                    bgWorker.RunWorkerCompleted += (s, x) =>
                    {
                        scLoading.Visibility = Visibility.Collapsed;

                        if (x.Error != null)
                        {
                            TheAirline.Infrastructure.StartupLog.Write("Load failed: " + x.Error);
                            MessageBox.Show("Loading the game failed:\n" + x.Error, "TheAirline");
                            return;
                        }

                        HolidayYear.Clear();

                        GeneralHelpers.CreateHolidays(GameObject.GetInstance().GameTime.Year);

                        Setup.SetupMergers();

                        GameObjectWorker.GetInstance().Pause();

                        PageNavigator.NavigateTo(new PageAirline(GameObject.GetInstance().HumanAirline));
                    };
                    bgWorker.RunWorkerAsync();
                }
            }
        }

        #endregion
    }
}