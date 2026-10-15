using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class FleetItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Homebase { get; set; }
        public int Routes { get; set; }
        public string Crew { get; set; }
        public string Note { get; set; }
        public bool CanStart { get; set; }
        public bool CanStop { get; set; }
        public ICommand StartCommand { get; set; }
        public ICommand StopCommand { get; set; }
    }

    public class RouteItem : ObservableObject
    {
        private string _balance;

        public Route Route { get; set; }
        public string Name { get; set; }
        public string Distance { get; set; }

        public string Balance
        {
            get { return _balance; }
            set { SetProperty(ref _balance, value); }
        }
    }

    public class FlightItem
    {
        public string Flight { get; set; }
        public string Route { get; set; }
        public string When { get; set; }
        public string Aircraft { get; set; }
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
                        // the game changes the loaded data, so it is read again for the next game
                        TheAirline.Infrastructure.GameDataLoader.Invalidate();
                        _shell.ShowMainMenu();
                    });
            Market = new MarketViewModel(OnGameChanged);
            NewAircraft = new NewAircraftViewModel(OnGameChanged);
            Airports = new AirportsViewModel(OnGameChanged);
            Pilots = new PilotsViewModel(OnGameChanged);
            Finance = new FinanceViewModel();
            AirportManager = new AirportManagerViewModel(OnGameChanged);
            Loans = new LoansViewModel(OnGameChanged);
            Timetable = new TimetableViewModel(OnGameChanged);
            Alliances = new AlliancesViewModel(OnGameChanged);
            AirlineList = new AirlinesViewModel();
            Options = new OptionsViewModel(shell, null, MainMenuCommand);
            Refresh();
        }

        /// <summary>The pages of the menu on the left, in the order of the buttons.</summary>
        public static readonly string[] PageNames =
            { "My Airline", "Routes", "Airliners", "Airports", "Alliances", "Staff", "Airlines", "Flights", "Options" };

        private int _page;

        public string[] Pages => PageNames;

        /// <summary>The index of the page shown.</summary>
        public int Page
        {
            get { return _page; }
            set
            {
                if (value >= 0 && value < PageNames.Length && SetProperty(ref _page, value))
                {
                    OnPageShown(value);
                }
            }
        }

        // pages that show data the game changes on its own are read again when they are opened
        private void OnPageShown(int page)
        {
            switch (PageNames[page])
            {
                case "Alliances": Alliances.Reload(); break;
                case "Airlines": AirlineList.Reload(); break;
                case "Options": Options.Reload(); break;
            }
        }

        public MarketViewModel Market { get; }
        public NewAircraftViewModel NewAircraft { get; }
        public AirportsViewModel Airports { get; }
        public PilotsViewModel Pilots { get; }
        public FinanceViewModel Finance { get; }
        public AirportManagerViewModel AirportManager { get; }
        public LoansViewModel Loans { get; }
        public TimetableViewModel Timetable { get; }
        public AlliancesViewModel Alliances { get; }
        public AirlinesViewModel AirlineList { get; }
        public OptionsViewModel Options { get; }

        private RouteItem _selectedRoute;
        private RouteDetailViewModel _routeDetail;

        public RouteItem SelectedRoute
        {
            get { return _selectedRoute; }
            set
            {
                if (SetProperty(ref _selectedRoute, value))
                {
                    RouteDetail = value == null ? null : new RouteDetailViewModel(value.Route, OnGameChanged);
                }
            }
        }

        public RouteDetailViewModel RouteDetail
        {
            get { return _routeDetail; }
            private set
            {
                if (SetProperty(ref _routeDetail, value))
                {
                    OnPropertyChanged(nameof(HasRouteDetail));
                }
            }
        }

        public bool HasRouteDetail
        {
            get { return _routeDetail != null; }
        }
        private string _financeDate;
        private int _loanMonth;

        private void OnGameChanged()
        {
            Airports.ReloadAdvice();
            Pilots.Reload();
            AirportManager?.Reload();
            Loans?.Reload();
            Timetable?.ReloadAircraft();
            _financeDate = null;
            Refresh();
        }

        public SpeedChoice[] Speeds { get; }
        public ObservableCollection<FleetItem> Fleet { get; }
        public ObservableCollection<RouteItem> Routes { get; }
        public ObservableCollection<FlightItem> Flights { get; } = new ObservableCollection<FlightItem>();

        public string Summary { get; private set; }

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

        private void StartAirliner(FleetAirliner f)
        {
            if (f.Airliner.BuiltDate > GameObject.GetInstance().GameTime)
            {
                Status = f.Name + " has not been delivered yet (" + f.Airliner.BuiltDate.ToString("d MMM yyyy") + ").";
            }
            else if (f.Routes.Count == 0)
            {
                Status = f.Name + " has no route. Create a route for it first.";
            }
            else if (f.Pilots.Count < f.Airliner.Type.CockpitCrew)
            {
                Status = f.Name + " needs " + f.Airliner.Type.CockpitCrew + " pilots (it has " + f.Pilots.Count + "). Hire them under Airline > Pilots.";
            }
            else
            {
                f.Status = GameObject.GetInstance().DayRoundEnabled
                    ? FleetAirliner.AirlinerStatus.OnRoute
                    : FleetAirliner.AirlinerStatus.ToRouteStart;
                Status = f.Name + " is flying.";
            }

            Refresh();
        }

        private void StopAirliner(FleetAirliner f)
        {
            f.Status = FleetAirliner.AirlinerStatus.Stopped;
            Status = f.Name + " stopped.";
            Refresh();
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
            if (_financeDate != Date)
            {
                _financeDate = Date;
                Finance.Refresh();

                // loan payments are taken once a month
                if (_loanMonth != game.GameTime.Month)
                {
                    _loanMonth = game.GameTime.Month;
                    Loans.Reload();
                }
            }
            Money = Convert.ToString(new ValueCurrencyFormatter().Convert(airline.Money));
            IsRunning = !GameObjectWorker.GetInstance().IsPaused;

            var fleet = airline.Fleet.ToList();
            var routes = airline.Routes.ToList();
            var entries = routes.SelectMany(r => r.TimeTable.Entries).ToList();
            string signature = string.Join(",", fleet.Select(f => f.Name + f.Status + f.Routes.Count + f.Pilots.Count + (f.Airliner.BuiltDate > game.GameTime ? "u" : "")))
                               + "|" + routes.Count + "|" + entries.Count;
            if (signature != _signature)
            {
                _signature = signature;
                Timetable?.ReloadAircraft();

                Fleet.Clear();
                foreach (var f in fleet)
                {
                    Fleet.Add(
                        new FleetItem
                        {
                            Name = f.Name,
                            Type = f.Airliner?.Type?.Name,
                            Status = f.Airliner.BuiltDate > game.GameTime
                                ? "Delivery " + f.Airliner.BuiltDate.ToString("d MMM yyyy")
                                : f.Status.ToString(),
                            Homebase = f.Homebase?.Profile.IATACode,
                            Routes = f.Routes.Count,
                            Crew = f.Pilots.Count + "/" + f.Airliner.Type.CockpitCrew + " pilots",
                            CanStart = f.Status == FleetAirliner.AirlinerStatus.Stopped && f.Airliner.BuiltDate <= game.GameTime,
                            CanStop = f.Status != FleetAirliner.AirlinerStatus.Stopped && f.Airliner.BuiltDate <= game.GameTime,
                            Note = f.Routes.Count == 0 ? "no route yet" : null,
                            StartCommand = new RelayCommand(() => StartAirliner(f)),
                            StopCommand = new RelayCommand(() => StopAirliner(f))
                        });
                }

                Flights.Clear();
                foreach (var e in entries.OrderBy(e => e.Day == DayOfWeek.Sunday ? 7 : (int) e.Day).ThenBy(e => e.Time).Take(600))
                {
                    Flights.Add(
                        new FlightItem
                        {
                            Flight = e.Destination?.FlightCode,
                            Route = e.DepartureAirport?.Profile.IATACode + " - " + e.Destination?.Airport?.Profile.IATACode,
                            When = e.Day + " " + e.Time.ToString(@"hh\:mm"),
                            Aircraft = e.Airliner?.Name
                        });
                }

                Summary = string.Format(
                    "{0} aircraft, {1} routes, {2} weekly flights, {3} airports",
                    fleet.Count, routes.Count, entries.Count, airline.Airports.Count);
                OnPropertyChanged(nameof(Summary));
            }

            RefreshRoutes(routes);
            _routeDetail?.RefreshNumbers();
        }

        // the route list is only rebuilt when routes are added or removed, so the selection and typing in the fare boxes stay
        private void RefreshRoutes(List<Route> routes)
        {
            var money = new ValueCurrencyFormatter();
            bool same = routes.Count == Routes.Count && routes.Select((r, i) => Routes[i].Route == r).All(x => x);

            if (!same)
            {
                var keep = _selectedRoute?.Route;
                Routes.Clear();
                foreach (var r in routes)
                {
                    Routes.Add(
                        new RouteItem
                        {
                            Route = r,
                            Name = r.Destination1.Profile.IATACode + " - " + r.Destination2.Profile.IATACode,
                            Distance = string.Format("{0:0} km", MathHelpers.GetDistance(r.Destination1, r.Destination2))
                        });
                }

                var again = Routes.FirstOrDefault(x => x.Route == keep);
                if (again != null)
                {
                    SelectedRoute = again;
                }
                else if (keep != null)
                {
                    SelectedRoute = null;
                }
            }

            foreach (var item in Routes)
            {
                item.Balance = "profit " + Convert.ToString(money.Convert(item.Route.Balance));
            }
        }

        public void Dispose()
        {
        }
    }
}
