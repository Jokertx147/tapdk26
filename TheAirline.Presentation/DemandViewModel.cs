using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>One destination on the demand page: how many passengers and how much cargo want to go there.</summary>
    public class DemandRow : ObservableObject
    {
        private int _passengers;
        private int _cargo;

        public DemandRow(Airport origin, Airport destination, int passengers, int cargo, bool domestic)
        {
            Origin = origin;
            Destination = destination;
            _passengers = passengers;
            _cargo = cargo;
            OriginalPassengers = passengers;
            OriginalCargo = cargo;
            IsDomestic = domestic;
            Distance = MathHelpers.GetDistance(origin, destination);

            var airline = GameObject.GetInstance().HumanAirline;
            var p = destination.Profile;
            Name = p.IATACode + "  " + p.Name + ", " + p.Country?.Name;
            Kind = DemandViewModel.TypeName(p.Type) + ", " + DemandViewModel.SizeName(p.Size);
            HasContract = destination.AirlineContracts.Exists(c => c.Airline == airline);
            FreeGates = destination.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            HasRoute = airline.Routes.Any(
                r => (r.Destination1 == origin && r.Destination2 == destination) || (r.Destination1 == destination && r.Destination2 == origin));
        }

        public Airport Origin { get; }
        public Airport Destination { get; }
        public bool IsDomestic { get; }
        public double Distance { get; }
        public string Name { get; }
        public string Kind { get; }
        public bool HasContract { get; }
        public bool HasRoute { get; }
        public int FreeGates { get; }
        public int OriginalPassengers { get; }
        public int OriginalCargo { get; }

        public bool IsChanged => _passengers != OriginalPassengers || _cargo != OriginalCargo;

        public int Passengers
        {
            get { return _passengers; }
            set
            {
                value = Math.Max(0, Math.Min(ushort.MaxValue, value));
                if (SetProperty(ref _passengers, value))
                {
                    Origin.SetDestinationPassengersDemand(Destination, value);
                    Changed();
                }
            }
        }

        public int Cargo
        {
            get { return _cargo; }
            set
            {
                value = Math.Max(0, Math.Min(ushort.MaxValue, value));
                if (SetProperty(ref _cargo, value))
                {
                    Origin.SetDestinationCargoDemand(Destination, value);
                    Changed();
                }
            }
        }

        public string Numbers
        {
            get
            {
                return string.Format(
                    "{0} passengers  |  {1} cargo  |  {2}{3}",
                    _passengers.ToString("N0"), _cargo.ToString("N0"), DistanceFormatter.Km(Distance),
                    IsChanged ? "  |  edited" : "");
            }
        }

        public string Notes
        {
            get
            {
                var parts = new List<string> { IsDomestic ? "Domestic" : "International", Kind };
                if (HasRoute) parts.Add("you fly this route");
                parts.Add(HasContract ? "you have gates there" : FreeGates > 0 ? FreeGates + " free gate(s)" : "no free gates");
                return string.Join("  |  ", parts);
            }
        }

        private void Changed()
        {
            OnPropertyChanged(nameof(Numbers));
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    /// <summary>The airport demand page: where people and cargo from one airport want to go.</summary>
    public class DemandViewModel : ObservableObject
    {
        private readonly Action _changed;
        private readonly List<DemandRow> _rows = new List<DemandRow>();
        private List<Airport> _all;
        private string _search = "";
        private AirportItem _selected;
        private string _filter = "All";
        private string _sortBy = "Passengers";
        private string _destFilter = "";
        private DemandRow _selectedRow;
        private CreateRouteViewModel _create;
        private string _status;
        private string _summary;
        private int _scale = 120;
        private bool _onlyWithoutRoute;

        public DemandViewModel(Action changed)
        {
            _changed = changed;
            Airports = new ObservableCollection<AirportItem>();
            Rows = new ObservableCollection<DemandRow>();
            Filters = new List<string> { "All", "Domestic", "International" };
            SortOptions = new List<string> { "Passengers", "Cargo", "Distance", "Name" };
            ScaleSelectedCommand = new RelayCommand(() => Scale(_selectedRow == null ? new List<DemandRow>() : new List<DemandRow> { _selectedRow }));
            ScaleShownCommand = new RelayCommand(() => Scale(Rows.ToList()));
            UndoCommand = new RelayCommand(Undo);
            CreateCommand = new RelayCommand(OpenCreate, () => _selectedRow != null);
            ApplySearch();
            Selected = Airports.FirstOrDefault();
        }

        public ObservableCollection<AirportItem> Airports { get; }
        public ObservableCollection<DemandRow> Rows { get; }
        public List<string> Filters { get; }
        public List<string> SortOptions { get; }
        public List<int> ScaleChoices { get; } = new List<int> { 25, 50, 75, 90, 110, 120, 150, 200, 300 };

        public ICommand ScaleSelectedCommand { get; }
        public ICommand ScaleShownCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand CreateCommand { get; }

        public static string SizeName(GeneralHelpers.Size size)
        {
            return System.Text.RegularExpressions.Regex.Replace(size.ToString().Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").ToLower();
        }

        public static string TypeName(AirportProfile.AirportType type)
        {
            switch (type)
            {
                case AirportProfile.AirportType.LongHaulInternational: return "Long haul international";
                case AirportProfile.AirportType.ShortHaulInternational: return "Short haul international";
                case AirportProfile.AirportType.Domestic: return "Domestic";
                default: return "Regional";
            }
        }

        /// <summary>Filters the airport list by code, name, town or country; empty shows your own airports.</summary>
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
                    Load();
                }
            }
        }

        /// <summary>Shows the demand of this airport (used when another page picks an airport).</summary>
        public void ShowAirport(Airport airport)
        {
            if (airport == null) return;
            var item = Airports.FirstOrDefault(a => a.Airport == airport);
            if (item == null)
            {
                var own = new HashSet<Airport>(GameObject.GetInstance().HumanAirline.Airports);
                item = new AirportItem(airport, own.Contains(airport));
                Airports.Insert(0, item);
            }
            Selected = item;
        }

        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value ?? "All"))
                {
                    ApplyRows();
                }
            }
        }

        public string SortBy
        {
            get { return _sortBy; }
            set
            {
                if (SetProperty(ref _sortBy, value ?? "Passengers"))
                {
                    ApplyRows();
                }
            }
        }

        /// <summary>Narrows the destinations by code, name or country.</summary>
        public string DestinationFilter
        {
            get { return _destFilter; }
            set
            {
                if (SetProperty(ref _destFilter, value ?? ""))
                {
                    ApplyRows();
                }
            }
        }

        public bool OnlyWithoutRoute
        {
            get { return _onlyWithoutRoute; }
            set
            {
                if (SetProperty(ref _onlyWithoutRoute, value))
                {
                    ApplyRows();
                }
            }
        }

        public DemandRow SelectedRow
        {
            get { return _selectedRow; }
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    (CreateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int ScalePercent
        {
            get { return _scale; }
            set { SetProperty(ref _scale, Math.Max(1, Math.Min(1000, value))); }
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

        public bool HasCreate => _create != null;

        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public int RowCount => Rows.Count;

        /// <summary>Recalculates the page after the game changed (new routes, gates).</summary>
        public void Reload()
        {
            var keep = _selected?.Airport;
            ApplySearch();
            if (keep != null)
            {
                ShowAirport(keep);
                Load();
            }
        }

        private void ApplySearch()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null) return;
            var own = new HashSet<Airport>(airline.Airports);
            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Airport> list;
            string s = _search.Trim();
            if (s.Length == 0)
            {
                list = airline.Airports.OrderBy(a => a.Profile.Name);
            }
            else
            {
                list = _all.Where(
                    a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (a.Profile.Town != null && a.Profile.Town.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                         || (a.Profile.Country != null && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                     .OrderBy(a => string.Equals(a.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(a => a.Profile.Name);
            }

            Airports.Clear();
            foreach (var a in list.Take(150))
            {
                Airports.Add(new AirportItem(a, own.Contains(a)));
            }
        }

        private void Load()
        {
            _rows.Clear();
            if (_selected == null)
            {
                Summary = "";
                ApplyRows();
                return;
            }

            var origin = _selected.Airport;
            var resolver = new CountryCurrentCountryResolver();
            var home = resolver.Convert(origin.Profile.Country);

            foreach (var d in origin.GetDestinationDemands())
            {
                if (d == null || d == origin || !GeneralHelpers.IsAirportActive(d)) continue;
                int pax = origin.GetDestinationPassengersRate(d, AirlinerClass.ClassType.EconomyClass);
                int cargo = origin.GetDestinationCargoRate(d);
                if (pax == 0 && cargo == 0) continue;
                bool domestic = Equals(resolver.Convert(d.Profile.Country), home);
                _rows.Add(new DemandRow(origin, d, pax, cargo, domestic));
            }

            UpdateSummary();
            ApplyRows();
        }

        private void UpdateSummary()
        {
            if (_selected == null)
            {
                Summary = "";
                return;
            }
            var p = _selected.Airport.Profile;
            var dom = _rows.Where(r => r.IsDomestic).ToList();
            var intl = _rows.Where(r => !r.IsDomestic).ToList();
            Summary = string.Format(
                "{0} {1}  |  {2}, {3}{4}" + Environment.NewLine +
                "Domestic: {5} destinations, {6} passengers, {7} cargo" + Environment.NewLine +
                "International: {8} destinations, {9} passengers, {10} cargo",
                p.IATACode, p.Name, TypeName(p.Type), SizeName(p.Size),
                _rows.Count == 0 ? "" : "",
                dom.Count, dom.Sum(r => r.Passengers).ToString("N0"), dom.Sum(r => r.Cargo).ToString("N0"),
                intl.Count, intl.Sum(r => r.Passengers).ToString("N0"), intl.Sum(r => r.Cargo).ToString("N0"));
        }

        private void ApplyRows()
        {
            IEnumerable<DemandRow> list = _rows;
            if (_filter == "Domestic") list = list.Where(r => r.IsDomestic);
            else if (_filter == "International") list = list.Where(r => !r.IsDomestic);
            if (_onlyWithoutRoute) list = list.Where(r => !r.HasRoute);

            string s = _destFilter.Trim();
            if (s.Length > 0)
            {
                list = list.Where(
                    r => r.Destination.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || r.Destination.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (r.Destination.Profile.Country != null && r.Destination.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            switch (_sortBy)
            {
                case "Cargo": list = list.OrderByDescending(r => r.Cargo).ThenByDescending(r => r.Passengers); break;
                case "Distance": list = list.OrderBy(r => r.Distance); break;
                case "Name": list = list.OrderBy(r => r.Destination.Profile.Name); break;
                default: list = list.OrderByDescending(r => r.Passengers).ThenByDescending(r => r.Cargo); break;
            }

            var keep = _selectedRow;
            Rows.Clear();
            foreach (var r in list)
            {
                Rows.Add(r);
            }
            SelectedRow = keep != null && Rows.Contains(keep) ? keep : null;
            OnPropertyChanged(nameof(RowCount));
        }

        private void Scale(List<DemandRow> rows)
        {
            if (rows.Count == 0)
            {
                Status = "Pick a destination first.";
                return;
            }
            double factor = _scale / 100.0;
            foreach (var r in rows)
            {
                r.Passengers = (int) Math.Round(r.Passengers * factor);
                r.Cargo = (int) Math.Round(r.Cargo * factor);
            }
            UpdateSummary();
            Status = string.Format("Changed the demand to {0} destination(s) to {1}%. It is kept in your save.", rows.Count, _scale);
        }

        private void Undo()
        {
            int changed = 0;
            foreach (var r in _rows.Where(r => r.IsChanged))
            {
                r.Passengers = r.OriginalPassengers;
                r.Cargo = r.OriginalCargo;
                changed++;
            }
            UpdateSummary();
            Status = changed == 0 ? "Nothing to undo." : string.Format("Restored {0} destination(s).", changed);
        }

        private void OpenCreate()
        {
            if (_selected == null || _selectedRow == null) return;
            bool cargo = _selectedRow.Passengers == 0 && _selectedRow.Cargo > 0;
            Create = new CreateRouteViewModel(_selected.Airport, _selectedRow.Destination, OnCreateDone, cargo);
        }

        private void OnCreateDone(string message)
        {
            Create = null;
            if (message != null)
            {
                Status = message;
                _changed?.Invoke();
            }
        }
    }
}
