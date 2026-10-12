using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class FleetItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Homebase { get; set; }
        public int Routes { get; set; }
    }

    public class RouteItem
    {
        public string Name { get; set; }
        public string Distance { get; set; }
        public string Balance { get; set; }
    }

    public class SpeedChoice
    {
        public string Name { get; set; }
        public GeneralHelpers.GameSpeedValue Value { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>The main screen of a running game: clock, money, fleet and routes of the human airline.</summary>
    public class AirlineViewModel : ObservableObject, IDisposable
    {
        private readonly ShellViewModel _shell;
        private string _date, _money, _status;
        private bool _isRunning;
        private SpeedChoice _speed;
        private string _signature;

        public AirlineViewModel(ShellViewModel shell)
        {
            _shell = shell;
            Fleet = new ObservableCollection<FleetItem>();
            Routes = new ObservableCollection<RouteItem>();
            Speeds = new[]
            {
                new SpeedChoice {Name = "Slowest", Value = GeneralHelpers.GameSpeedValue.Slowest},
                new SpeedChoice {Name = "Slow", Value = GeneralHelpers.GameSpeedValue.Slow},
                new SpeedChoice {Name = "Normal", Value = GeneralHelpers.GameSpeedValue.Normal},
                new SpeedChoice {Name = "Fast", Value = GeneralHelpers.GameSpeedValue.Fast},
                new SpeedChoice {Name = "Fastest", Value = GeneralHelpers.GameSpeedValue.Fastest}
            };
            var current = TheAirline.Infrastructure.Settings.GetInstance().GameSpeed;
            _speed = Speeds.First(s => s.Value == current);

            TogglePauseCommand = new RelayCommand(TogglePause);
            SaveCommand = new AsyncCommand(SaveAsync);
            MainMenuCommand = new RelayCommand(
                () =>
                    {
                        GameSession.StopClock();
                        _shell.ShowMainMenu();
                    });
            Market = new MarketViewModel(OnGameChanged);
            Airports = new AirportsViewModel(OnGameChanged);
            Refresh();
        }

        public MarketViewModel Market { get; }
        public AirportsViewModel Airports { get; }

        private void OnGameChanged()
        {
            Airports.ReloadAdvice();
            Refresh();
        }

        public SpeedChoice[] Speeds { get; }
        public ObservableCollection<FleetItem> Fleet { get; }
        public ObservableCollection<RouteItem> Routes { get; }

        public ICommand TogglePauseCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand MainMenuCommand { get; }

        public string AirlineName { get; private set; }

        public string Date
        {
            get { return _date; }
            private set { SetProperty(ref _date, value); }
        }

        public string Money
        {
            get { return _money; }
            private set { SetProperty(ref _money, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(PauseText));
                }
            }
        }

        public string PauseText
        {
            get { return IsRunning ? "Pause" : "Play"; }
        }

        public SpeedChoice Speed
        {
            get { return _speed; }
            set
            {
                if (value != null && SetProperty(ref _speed, value))
                {
                    TheAirline.Infrastructure.Settings.GetInstance().SetGameSpeed(value.Value);
                }
            }
        }

        private void TogglePause()
        {
            var worker = GameObjectWorker.GetInstance();
            if (worker.IsPaused)
            {
                worker.Restart();
            }
            else
            {
                worker.Pause();
            }
            Refresh();
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            string name = GameSession.SafeFileName(airline?.Profile?.Name ?? "save");
            Status = "Saving...";
            await GameSession.SaveGameAsync(name);
            Status = "Saved as " + name;
            Refresh();
        }

        /// <summary>Called by the UI on a timer (the game thread changes the data, so the view must poll).</summary>
        public void Refresh()
        {
            var game = GameObject.GetInstance();
            var airline = game?.HumanAirline;
            if (airline == null)
            {
                return;
            }

            AirlineName = airline.Profile.Name;
            Date = game.GameTime.ToString("d MMMM yyyy");
            Money = Convert.ToString(new ValueCurrencyFormatter().Convert(airline.Money));
            IsRunning = !GameObjectWorker.GetInstance().IsPaused;

            var fleet = airline.Fleet.ToList();
            var routes = airline.Routes.ToList();
            string signature = string.Join(",", fleet.Select(f => f.Name + f.Status + f.Routes.Count))
                               + "|" + routes.Count;
            if (signature != _signature)
            {
                _signature = signature;

                Fleet.Clear();
                foreach (var f in fleet)
                {
                    Fleet.Add(
                        new FleetItem
                        {
                            Name = f.Name,
                            Type = f.Airliner?.Type?.Name,
                            Status = f.Status.ToString(),
                            Homebase = f.Homebase?.Profile.IATACode,
                            Routes = f.Routes.Count
                        });
                }

                Routes.Clear();
                foreach (var r in routes)
                {
                    Routes.Add(
                        new RouteItem
                        {
                            Name = r.Destination1.Profile.IATACode + " - " + r.Destination2.Profile.IATACode,
                            Distance = string.Format("{0:0} km", MathHelpers.GetDistance(r.Destination1, r.Destination2)),
                            Balance = "profit " + Convert.ToString(new ValueCurrencyFormatter().Convert(r.Balance))
                        });
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
