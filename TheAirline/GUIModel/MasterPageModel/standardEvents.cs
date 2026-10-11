using System.Windows;
using TheAirline.GraphicsModel.UserControlModel.MessageBoxModel;
using TheAirline.GUIModel.CustomControlsModel.PopUpWindowsModel;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.GUIModel.PagesModel.AirlinersPageModel;
using TheAirline.GUIModel.PagesModel.AirlinesPageModel;
using TheAirline.GUIModel.PagesModel.AirportsPageModel;
using TheAirline.GUIModel.PagesModel.AlliancesPageModel;
using TheAirline.GUIModel.PagesModel.GamePageModel;
using TheAirline.GUIModel.PagesModel.OptionsPageModel;
using TheAirline.GUIModel.PagesModel.PilotsPageModel;
using TheAirline.GUIModel.PagesModel.RoutesPageModel;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.General;
using TheAirline.Views.Airline;
using TheAirline.Views.Game;

namespace TheAirline.GUIModel.MasterPageModel
{
    public partial class standardEvents
    {
        #region Methods

        private void Airliners_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageAirliners());
        }

        private void Airlines_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageAirlines());
        }

        private void Airports_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageAirports());
        }

        private void Alliances_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageAlliances());
        }

        private void Flights_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageFlights());
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageAirline(GameObject.GetInstance().HumanAirline));
        }

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageOptions());
        }

        private void Pilots_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PagePilotsFS());
        }

        private void RoutesManager_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageRoutes());
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateBack();
        }
        private void btnEvent_Click(object sender, RoutedEventArgs e)
        {
            PopUpSpecialContracts.ShowPopUp();
 
        }

        private void btnCalendar_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageCalendar());
        }

        private void btnExitGame_Click(object sender, RoutedEventArgs e)
        {
            WPFMessageBoxResult result = WPFMessageBox.Show(
                Translator.GetInstance().GetString("MessageBox", "1003"),
                Translator.GetInstance().GetString("MessageBox", "1003", "message"),
                WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                PageNavigator.MainWindow.Close();
            }
        }

        //goes back to the main menu; the game that is running is dropped, so unsaved progress is lost
        private async void btnNewGame_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Return to the main menu? Progress since your last save will be lost.",
                "Main menu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // ask the simulation to stop and wait without blocking the window (it needs the window thread to finish)
            GameObjectWorker.GetInstance().RequestCancel();
            for (int i = 0; i < 300 && GameObjectWorker.GetInstance().IsBusy(); i++)
            {
                await System.Threading.Tasks.Task.Delay(50);
            }

            GameObject.RestartInstance();
            GameDataLoader.Invalidate();
            PageNavigator.MainWindow.ShowStartMenu();
        }

        private void btnNews_Click(object sender, RoutedEventArgs e)
        {
            PageNavigator.NavigateTo(new PageNews());
        }

        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            GameObjectWorker.GetInstance().Pause();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            GameObjectWorker.GetInstance().Restart();
        }

        #endregion
    }
}