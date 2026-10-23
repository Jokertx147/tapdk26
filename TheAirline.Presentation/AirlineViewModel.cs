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
        public bool IsIdle { get; set; }
        public bool IsOnOrder { get; set; }
        public string Crew { get; set; }
        public string Note { get; set; }
        public bool CanStart { get; set; }
        public bool CanStop { get; set; }
        public bool CanEditCabin { get; set; }
        public ICommand EditCabinCommand { get; set; }
        public ICommand StartCommand { get; set; }
        public ICommand StopCommand { get; set; }
        public bool CanSchedule { get; set; }
        public ICommand ScheduleCommand { get; set; }
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
                new SpeedChoice {Name = "Slowest (75 s / month)", Value = GeneralHelpers.GameSpeedValue.Slowest},
                new SpeedChoice {Name = "Slow (60 s / month)", Value = GeneralHelpers.GameSpeedValue.Slow},
                new SpeedChoice {Name = "Normal (45 s / month)", Value = GeneralHelpers.GameSpeedValue.Normal},
                new SpeedChoice {Name = "Fast (30 s / month)", Value = GeneralHelpers.GameSpeedValue.Fast},
                new SpeedChoice {Name = "Fastest (15 s / month)", Value = GeneralHelpers.GameSpeedValue.Fastest},
                new SpeedChoice {Name = "Turbo (as fast as possible)", Value = GeneralHelpers.GameSpeedValue.Turbo}
            };
            var current = TheAirline.Infrastructure.Settings.GetInstance().GameSpeed;
            _speed = Speeds.First(s => s.Value == current);

            TogglePauseCommand = new RelayCommand(TogglePause);
            SaveCommand = new AsyncCommand(SaveAsync);
            GameSession.InGame = true;
            DateDisplay.Formatter = DateFormatter.Date;
            DateFormatter.Changed += OnDateFormatChanged;
            DistanceFormatter.Changed += OnDateFormatChanged;
            MainMenuCommand = new AsyncCommand(
                async () =>
                    {
                        Status = GameSession.AutoSaveOnExit ? "Saving..." : Status;
                        await System.Threading.Tasks.Task.Run(() => GameSession.AutoSaveIfNeeded());
                        GameSession.InGame = false;
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
            Maintenance = new MaintenanceViewModel(OnGameChanged);
            Charter = new CharterViewModel(OnGameChanged);
            Deals = new AircraftDealsViewModel(OnGameChanged);
            License = new LicenseViewModel(OnGameChanged);
            Demand = new DemandViewModel(OnGameChanged);
            Ratings = new RatingsViewModel();
            Profile = new ProfileViewModel(OnGameChanged);
            Advertising = new AdvertisingViewModel(OnGameChanged);
            AirportStats = new AirportStatsViewModel();
            FleetPerformance = new FleetPerformanceViewModel();
            CompareTypes = new CompareTypesViewModel();
            NewAircraft.CompareRequested = t =>
                {
                    CompareTypes.AddType(t);
                    AirlinersTab = 2;
                };
            Connections = new ConnectionsViewModel((a, b) =>
                {
                    Airports.PlanRoute(a, b);
                    RoutesTab = 1;
                });
            AirportManager.StatsRequested = a =>
                {
                    AirportStats.ShowAirport(a);
                    AirportsTab = 3;
                };
            GateContracts = new GateContractsViewModel(OnGameChanged);
            GateContracts.OpenAirport = a =>
                {
                    AirportManager.SelectAirport(a);
                    AirportsTab = 0;
                };
            AirportManager.DemandRequested = a =>
                {
                    Demand.ShowAirport(a);
                    AirportsTab = 1;
                };
            Insurance = new InsuranceViewModel(OnGameChanged);
            Services = new AirlineServicesViewModel(OnGameChanged);
            FlightSchools = new FlightSchoolsViewModel(OnGameChanged);
            Contracts = new ContractsViewModel(OnGameChanged);
            Timetable = new TimetableViewModel(OnGameChanged);
            Alliances = new AlliancesViewModel(OnGameChanged);
            AirlineList = new AirlinesViewModel();
            Codeshares = new CodesharesViewModel(OnGameChanged);
            Shares = new SharesViewModel(OnGameChanged);
            Subsidiaries = new SubsidiariesViewModel(OnGameChanged);
            Options = new OptionsViewModel(shell, null, MainMenuCommand);
            News = new NewsViewModel();
            Map = new WorldMapViewModel();
            Overview = new FleetOverviewViewModel(OnGameChanged, f => Refit = new CabinRefitViewModel(f, CloseRefit), OpenSchedule);
            Refresh();
        }

        /// <summary>The pages of the menu on the left, in the order of the buttons.</summary>
        public static readonly string[] PageNames =
            { "My Airline", "Routes", "Airliners", "Airports", "Alliances", "Staff", "Airlines", "Flights", "News", "Options" };

        private int _page;
        private CabinRefitViewModel _refit;

        /// <summary>The cabin editor of an owned aircraft, or null when none is open.</summary>
        public CabinRefitViewModel Refit
        {
            get { return _refit; }
            private set
            {
                if (SetProperty(ref _refit, value))
                {
                    OnPropertyChanged(nameof(HasRefit));
                }
            }
        }

        public bool HasRefit => _refit != null;

        private void CloseRefit()
        {
            Refit = null;
            OnGameChanged();
        }

        public string[] Pages => PageNames;

        private void OnDateFormatChanged()
        {
            // the screens built their texts with the old format
            _financeDate = null;
            _signature = null;
            OnGameChanged();
        }

        private int _flightsTab;

        /// <summary>The sub page of Flights: 0 fleet, 1 timetable, 2 all flights.</summary>
        public int FlightsTab
        {
            get { return _flightsTab; }
            set { SetProperty(ref _flightsTab, value); }
        }

        /// <summary>Go to the scheduler of an aircraft (Flights > Edit timetable).</summary>
        public void OpenSchedule(FleetAirliner f)
        {
            Timetable.Select(f);
            Page = Array.IndexOf(PageNames, "Flights");
            FlightsTab = 1;
        }

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
                case "My Airline": FleetPerformance.Reload(); break;
                case "Airports": AirportStats.Reload(); break;
                case "Routes": Connections.Reload(); break;
                case "Alliances": Alliances.Reload(); break;
                case "Airlines": AirlineList.Reload(); Codeshares.Reload(); Shares.Reload(); Subsidiaries.Reload(); break;
                case "News": News.Reload(); break;
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
        public MaintenanceViewModel Maintenance { get; }
        public CharterViewModel Charter { get; }
        public AircraftDealsViewModel Deals { get; }
        public LicenseViewModel License { get; }
        public DemandViewModel Demand { get; }
        public RatingsViewModel Ratings { get; }
        public ProfileViewModel Profile { get; }
        public AdvertisingViewModel Advertising { get; }
        public AirportStatsViewModel AirportStats { get; }
        public FleetPerformanceViewModel FleetPerformance { get; }
        public CompareTypesViewModel CompareTypes { get; }

        private int _airlinersTab, _routesTab;

        /// <summary>The open sub tab under Routes: 0 my routes, 1 create route, 2 connections.</summary>
        public int RoutesTab
        {
            get { return _routesTab; }
            set { SetProperty(ref _routesTab, value); }
        }

        public ConnectionsViewModel Connections { get; }

        /// <summary>The open sub tab under Airliners: 0 new, 1 used, 2 compare.</summary>
        public int AirlinersTab
        {
            get { return _airlinersTab; }
            set { SetProperty(ref _airlinersTab, value); }
        }
        public GateContractsViewModel GateContracts { get; }

        private string _logoPath;

        /// <summary>The airline's logo file for the top bar, or null.</summary>
        public string LogoPath
        {
            get { return _logoPath; }
            private set { SetProperty(ref _logoPath, value); }
        }

        private int _airportsTab;

        /// <summary>The open sub tab under Airports: 0 manage, 1 demand.</summary>
        public int AirportsTab
        {
            get { return _airportsTab; }
            set { SetProperty(ref _airportsTab, value); }
        }
        public InsuranceViewModel Insurance { get; }
        public AirlineServicesViewModel Services { get; }
        public FlightSchoolsViewModel FlightSchools { get; }
        public ContractsViewModel Contracts { get; }
        public TimetableViewModel Timetable { get; }
        public AlliancesViewModel Alliances { get; }
        public AirlinesViewModel AirlineList { get; }
        public CodesharesViewModel Codeshares { get; }
        public SharesViewModel Shares { get; }
        public SubsidiariesViewModel Subsidiaries { get; }
        public OptionsViewModel Options { get; }
        public NewsViewModel News { get; }
        public FleetOverviewViewModel Overview { get; }
        public WorldMapViewModel Map { get; }

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
            Maintenance?.Reload();
            Insurance?.Reload();
            Services?.Reload();
            FlightSchools?.Reload();
            Contracts?.Reload();
            Charter?.Reload();
            Deals?.Reload();
            License?.Reload();
            Demand?.Reload();
            Ratings?.Reload();
            GateContracts?.Reload();
            if (GameObject.GetInstance()?.HumanAirline != null) LogoPath = AirlineProfileService.GetLogo(GameObject.GetInstance().HumanAirline);
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

        private string _airlineName;
        public string AirlineName
        {
            get { return _airlineName; }
            private set { SetProperty(ref _airlineName, value); }
        }

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
            Status = FleetOverviewViewModel.TryStart(f);
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

            if (AirlineName != airline.Profile.Name || _logoPath == null) LogoPath = AirlineProfileService.GetLogo(airline);
            AirlineName = airline.Profile.Name;
            Date = DateFormatter.Date(game.GameTime);
            if (_financeDate != Date)
            {
                _financeDate = Date;
                Finance.Refresh();

                // loan payments are taken once a month
                if (_loanMonth != game.GameTime.Month)
                {
                    _loanMonth = game.GameTime.Month;
                    Loans.Reload();
                    Maintenance.Reload();
                    Insurance.Reload();
                    Contracts.Reload();
                    Ratings.Reload();
                    GateContracts.Reload();
                    Advertising.Reload();
                    AirportStats.Reload();
                    FleetPerformance.Reload();
                }
                Charter.Reload();
                Deals.Reload();
                // a computer airline may have sent a codeshare offer
                if (Codeshares.Offers.Count != CodeshareService.Offers(airline).Count) Codeshares.Reload();
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
                                ? "Delivery " + DateFormatter.Date(f.Airliner.BuiltDate)
                                : f.Status.ToString(),
                            Homebase = f.Homebase?.Profile.IATACode,
                            Routes = f.Routes.Count,
                            IsIdle = f.Routes.Count == 0 && f.Airliner.BuiltDate <= game.GameTime,
                            IsOnOrder = f.Airliner.BuiltDate > game.GameTime,
                            Crew = f.Pilots.Count + "/" + f.Airliner.Type.CockpitCrew + " pilots",
                            CanStart = f.Status == FleetAirliner.AirlinerStatus.Stopped && f.Airliner.BuiltDate <= game.GameTime,
                            CanStop = f.Status != FleetAirliner.AirlinerStatus.Stopped && f.Airliner.BuiltDate <= game.GameTime,
                            Note = f.Routes.Count == 0 ? "no route yet" : null,
                            CanEditCabin = f.Status == FleetAirliner.AirlinerStatus.Stopped && f.Airliner.Type is AirlinerPassengerType,
                            EditCabinCommand = new RelayCommand(() => Refit = new CabinRefitViewModel(f, CloseRefit)),
                            StartCommand = new RelayCommand(() => StartAirliner(f)),
                            StopCommand = new RelayCommand(() => StopAirliner(f)),
                            CanSchedule = f.Airliner.BuiltDate <= game.GameTime,
                            ScheduleCommand = new RelayCommand(() => OpenSchedule(f))
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
            Overview?.Reload();
            Map?.Reload();
            News?.Poll();
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
                            Name = r.Destination1.Profile.IATACode + " - " + r.Destination2.Profile.IATACode + (r.IsCargoRoute ? " (cargo)" : ""),
                            Distance = DistanceFormatter.Km(MathHelpers.GetDistance(r.Destination1, r.Destination2))
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
            DateFormatter.Changed -= OnDateFormatChanged;
            DistanceFormatter.Changed -= OnDateFormatChanged;
        }
    }
}
