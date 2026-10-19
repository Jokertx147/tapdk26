using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TheAirline.Helpers;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class AirportAirlineRow
    {
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
        public bool IsMine { get; set; }
    }

    public class HourBar
    {
        public string Hour { get; set; }
        public int Count { get; set; }
        public int Mine { get; set; }
        public double AllWidth { get; set; }
        public double MineWidth { get; set; }
        public string Label { get; set; }
    }

    public class MovementRow
    {
        public string Time { get; set; }
        public string Text { get; set; }
        public bool IsMine { get; set; }
        public string Marker => IsMine ? "●" : "";
    }

    /// <summary>Airports > Traffic: passengers, airlines, top destinations, the weekly schedule and the gates of an airport.</summary>
    public class AirportStatsViewModel : ObservableObject
    {
        private List<Airport> _all;
        private string _search = "";
        private AirportItem _selected;
        private string _summary, _schedulePeak;
        private string _day;
        private bool _onlyMine;
        private List<AirportMovement> _movements = new List<AirportMovement>();

        private static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        public AirportStatsViewModel()
        {
            Airports = new ObservableCollection<AirportItem>();
            AirlineRows = new ObservableCollection<AirportAirlineRow>();
            Destinations = new ObservableCollection<string>();
            Hours = new ObservableCollection<HourBar>();
            Movements = new ObservableCollection<MovementRow>();
            GateLines = new ObservableCollection<string>();
            _day = DayNames[((int) DateTime.Today.DayOfWeek + 6)%7];
            ApplySearch();
            Selected = Airports.FirstOrDefault();
        }

        public ObservableCollection<AirportItem> Airports { get; }
        public ObservableCollection<AirportAirlineRow> AirlineRows { get; }
        public ObservableCollection<string> Destinations { get; }
        public ObservableCollection<HourBar> Hours { get; }
        public ObservableCollection<MovementRow> Movements { get; }
        public ObservableCollection<string> GateLines { get; }
        public List<string> Days => DayNames.ToList();

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string SchedulePeak { get => _schedulePeak; private set => SetProperty(ref _schedulePeak, value); }
        public bool HasDestinations => Destinations.Count > 0;

        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) ApplySearch(); }
        }

        public AirportItem Selected
        {
            get => _selected;
            set { if (SetProperty(ref _selected, value)) Load(); }
        }

        public string Day
        {
            get => _day;
            set { if (SetProperty(ref _day, value ?? DayNames[0])) LoadSchedule(); }
        }

        public bool OnlyMine
        {
            get => _onlyMine;
            set { if (SetProperty(ref _onlyMine, value)) LoadSchedule(); }
        }

        /// <summary>Shows an airport (used by the Manage tab).</summary>
        public void ShowAirport(Airport airport)
        {
            if (airport == null) return;
            var item = Airports.FirstOrDefault(a => a.Airport == airport);
            if (item == null)
            {
                item = new AirportItem(airport, GameObject.GetInstance().HumanAirline.Airports.Contains(airport));
                Airports.Insert(0, item);
            }
            if (_selected == item) Load();
            else Selected = item;
        }

        public void Reload()
        {
            if (_selected != null) Load();
        }

        private void ApplySearch()
        {
            var airline = GameObject.GetInstance()?.HumanAirline;
            if (airline == null) return;
            var own = new HashSet<Airport>(airline.Airports);
            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();
            string s = _search.Trim();
            IEnumerable<Airport> list = s.Length == 0
                ? airline.Airports.OrderBy(a => a.Profile.Name)
                : _all.Where(a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                                  || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                  || (a.Profile.Town != null && a.Profile.Town.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                      .OrderBy(a => string.Equals(a.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                      .ThenBy(a => a.Profile.Name);
            Airports.Clear();
            foreach (var a in list.Take(150)) Airports.Add(new AirportItem(a, own.Contains(a)));
        }

        private void Load()
        {
            AirlineRows.Clear();
            Destinations.Clear();
            GateLines.Clear();
            if (_selected == null)
            {
                Summary = "";
                _movements = new List<AirportMovement>();
                LoadSchedule();
                return;
            }

            var airport = _selected.Airport;
            var me = GameObject.GetInstance().HumanAirline;
            int year = GameObject.GetInstance().GameTime.Year;

            var stats = AirportStatsService.Airlines(airport);
            double pax = AirportStatsService.Passengers(airport, year), paxLast = AirportStatsService.Passengers(airport, year - 1);
            var mine = stats.FirstOrDefault(s => s.Airline == me);
            double myShare = pax > 0 && mine != null ? mine.PassengersThisYear/pax*100 : 0;
            _movements = AirportStatsService.Movements(airport);
            int weekly = _movements.Count;

            Summary = string.Format(
                "{0}  {1}, {2}  |  {3}, {4}" + Environment.NewLine +
                "Passengers this year {5:N0} (last year {6:N0})  |  {7} airline(s)  |  {8} scheduled arrivals and departures a week" + Environment.NewLine +
                "You: {9}",
                airport.Profile.IATACode, airport.Profile.Name, airport.Profile.Country?.Name,
                DemandViewModel.TypeName(airport.Profile.Type), DemandViewModel.SizeName(airport.Profile.Size),
                pax, paxLast, stats.Count, weekly,
                mine == null ? "you don't fly here" : string.Format("{0:N0} passengers this year ({1:0.#}% of the airport), {2} route(s), {3} departures a week", mine.PassengersThisYear, myShare, mine.Routes, mine.DeparturesPerWeek));

            foreach (var s in stats)
            {
                double share = pax > 0 ? s.PassengersThisYear/pax*100 : 0;
                AirlineRows.Add(new AirportAirlineRow
                {
                    LogoPath = AirlineProfileService.GetLogo(s.Airline),
                    Name = s.Airline.Profile.Name + " (" + s.Airline.Profile.IATACode + ")" + (s.Airline == me ? "  ← you" : ""),
                    Numbers = string.Format(
                        "{0:N0} passengers this year ({1:0.#}%)  |  last year {2:N0}  |  {3} route(s), {4} departures a week  |  gates {5}{6}",
                        s.PassengersThisYear, share, s.PassengersLastYear, s.Routes, s.DeparturesPerWeek, s.PassengerGates,
                        s.CargoGates > 0 ? " + " + s.CargoGates + " cargo" : ""),
                    IsMine = s.Airline == me
                });
            }

            foreach (var d in AirportStatsService.TopDestinations(airport, 15))
                Destinations.Add(string.Format("{0}  {1}, {2}  |  {3:N0} passengers", d.Key.Profile.IATACode, d.Key.Profile.Name, d.Key.Profile.Country?.Name, d.Value));
            OnPropertyChanged(nameof(HasDestinations));

            foreach (var line in AirportStatsService.GateLines(airport)) GateLines.Add(line);

            LoadSchedule();
        }

        private void LoadSchedule()
        {
            var me = GameObject.GetInstance()?.HumanAirline;
            var day = (DayOfWeek) ((Array.IndexOf(DayNames, _day) + 1)%7);
            var dayMoves = _movements.Where(m => m.Day == day).ToList();
            var all = AirportStatsService.PerHour(dayMoves, day);
            var mineHours = AirportStatsService.PerHour(dayMoves.Where(m => m.Airline == me), day);
            int max = Math.Max(1, all.Max());

            Hours.Clear();
            for (int h = 0; h < 24; h++)
            {
                Hours.Add(new HourBar
                {
                    Hour = h.ToString("00") + ":00",
                    Count = all[h],
                    Mine = mineHours[h],
                    AllWidth = 260.0*all[h]/max,
                    MineWidth = 260.0*mineHours[h]/max,
                    Label = all[h] == 0 ? "" : all[h] + (mineHours[h] > 0 ? " (yours " + mineHours[h] + ")" : "")
                });
            }
            int peak = Array.IndexOf(all, all.Max());
            SchedulePeak = dayMoves.Count == 0
                ? "No scheduled flights on " + _day + "."
                : string.Format("{0} arrivals and departures on {1}; the busiest hour is {2:00}:00 with {3}.", dayMoves.Count, _day, peak, all[peak]);

            Movements.Clear();
            foreach (var m in dayMoves.Where(m => !_onlyMine || m.Airline == me).Take(400))
            {
                Movements.Add(new MovementRow
                {
                    Time = m.Time.ToString(@"hh\:mm"),
                    IsMine = m.Airline == me,
                    Text = string.Format(
                        "{0} {1} {2} {3}  {4}{5}",
                        m.IsDeparture ? "Departure" : "Arrival", m.FlightCode, m.IsDeparture ? "to" : "from",
                        m.Other?.Profile.IATACode, m.Airline?.Profile.Name, string.IsNullOrEmpty(m.Aircraft) ? "" : "  |  " + m.Aircraft)
                });
            }
        }
    }
}
