using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TheAirline.Helpers;
using TheAirline.Models.Airports;
using TheAirline.Models.General.Countries.Towns;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class AirportItem
    {
        public AirportItem(Airport airport, bool own, Airport near = null)
        {
            Airport = airport;
            Text = airport.Profile.IATACode + "  " + airport.Profile.Name + ", " + airport.Profile.Country?.Name;
            IsOwn = own;
            if (near != null && near != airport && airport.Profile.Coordinates != null && near.Profile.Coordinates != null)
            {
                Distance = MathHelpers.GetDistance(near, airport);
                DistanceText = DistanceFormatter.Km(Distance.Value);
                Details = DistanceText + " from " + near.Profile.IATACode;
            }
            else if (near == airport) Details = "the airport you are measuring from";
        }

        public Airport Airport { get; }
        public string Text { get; }
        public bool IsOwn { get; }

        /// <summary>How far from the airport the list is sorted around, when there is one.</summary>
        public double? Distance { get; }

        /// <summary>Just the distance, for a narrow column.</summary>
        public string DistanceText { get; } = "";

        public string Details { get; }
        public bool HasDetails => !string.IsNullOrEmpty(Details);
    }

    public class AdviceItem
    {
        public AdviceItem(CargoAdvice a)
        {
            Destination = a.Destination;
            var f = new ValueCurrencyFormatter();
            Name = a.Destination.Profile.IATACode + "  " + a.Destination.Profile.Name + ", " + a.Destination.Profile.Country?.Name;
            Numbers = string.Format(
                "{0}  |  cargo demand {1}  |  {2} competitor(s)  |  ~{3}/day{4}",
                DistanceFormatter.Km(a.Distance), a.Demand, a.Competitors, f.Convert(a.RevenuePerDay),
                a.FleetCanFly ? "" : "  |  no cargo aircraft of yours can fly it");
            Notes = a.Advice;
        }

        public AdviceItem(RouteAdvice a)
        {
            Advice = a;
            Destination = a.Destination;
            var f = new ValueCurrencyFormatter();
            Name = a.Destination.Profile.IATACode + "  " + a.Destination.Profile.Name + ", " + a.Destination.Profile.Country?.Name;
            Numbers = string.Format(
                "{0}  |  {1} pax/day  |  {2} competitor(s)  |  ~{3}/day{4}",
                DistanceFormatter.Km(a.Distance), a.DailyDemand, a.Competitors, f.Convert(a.RevenuePerDay),
                a.FleetCanFly ? "" : "  |  no aircraft of yours can fly it");
            Notes = a.Advice;
        }

        public RouteAdvice Advice { get; }
        public Airport Destination { get; }
        public string Name { get; }
        public string Numbers { get; }
        public string Notes { get; }
    }

    /// <summary>Airports tab: pick an airport, see the advisor's suggested routes, create one.</summary>
    /// <summary>An airport found when searching for any destination, not only the recommended ones.</summary>
    public class DestinationItem
    {
        public DestinationItem(Airport origin, Airport airport)
        {
            Airport = airport;
            double km = MathHelpers.GetDistance(origin, airport);
            Distance = km;
            Name = airport.Profile.IATACode + "  " + airport.Profile.Name + ", " + airport.Profile.Country?.Name;
            Details = DistanceFormatter.Km(km);
        }

        public Airport Airport { get; }
        public double Distance { get; }
        public string Name { get; }
        public string Details { get; }
    }

    public class AirportsViewModel : ObservableObject
    {
        private string _destSearch = "";
        private DestinationItem _selectedDestination;
        private readonly Action _changed;
        private string _search = "";
        private AirportItem _selected;
        private AdviceItem _selectedAdvice;
        private CreateRouteViewModel _create;
        private string _status;
        private bool _busy;
        private List<Airport> _all;
        private bool _cargo;
        private string _near = "";
        private Airport _nearAirport;

        public AirportsViewModel(Action changed)
        {
            _changed = changed;
            Airports = new ObservableCollection<AirportItem>();
            Advice = new ObservableCollection<AdviceItem>();
            Destinations = new ObservableCollection<DestinationItem>();
            // a cargo airline plans cargo routes, a passenger airline passenger routes
            _cargo = AirlineTypeRules.IsCargoAirline(GameObject.GetInstance()?.HumanAirline);
            ApplySearch();
            Selected = Airports.FirstOrDefault();
        }

        public ObservableCollection<AirportItem> Airports { get; }
        public ObservableCollection<AdviceItem> Advice { get; }
        public ObservableCollection<DestinationItem> Destinations { get; }

        /// <summary>
        ///     Sorts the airports by how far they are from this one, nearest first (a code, a name, a town; empty sorts
        ///     by name again). Useful for finding what is near a hub, or which of your airports is closest to somewhere.
        /// </summary>
        public string Near
        {
            get { return _near; }
            set
            {
                if (SetProperty(ref _near, value ?? ""))
                {
                    _nearAirport = Resolve(_near);
                    OnPropertyChanged(nameof(NearStatus));
                    OnPropertyChanged(nameof(HasNear));
                    ApplySearch();
                }
            }
        }

        /// <summary>The airport the list is sorted around, or null.</summary>
        public Airport NearAirport => _nearAirport;

        public bool HasNear => !string.IsNullOrEmpty(_near.Trim());

        public string NearStatus
        {
            get
            {
                if (!HasNear) return "";
                if (_nearAirport == null) return "No airport found for \"" + _near.Trim() + "\".";
                return "Nearest first from " + _nearAirport.Profile.IATACode + "  " + _nearAirport.Profile.Name + ".";
            }
        }

        /// <summary>The airport a typed code or name means: an exact code wins, else the first match by name.</summary>
        public Airport Resolve(string text)
        {
            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();
            return AirportSearch.Resolve(text, _all);
        }

        /// <summary>Search any airport as the destination of a new route (code, name or country), recommended or not.</summary>
        public string DestinationSearch
        {
            get { return _destSearch; }
            set
            {
                if (SetProperty(ref _destSearch, value ?? ""))
                {
                    OnPropertyChanged(nameof(IsSearchingDestinations));
                    OnPropertyChanged(nameof(ShowAdvice));
                    ApplyDestinationSearch();
                }
            }
        }

        public bool IsSearchingDestinations => _destSearch.Trim().Length > 0;
        public bool ShowAdvice => !IsSearchingDestinations;

        public DestinationItem SelectedDestination
        {
            get { return _selectedDestination; }
            set
            {
                if (SetProperty(ref _selectedDestination, value) && value != null && _selected != null && value.Airport != _selected.Airport)
                {
                    Create = new CreateRouteViewModel(_selected.Airport, value.Airport, OnCreateDone, _cargo);
                }
            }
        }

        private void ApplyDestinationSearch()
        {
            Destinations.Clear();
            _selectedDestination = null;
            OnPropertyChanged(nameof(SelectedDestination));
            if (_selected == null || !IsSearchingDestinations)
            {
                return;
            }

            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();
            string s = _destSearch.Trim();
            var origin = _selected.Airport;
            var found = _all.Where(a => a != origin && a.Profile.Coordinates != null && Matches(a, s))
                            .Select(a => new DestinationItem(origin, a))
                            .OrderBy(d => string.Equals(d.Airport.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                            .ThenBy(d => d.Distance)
                            .Take(80);
            foreach (var d in found)
            {
                Destinations.Add(d);
            }
        }


        /// <summary>Whether the search text matches the airport (code, name, town, state or country).</summary>
        public static bool Matches(Airport a, string s) => AirportSearch.Matches(a, s);

        /// <summary>Filters by IATA code, name, town, state or country; empty shows the airline's own airports.</summary>
        public string Search
        {
            get { return _search; }
            set
            {
                if (SetProperty(ref _search, value ?? ""))
                {
                    ApplySearch();
                }
            }
        }

        public AirportItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    Create = null;
                    ApplyDestinationSearch();
                    LoadAdvice();
                }
            }
        }

        /// <summary>True when the suggestions and the new route are for cargo instead of passengers.</summary>
        public bool CargoMode
        {
            get { return _cargo; }
            set
            {
                var me = GameObject.GetInstance()?.HumanAirline;
                string blocker = me == null ? null : AirlineTypeRules.RouteBlocker(me, value ? Route.RouteType.Cargo : Route.RouteType.Passenger);
                if (blocker != null)
                {
                    Status = blocker;
                    OnPropertyChanged(nameof(CargoMode));
                    OnPropertyChanged(nameof(PassengerMode));
                    return;
                }
                if (SetProperty(ref _cargo, value))
                {
                    OnPropertyChanged(nameof(PassengerMode));
                    OnPropertyChanged(nameof(AdviceTitle));
                    Create = null;
                    LoadAdvice();
                }
            }
        }

        public bool PassengerMode
        {
            get { return !_cargo; }
            set { CargoMode = !value; }
        }

        public string AdviceTitle
        {
            get { return _cargo ? "Suggested cargo routes" : "Suggested routes"; }
        }

        public AdviceItem SelectedAdvice
        {
            get { return _selectedAdvice; }
            set
            {
                if (SetProperty(ref _selectedAdvice, value) && value != null && _selected != null)
                {
                    Create = new CreateRouteViewModel(_selected.Airport, value.Destination, OnCreateDone, _cargo);
                }
            }
        }

        public CreateRouteViewModel Create
        {
            get { return _create; }
            private set
            {
                if (SetProperty(ref _create, value))
                {
                    OnPropertyChanged(nameof(HasCreate));
                }
            }
        }

        public bool HasCreate
        {
            get { return _create != null; }
        }

        public bool IsBusy
        {
            get { return _busy; }
            private set { SetProperty(ref _busy, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>Opens the new route form for two airports (used by the connections page).</summary>
        public void PlanRoute(Airport from, Airport to)
        {
            if (AirlineTypeRules.CanFlyRoute(GameObject.GetInstance().HumanAirline, Route.RouteType.Passenger)) CargoMode = false;
            var item = Airports.FirstOrDefault(a => a.Airport == from);
            if (item != null) Selected = item;
            Create = new CreateRouteViewModel(from, to, OnCreateDone, false);
        }

        private void OnCreateDone(string message)
        {
            Create = null;
            if (message != null)
            {
                Status = message;
                _changed();
            }
            _selectedAdvice = null;
            OnPropertyChanged(nameof(SelectedAdvice));
            _selectedDestination = null;
            OnPropertyChanged(nameof(SelectedDestination));
        }

        private void ApplySearch()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            var own = new HashSet<Airport>(airline.Airports);
            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Airport> list;
            if (_search.Trim().Length == 0)
            {
                list = airline.Airports.OrderBy(a => a.Profile.Name);
            }
            else
            {
                string s = _search.Trim();
                list = _all.Where(a => Matches(a, s));
            }

            // sorted around an airport when one is given, so the nearest 150 are the ones kept
            if (_nearAirport != null)
                list = list.Where(a => a.Profile.Coordinates != null)
                           .OrderBy(a => a == _nearAirport ? 0 : 1)
                           .ThenBy(a => MathHelpers.GetDistance(_nearAirport, a));

            Airports.Clear();
            foreach (var a in list.Take(150))
            {
                Airports.Add(new AirportItem(a, own.Contains(a), _nearAirport));
            }
        }

        /// <summary>Recalculates the suggestions (after the fleet or routes changed).</summary>
        public void ReloadAdvice()
        {
            // the airline's kind decides the kind of route (it can change on the profile page while there are no routes)
            var me = GameObject.GetInstance()?.HumanAirline;
            if (me != null && !AirlineTypeRules.CanFlyRoute(me, _cargo ? Route.RouteType.Cargo : Route.RouteType.Passenger))
            {
                _cargo = !_cargo;
                OnPropertyChanged(nameof(CargoMode));
                OnPropertyChanged(nameof(PassengerMode));
                OnPropertyChanged(nameof(AdviceTitle));
            }
            OnPropertyChanged(nameof(CanChooseKind));
            OnPropertyChanged(nameof(KindText));
            LoadAdvice(false);
        }

        /// <summary>Only an airline that flies both kinds picks passenger or cargo routes here.</summary>
        public bool CanChooseKind
        {
            get
            {
                var me = GameObject.GetInstance()?.HumanAirline;
                return me != null && AirlineTypeRules.CanFlyRoute(me, Route.RouteType.Cargo) && AirlineTypeRules.CanFlyRoute(me, Route.RouteType.Passenger);
            }
        }

        public string KindText
        {
            get
            {
                var me = GameObject.GetInstance()?.HumanAirline;
                if (me == null || CanChooseKind) return null;
                return AirlineTypeRules.IsCargoAirline(me) ? "Cargo routes (a cargo airline)" : "Passenger routes (a passenger airline)";
            }
        }

        private int _loadId;

        private async void LoadAdvice(bool announce = true)
        {
            Advice.Clear();
            if (_selected == null)
            {
                return;
            }

            var airport = _selected.Airport;
            int id = ++_loadId;
            IsBusy = true;
            if (announce)
            {
                Status = "Looking for routes from " + airport.Profile.IATACode + "...";
            }

            List<AdviceItem> advice;
            bool cargo = _cargo;
            try
            {
                advice = await Task.Run(
                    () => cargo
                        ? CargoAdvisor.GetAdvice(airport).Select(a => new AdviceItem(a)).ToList()
                        : RouteAdvisor.GetAdvice(airport).Select(a => new AdviceItem(a)).ToList());
            }
            catch (Exception e)
            {
                IsBusy = false;
                Status = "Could not work out route advice: " + e.Message;
                return;
            }

            if (id != _loadId || cargo != _cargo)
            {
                return; // a newer request replaced this one
            }

            IsBusy = false;
            if (_selected == null || _selected.Airport != airport)
            {
                return; // the player picked another airport meanwhile
            }

            foreach (var a in advice)
            {
                Advice.Add(a);
            }

            if (announce || advice.Count == 0)
            {
                Status = advice.Count == 0
                    ? "No " + (cargo ? "cargo " : "") + "routes to suggest from " + airport.Profile.IATACode + "."
                    : null;
            }
        }
    }
}
