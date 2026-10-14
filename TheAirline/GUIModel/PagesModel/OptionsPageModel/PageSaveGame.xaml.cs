using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TheAirline.GraphicsModel.UserControlModel.MessageBoxModel;
using TheAirline.GUIModel.CustomControlsModel;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Models.General;

namespace TheAirline.GUIModel.PagesModel.OptionsPageModel
{
    /// <summary>
    ///     Interaction logic for PageSaveGame.xaml
    /// </summary>
    public partial class PageSaveGame : Page, INotifyPropertyChanged
    {
        #region Constructors and Destructors

        public PageSaveGame()
        {
            Saves = new ObservableCollection<SaveFileInfo>();
            InitializeComponent();

            foreach (SaveFileInfo savedFile in LoadSaveHelpers.GetSavedGameInfos())
            {
                Saves.Add(savedFile);
            }
        }

        #endregion

        #region Public Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Public Properties

        public ObservableCollection<SaveFileInfo> Saves { get; set; }

        #endregion

        #region Methods

        private void NotifyPropertyChanged(String propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (null != handler)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void btnSaveGame_Click(object sender, RoutedEventArgs e)
        {
            Boolean gameworkerPaused = GameObjectWorker.GetInstance().IsPaused;

            GameObjectWorker.GetInstance().Cancel();

            string name = txtName.Text.Trim();

            Boolean doSave = true;

            if (SerializedLoadSaveHelpers.SaveGameExists(name))
            {
                WPFMessageBoxResult result = WPFMessageBox.Show(
                    Translator.GetInstance().GetString("MessageBox", "1007"),
                    Translator.GetInstance().GetString("MessageBox", "1007", "message"),
                    WPFMessageBoxButtons.YesNo);

                doSave = result == WPFMessageBoxResult.Yes;

                if (doSave)
                {
                    SerializedLoadSaveHelpers.DeleteSavedGame(name);
                }
            }

            if (doSave)
            {
                saveWithScreen(name, gameworkerPaused);
            }
            else if (!gameworkerPaused)
            {
                GameObjectWorker.GetInstance().Start();
            }
        }

        //saves behind the "Saving your game..." screen
        private async void saveWithScreen(string name, bool gameworkerPaused)
        {
            Exception error = null;

            await PageNavigator.MainWindow.RunWithSavingScreen(
                () =>
                {
                    try
                    {
                        GameObject.GetInstance().Name = name;

                        SerializedLoadSaveHelpers.SaveGame(name);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                },
                "Saving your game...",
                "\"" + name + "\"");

            if (!gameworkerPaused)
            {
                GameObjectWorker.GetInstance().Start();
            }

            if (error != null)
            {
                MessageBox.Show("Saving the game failed:\n" + error.Message, "TheAirline");
                return;
            }

            // show the new save in the list
            Saves.Clear();
            foreach (SaveFileInfo savedFile in LoadSaveHelpers.GetSavedGameInfos())
            {
                Saves.Add(savedFile);
            }
        }

        private void lbSaves_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = lbSaves.SelectedItem as SaveFileInfo;
            if (selected != null)
            {
                txtName.Text = selected.Name;
            }
        }

        #endregion
    }
}