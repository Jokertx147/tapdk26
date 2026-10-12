using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TheAirline.GraphicsModel.UserControlModel.PopUpWindowsModel;
using TheAirline.GUIModel.PagesModel.RoutesPageModel;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.GUIModel.CustomControlsModel.PopUpWindowsModel
{
    /// <summary>
    ///     Lets the player set up a route suggested by the advisory tab.
    /// </summary>
    public class PopUpAdvisedRoute : PopUpWindow
    {
        private const string NoAircraft = "None - assign later";

        private readonly Airport _origin;
        private readonly Airport _destination;
        private readonly List<FleetAirliner> _candidates;

        private ComboBox cbAircraft, cbTier, cbOperation, cbTurnaround, cbTrips, cbStart;
        private CheckBox chkStart;
        private TextBlock txtInfo;
        private StackPanel panelSchedule;
        private bool _updating;

        public static bool ShowPopUp(Airport origin, Airport destination)
        {
            var window = new PopUpAdvisedRoute(origin, destination);
            window.ShowDialog();

            return window.Selected != null;
        }

        public PopUpAdvisedRoute(Airport origin, Airport destination)
        {
            _origin = origin;
            _destination = destination;
            _candidates = RouteCreator.GetCandidateAirliners(origin, destination);

            Title = string.Format("Create route {0} - {1}", origin.Profile.IATACode, destination.Profile.IATACode);
            Width = 420;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.SingleBorderWindow;

            var main = new StackPanel { Margin = new Thickness(10) };

            cbAircraft = AddRow(main, "Aircraft");
            cbAircraft.Items.Add(NoAircraft);
            foreach (FleetAirliner f in _candidates)
            {
                cbAircraft.Items.Add(f);
            }
            cbAircraft.DisplayMemberPath = "Name";
            cbAircraft.SelectedIndex = _candidates.Count > 0 ? 1 : 0;

            cbTier = AddRow(main, "Service tier");
            foreach (ServiceTier t in Enum.GetValues(typeof (ServiceTier)))
            {
                cbTier.Items.Add(t);
            }
            cbTier.SelectedItem = ServiceTier.Standard;

            panelSchedule = new StackPanel();
            main.Children.Add(panelSchedule);

            cbOperation = AddRow(panelSchedule, "Schedule");
            cbOperation.Items.Add(OpsType.Regular);
            cbOperation.Items.Add(OpsType.Whole_Day);
            cbOperation.SelectedItem = OpsType.Regular;

            cbTurnaround = AddRow(panelSchedule, "Turnaround time (minutes)");
            foreach (int m in RouteCreator.TurnaroundOptions)
            {
                cbTurnaround.Items.Add(m);
            }

            cbTrips = AddRow(panelSchedule, "Round trips per day");
            cbStart = AddRow(panelSchedule, "First departure");

            chkStart = new CheckBox { Content = "Start the aircraft right away", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
            panelSchedule.Children.Add(chkStart);

            txtInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            main.Children.Add(txtInfo);

            SetDefaultTurnaround();
            Refresh();

            cbAircraft.SelectionChanged += (s, e) => { SetDefaultTurnaround(); Refresh(); };
            cbOperation.SelectionChanged += (s, e) => Refresh();
            cbTurnaround.SelectionChanged += (s, e) => Refresh();
            cbTrips.SelectionChanged += (s, e) => RefreshStartTimes();

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(MakeButton("Create route", btnOk_Click));
            buttons.Children.Add(MakeButton("Cancel", (s, e) => Close()));
            main.Children.Add(buttons);

            Content = main;
        }

        private ComboBox AddRow(Panel parent, string label)
        {
            parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
            cb.SetResourceReference(StyleProperty, "ComboBoxTransparentStyle");
            parent.Children.Add(cb);
            return cb;
        }

        private Button MakeButton(string text, RoutedEventHandler handler)
        {
            var b = new Button { Content = text, Height = double.NaN, Width = double.NaN, Margin = new Thickness(0, 0, 8, 0) };
            b.SetResourceReference(StyleProperty, "RoundedButton");
            b.SetResourceReference(BackgroundProperty, "ButtonBrush");
            b.Click += handler;
            return b;
        }

        private FleetAirliner SelectedAirliner
        {
            get { return cbAircraft.SelectedItem as FleetAirliner; }
        }

        private void SetDefaultTurnaround()
        {
            _updating = true;
            int minutes = 60;
            if (SelectedAirliner != null)
            {
                minutes = (int) FleetAirlinerHelpers.GetMinTimeBetweenFlights(SelectedAirliner).TotalMinutes;
            }
            int best = RouteCreator.TurnaroundOptions.FirstOrDefault(o => o >= minutes);
            cbTurnaround.SelectedItem = best == 0 ? RouteCreator.TurnaroundOptions.Last() : best;
            _updating = false;
        }

        private void Refresh()
        {
            if (_updating)
            {
                return;
            }

            FleetAirliner airliner = SelectedAirliner;
            panelSchedule.IsEnabled = airliner != null;

            if (airliner == null)
            {
                txtInfo.Text = _candidates.Count == 0
                    ? "None of your aircraft can fly this route (range or runway). The route is created without a schedule; assign an aircraft later."
                    : "The route is created without a schedule; assign an aircraft later.";
                return;
            }

            var type = airliner.Airliner.Type;
            int turnaround = (int) cbTurnaround.SelectedItem;
            var ops = (OpsType) cbOperation.SelectedItem;

            if (RouteCreator.IsLongRoute(_origin, _destination, type, turnaround))
            {
                cbTrips.IsEnabled = false;
                cbStart.IsEnabled = false;
                cbOperation.IsEnabled = false;
                txtInfo.Text = "This is a long-haul route, so a weekly schedule is created.";
                return;
            }

            cbTrips.IsEnabled = cbStart.IsEnabled = cbOperation.IsEnabled = true;

            int max = RouteCreator.GetMaxRoundTrips(_origin, _destination, type, turnaround, ops);
            _updating = true;
            cbTrips.Items.Clear();
            for (int i = 1; i <= max; i++)
            {
                cbTrips.Items.Add(i);
            }
            cbTrips.SelectedIndex = max - 1;
            _updating = false;

            RefreshStartTimes();
        }

        private void RefreshStartTimes()
        {
            if (_updating || SelectedAirliner == null || cbTrips.SelectedItem == null)
            {
                return;
            }

            var times = RouteCreator.GetStartTimes(
                _origin, _destination, SelectedAirliner.Airliner.Type,
                (int) cbTurnaround.SelectedItem, (int) cbTrips.SelectedItem, (OpsType) cbOperation.SelectedItem);

            cbStart.Items.Clear();
            foreach (TimeSpan t in times)
            {
                cbStart.Items.Add(t.ToString(@"hh\:mm"));
            }
            cbStart.SelectedIndex = 0;

            txtInfo.Text = "";
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            var request = new NewRouteRequest
            {
                Origin = _origin,
                Destination = _destination,
                Tier = (ServiceTier) cbTier.SelectedItem,
                Airliner = SelectedAirliner,
                StartAirliner = chkStart.IsChecked == true
            };

            if (request.Airliner != null)
            {
                request.TurnaroundMinutes = (int) cbTurnaround.SelectedItem;
                request.Operation = (OpsType) cbOperation.SelectedItem;
                request.RoundTripsPerDay = cbTrips.SelectedItem == null ? 1 : (int) cbTrips.SelectedItem;
                request.StartTime = cbStart.SelectedItem == null
                    ? new TimeSpan(6, 0, 0)
                    : TimeSpan.Parse((string) cbStart.SelectedItem);
            }

            string message;
            var route = RouteCreator.Create(request, out message);

            MessageBox.Show(message, Title);

            if (route != null)
            {
                Selected = route;
                Close();
            }
        }
    }
}
