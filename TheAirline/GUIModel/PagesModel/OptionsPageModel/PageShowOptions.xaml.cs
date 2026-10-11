using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TheAirline.GraphicsModel.UserControlModel.MessageBoxModel;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.General;
using Settings = TheAirline.Properties.Settings;

namespace TheAirline.GUIModel.PagesModel.OptionsPageModel
{
    /// <summary>
    ///     Interaction logic for PageShowOptions.xaml
    /// </summary>
    public partial class PageShowOptions : Page
    {
        #region Constructors and Destructors

        public PageShowOptions()
        {
            Options = new OptionsMVVM();
            DataContext = Options;

            Loaded += PageShowOptions_Loaded;

            InitializeComponent();
        }

        #endregion

        #region Public Properties

        public OptionsMVVM Options { get; set; }

        #endregion

        #region Methods

        private void btnSetMoney_Click(object sender, RoutedEventArgs e)
        {
            double amount;
            string text = (txtMoney.Text ?? "").Replace(",", "").Replace(" ", "").Trim();
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount))
            {
                MessageBox.Show("Enter the new amount as a number, for example 50000000.", "Set money");
                return;
            }

            GameObject game = GameObject.GetInstance();
            if (game.HumanAirline == null)
            {
                MessageBox.Show("There is no player airline. Start or load a game first.", "Set money");
                return;
            }

            game.HumanAirline.Money = amount;
            game.HumanMoney = amount;
            MessageBox.Show("Your airline's money is now " + amount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + ".", "Set money");
        }

        private void PageShowOptions_Loaded(object sender, RoutedEventArgs e)
        {
            setIntevalValues();
        }

        private void btnSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            WPFMessageBoxResult result = WPFMessageBox.Show(
                Translator.GetInstance().GetString("MessageBox", "2952"),
                Translator.GetInstance().GetString("MessageBox", "2952", "message"),
                WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                AppSettings.GetInstance().SetLanguage((Language)cbLanguage.SelectedItem);

                Infrastructure.Settings.GetInstance().AirportCodeDisplay = rbIATA.IsChecked.Value
                    ? AirportCode.Iata
                    : AirportCode.Icao;
                Infrastructure.Settings.GetInstance().MailsOnLandings = cbLandings.IsChecked.Value;
                Infrastructure.Settings.GetInstance().MailsOnBadWeather = cbWeather.IsChecked.Value;
                Infrastructure.Settings.GetInstance().MailsOnAirlineRoutes = cbAirlineDestinations.IsChecked.Value;
                Infrastructure.Settings.GetInstance().CurrencyShorten = cbShortenCurrency.IsChecked.Value;

                if (Options.HourRoundEnabled)
                {
                    Infrastructure.Settings.GetInstance().MinutesPerTurn = (int)cbHours.SelectedItem;
                }

                var gameSpeed =
                    (GeneralHelpers.GameSpeedValue)
                        Enum.ToObject(typeof(GeneralHelpers.GameSpeedValue), (int)slGameSpeed.Value);

                Infrastructure.Settings.GetInstance().SetGameSpeed(gameSpeed);

                List<RadioButton> rbAutoSaves = UIHelpers.FindRBChildren(this, "AutoSave");

                foreach (RadioButton rbInterval in rbAutoSaves)
                {
                    if (rbInterval.IsChecked.Value)
                    {
                        Infrastructure.Settings.GetInstance().AutoSave =
                            (Intervals)TheAirline.Infrastructure.DataParsing.ParseEnum(typeof(Intervals), rbInterval.Tag.ToString(), true);
                    }
                }

                List<RadioButton> rbClearings = UIHelpers.FindRBChildren(this, "ClearStats");

                foreach (RadioButton rbInterval in rbClearings)
                {
                    if (rbInterval.IsChecked.Value)
                    {
                        Infrastructure.Settings.GetInstance().ClearStats =
                            (Intervals)TheAirline.Infrastructure.DataParsing.ParseEnum(typeof(Intervals), rbInterval.Tag.ToString(), true);
                    }
                }
            }
        }

        private void btnUndo_Click(object sender, RoutedEventArgs e)
        {
            Options.undoChanges();
            setIntevalValues();
        }

        //sets the values of the interval types
        private void setIntevalValues()
        {
            List<RadioButton> rbAutoSaves = UIHelpers.FindRBChildren(this, "AutoSave");

            foreach (RadioButton rbInterval in rbAutoSaves)
            {
                if (rbInterval.Tag.ToString() == Options.AutoSave.ToString())
                {
                    rbInterval.IsChecked = true;
                }
            }

            List<RadioButton> rbClearings = UIHelpers.FindRBChildren(this, "ClearStats");

            foreach (RadioButton rbInterval in rbClearings)
            {
                if (rbInterval.Tag.ToString() == Options.ClearStats.ToString())
                {
                    rbInterval.IsChecked = true;
                }
            }
        }

        #endregion
    }
}