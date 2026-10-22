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
    public class TypeChoice
    {
        public AirlinerType Type { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>One aircraft type's column in the comparison.</summary>
    public class CompareColumn
    {
        public AirlinerType Type { get; set; }
        public List<string> Values { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    /// <summary>Airliners > Compare types: up to four aircraft types side by side, and what a flight would cost.</summary>
    public class CompareTypesViewModel : ObservableObject
    {
        private const int MaxTypes = 4;
        private readonly List<AirlinerType> _picked = new List<AirlinerType>();
        private string _search = "", _distance = "1500", _route = "", _status;
        private TypeChoice _selectedChoice;

        public CompareTypesViewModel()
        {
            Choices = new ObservableCollection<TypeChoice>();
            Columns = new ObservableCollection<CompareColumn>();
            AddCommand = new RelayCommand(Add, () => _selectedChoice != null);
            ClearCommand = new RelayCommand(() => { _picked.Clear(); Build(); });
            LoadChoices();
            Build();
        }

        public ObservableCollection<TypeChoice> Choices { get; }
        public ObservableCollection<CompareColumn> Columns { get; }
        public ICommand AddCommand { get; }
        public ICommand ClearCommand { get; }
        public bool HasColumns => Columns.Count > 0;

        public List<string> Labels { get; } = new List<string>
        {
            "Maker", "Kind", "Price new", "Seats / cargo", "Range", "Speed", "Runway needed", "Crew",
            "Fuel per seat, 100 km", "In production", "Flight time", "Fuel per full flight", "Fuel per seat", "Other costs per flight", "Can fly it?"
        };

        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) LoadChoices(); }
        }

        public TypeChoice SelectedChoice
        {
            get => _selectedChoice;
            set { if (SetProperty(ref _selectedChoice, value)) (AddCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
        }

        /// <summary>The flight distance in km for the cost rows.</summary>
        public string Distance
        {
            get => _distance;
            set { if (SetProperty(ref _distance, value ?? "")) Build(); }
        }

        /// <summary>A route such as ATL-LHR; when set, its distance and runways are used.</summary>
        public string Route
        {
            get => _route;
            set { if (SetProperty(ref _route, value ?? "")) Build(); }
        }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }

        /// <summary>Handling, maintenance, en-route charges, crew, sales and overhead for one flight, 80% full at the usual fare.</summary>
        private static double OtherCosts(AirlinerType t, double km)
        {
            var me = GameObject.GetInstance().HumanAirline;
            var a = new Airliner("compare", t, "N-COMPARE", GameObject.GetInstance().GameTime) { Airline = me };
            double hours = AircraftHistoryService.FlightTime(t, km).TotalHours;
            double pax = AircraftHistoryService.Capacity(t)*0.8;
            double fare = FlightCosts.UsualFare(km);
            return FlightCosts.Calculate(a, km, hours, pax*fare).Total;
        }

        private static Airport FindAirport(string code)
        {
            string c = (code ?? "").Trim().ToUpperInvariant();
            return Airports.GetAllAirports().FirstOrDefault(a => a.Profile.IATACode == c)
                   ?? Airports.GetAllAirports().FirstOrDefault(a => a.Profile.ICAOCode == c);
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        /// <summary>Adds a type to the comparison (used by the new aircraft page too).</summary>
        public void AddType(AirlinerType t)
        {
            if (t == null) return;
            if (_picked.Contains(t))
            {
                Status = t.Name + " is already in the comparison.";
                return;
            }
            if (_picked.Count >= MaxTypes) _picked.RemoveAt(0);
            _picked.Add(t);
            Status = null;
            Build();
        }

        private void Add()
        {
            AddType(_selectedChoice?.Type);
        }

        private void LoadChoices()
        {
            string s = _search.Trim();
            Choices.Clear();
            foreach (var t in AircraftHistoryService.AvailableTypes()
                                                  .Where(t => s.Length == 0 || t.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                                              || (t.Manufacturer?.Name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                                                  .Take(200))
            {
                Choices.Add(new TypeChoice
                {
                    Type = t,
                    Text = t.Name + "  |  " + AircraftHistoryService.Capacity(t) + (t.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo ? " t cargo" : " seats") + ", " + t.Range.ToString("N0") + " km"
                });
            }
        }

        private void Build()
        {
            double km = 0;
            Airport a = null, b = null;
            var parts = _route.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                a = FindAirport(parts[0]);
                b = FindAirport(parts[1]);
                if (a != null && b != null) km = MathHelpers.GetDistance(a, b);
                else a = b = null;
            }
            if (km <= 0) double.TryParse(_distance.Replace(",", ""), out km);
            if (km <= 0) km = 1500;

            Columns.Clear();
            var now = GameObject.GetInstance().GameTime;
            foreach (var t in _picked.ToList())
            {
                var type = t;
                int cap = AircraftHistoryService.Capacity(t);
                bool cargo = t.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo;
                double fuel = AircraftHistoryService.FuelPerFlight(t, km);
                string cannot = AircraftHistoryService.CanFly(t, km, a, b);
                int crew = t.CockpitCrew + (t is AirlinerPassengerType p ? p.CabinCrew : 0);
                Columns.Add(new CompareColumn
                {
                    Type = t,
                    RemoveCommand = new RelayCommand(() => { _picked.Remove(type); Build(); }),
                    Values = new List<string>
                    {
                        t.Manufacturer?.Name ?? "",
                        cargo ? "Cargo" : t.TypeAirliner == AirlinerType.TypeOfAirliner.Helicopter ? "Helicopter" : "Passenger",
                        Money(t.Price),
                        cap.ToString("N0") + (cargo ? " t" : " seats"),
                        t.Range.ToString("N0") + " km",
                        t.CruisingSpeed.ToString("N0") + " km/h",
                        t.MinRunwaylength.ToString("N0") + " m",
                        crew + (t.CockpitCrew > 0 ? " (" + t.CockpitCrew + " pilots)" : ""),
                        Money(AircraftHistoryService.FuelPerSeat100(t)),
                        t.Produced.From.Year + " to " + (t.Produced.To.Year > now.Year + 50 ? "now" : t.Produced.To.Year.ToString()),
                        AircraftHistoryService.FlightTime(t, km).ToString(@"h\:mm"),
                        Money(fuel),
                        cap > 0 ? Money(fuel/cap) : "",
                        Money(OtherCosts(t, km)),
                        cannot == null ? "Yes" : "No: " + cannot
                    }
                });
            }
            OnPropertyChanged(nameof(HasColumns));
            OnPropertyChanged(nameof(DistanceText));
        }

        public string DistanceText
        {
            get
            {
                var parts = _route.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var a = FindAirport(parts[0]);
                    var b = FindAirport(parts[1]);
                    if (a != null && b != null) return string.Format("{0} to {1}: {2:N0} km. Runways are checked too.", a.Profile.IATACode, b.Profile.IATACode, MathHelpers.GetDistance(a, b));
                    return "Route not found; type two airport codes such as ATL-LHR.";
                }
                return "Using the distance above. Type a route such as ATL-LHR to use its distance and check its runways.";
            }
        }
    }
}
