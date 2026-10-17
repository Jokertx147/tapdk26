using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class AircraftOption
    {
        public FleetAirliner Airliner { get; set; }
        public string Text { get; set; }
    }

    public class RouteOption
    {
        public Route Route { get; set; }
        public string Text { get; set; }
    }

    public class AirportOption
    {
        public Airport Airport { get; set; }
        public string Text { get; set; }
    }

    public class TimeChoice
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; }
    }

    public class DayFlag : ObservableObject
    {
        private bool _checked;
        public DayOfWeek Day { get; set; }
        public string Name { get; set; }

        public bool IsChecked
        {
            get { return _checked; }
            set { SetProperty(ref _checked, value); }
        }
    }

    public class TimetableRow
    {
        public RouteTimeTableEntry Entry { get; set; }
        public string Day { get; set; }
        public string Time { get; set; }
        public string Arrives { get; set; }
        public string Flight { get; set; }
        public string Route { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    /// <summary>A flight drawn on the weekly timeline: a day, a start and an end in minutes of that day.</summary>
    public class TimelineBlock
    {
        public int Day { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public string Label { get; set; }
        public string Detail { get; set; }
        public int ColorIndex { get; set; }
        public bool IsPreview { get; set; }
        public RouteTimeTableEntry Entry { get; set; }
    }

    /// <summary>Edit the weekly schedule of one aircraft: add or remove flights on its routes.</summary>
    public class TimetableViewModel : ObservableObject
    {
        private readonly Action _changed;
        private AircraftOption _aircraft;
        private RouteOption _route;
        private AirportOption _from;
        private TimeChoice _time;
        private int _turnaround = 45;
        private string _flightCode = "";
        private string _status;
        private string _summary;
        private List<AirportOption> _directions = new List<AirportOption>();
        private List<TimelineBlock> _blocks = new List<TimelineBlock>();
        private List<TimelineBlock> _preview = new List<TimelineBlock>();
        private TimelineBlock _selectedBlock;

        public TimetableViewModel(Action changed = null)
        {
            _changed = changed;
            Aircraft = new ObservableCollection<AircraftOption>();
            Rows = new ObservableCollection<TimetableRow>();
            Routes = new ObservableCollection<RouteOption>();
            Days = new List<DayFlag>();
            foreach (var d in new[]
                     {
                         DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                         DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                     })
            {
                var flag = new DayFlag { Day = d, Name = d.ToString().Substring(0, 3), IsChecked = true };
                flag.PropertyChanged += (s, e) => BuildPreview();
                Days.Add(flag);
            }

            Times = new List<TimeChoice>();
            for (int m = 0; m < 24 * 60; m += 15)
            {
                var t = new TimeSpan(0, m, 0);
                Times.Add(new TimeChoice { Time = t, Text = t.ToString(@"hh\:mm") });
            }

            _time = Times.First(t => t.Time == new TimeSpan(8, 0, 0));
            TurnaroundOptions = RouteCreator.TurnaroundOptions;

            AddRoundTripCommand = new RelayCommand(() => Add(true), () => _aircraft != null && _route != null);
            AddOneWayCommand = new RelayCommand(() => Add(false), () => _aircraft != null && _route != null);
            RemoveRouteFlightsCommand = new RelayCommand(RemoveRouteFlights, () => _aircraft != null && _route != null);
            RemoveSelectedCommand = new RelayCommand(
                () => { if (_selectedBlock?.Entry != null) RemoveEntry(_selectedBlock.Entry); },
                () => _selectedBlock?.Entry != null);
            DailyCommand = new RelayCommand(() => SetDays(Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToArray()));
            WeekdaysCommand = new RelayCommand(
                () => SetDays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday));
            WeekendCommand = new RelayCommand(() => SetDays(DayOfWeek.Saturday, DayOfWeek.Sunday));
            MwfCommand = new RelayCommand(() => SetDays(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday));
            TtsCommand = new RelayCommand(() => SetDays(DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday));

            ReloadAircraft();
        }

        public ObservableCollection<AircraftOption> Aircraft { get; }
        public ObservableCollection<TimetableRow> Rows { get; }
        public ObservableCollection<RouteOption> Routes { get; }
        public List<DayFlag> Days { get; }
        public List<TimeChoice> Times { get; }
        public List<int> TurnaroundOptions { get; }

        public ICommand AddRoundTripCommand { get; }
        public ICommand AddOneWayCommand { get; }
        public ICommand RemoveRouteFlightsCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand DailyCommand { get; }
        public ICommand WeekdaysCommand { get; }
        public ICommand WeekendCommand { get; }
        public ICommand MwfCommand { get; }
        public ICommand TtsCommand { get; }

        public AircraftOption SelectedAircraft
        {
            get { return _aircraft; }
            set
            {
                if (SetProperty(ref _aircraft, value))
                {
                    LoadRoutes();
                    LoadRows();
                    RaiseCanExecute();
                }
            }
        }

        /// <summary>Raised when the timeline has to be drawn again.</summary>
        public event Action TimelineChanged;

        public List<TimelineBlock> Blocks => _blocks;
        public List<TimelineBlock> Preview => _preview;

        public TimelineBlock SelectedBlock
        {
            get { return _selectedBlock; }
            set
            {
                if (SetProperty(ref _selectedBlock, value))
                {
                    OnPropertyChanged(nameof(SelectedBlockText));
                    (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    TimelineChanged?.Invoke();
                }
            }
        }

        public string SelectedBlockText => _selectedBlock == null
            ? "Click a flight on the timeline to see it. The dashed blocks show the flights you are about to add."
            : _selectedBlock.Detail;

        public RouteOption SelectedRoute
        {
            get { return _route; }
            set
            {
                if (SetProperty(ref _route, value))
                {
                    LoadDirections();
                    BuildPreview();
                    RaiseCanExecute();
                }
            }
        }

        public List<AirportOption> Directions
        {
            get { return _directions; }
            private set { SetProperty(ref _directions, value); }
        }

        public AirportOption SelectedFrom
        {
            get { return _from; }
            set { if (SetProperty(ref _from, value)) BuildPreview(); }
        }

        public TimeChoice SelectedTime
        {
            get { return _time; }
            set { if (SetProperty(ref _time, value)) BuildPreview(); }
        }

        public int Turnaround
        {
            get { return _turnaround; }
            set { if (SetProperty(ref _turnaround, value)) BuildPreview(); }
        }

        /// <summary>Flight number of the outbound flight; empty picks the next free numbers.</summary>
        public string FlightCode
        {
            get { return _flightCode; }
            set { SetProperty(ref _flightCode, value ?? ""); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        private static Airline Human
        {
            get { return GameObject.GetInstance().HumanAirline; }
        }

        private void RaiseCanExecute()
        {
            (AddRoundTripCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddOneWayCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveRouteFlightsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void SetDays(params DayOfWeek[] days)
        {
            foreach (var d in Days)
            {
                d.IsChecked = days.Contains(d.Day);
            }
        }

        /// <summary>Opens the schedule of an aircraft.</summary>
        public void Select(FleetAirliner f)
        {
            ReloadAircraft();
            var option = Aircraft.FirstOrDefault(a => a.Airliner == f);
            if (option != null)
            {
                SelectedAircraft = option;
            }
        }

        /// <summary>Refreshes the lists after something changed elsewhere (new aircraft, new route).</summary>
        public void ReloadAircraft()
        {
            var keep = _aircraft?.Airliner;
            var list = Human.DeliveredFleet
                            .Where(
                                f => f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger
                                     || f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed
                                     || f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo)
                            .OrderBy(f => f.Name)
                            .ToList();

            Aircraft.Clear();
            foreach (var f in list)
            {
                Aircraft.Add(
                    new AircraftOption
                    {
                        Airliner = f,
                        Text = f.Name + "  |  " + f.Airliner.Type.Name + "  |  "
                               + f.Routes.SelectMany(r => r.TimeTable.Entries).Count(e => e.Airliner == f) + " flights/week"
                    });
            }

            _aircraft = Aircraft.FirstOrDefault(a => a.Airliner == keep) ?? Aircraft.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedAircraft));
            LoadRoutes();
            LoadRows();
            RaiseCanExecute();
        }

        private void LoadRoutes()
        {
            var keep = _route?.Route;
            Routes.Clear();

            if (_aircraft != null)
            {
                var type = _aircraft.Airliner.Airliner.Type;
                bool cargoAircraft = type.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo;
                foreach (var r in Human.Routes.Where(r => r.IsCargoRoute == cargoAircraft).OrderBy(r => r.Destination1.Profile.IATACode))
                {
                    double distance = r.GetDistance();
                    long runway = Math.Min(r.Destination1.GetMaxRunwayLength(), r.Destination2.GetMaxRunwayLength());
                    if (type.Range < distance || type.MinRunwaylength > runway)
                    {
                        continue;
                    }

                    Routes.Add(
                        new RouteOption
                        {
                            Route = r,
                            Text = string.Format(
                                "{0} - {1}  ({2}, {3})",
                                r.Destination1.Profile.IATACode,
                                r.Destination2.Profile.IATACode,
                                DistanceFormatter.Km(distance),
                                FormatSpan(r.GetFlightTime(type)))
                        });
                }
            }

            _route = Routes.FirstOrDefault(r => r.Route == keep) ?? Routes.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedRoute));
            LoadDirections();
        }

        private void LoadDirections()
        {
            var keep = _from?.Airport;
            if (_route == null)
            {
                Directions = new List<AirportOption>();
                SelectedFrom = null;
                return;
            }

            Directions = new[] { _route.Route.Destination1, _route.Route.Destination2 }
                .Select(a => new AirportOption { Airport = a, Text = a.Profile.IATACode + "  " + a.Profile.Name })
                .ToList();
            SelectedFrom = Directions.FirstOrDefault(d => d.Airport == keep) ?? Directions[0];
        }

        private static string FormatSpan(TimeSpan t)
        {
            return string.Format("{0}h{1:00}", (int) t.TotalHours, t.Minutes);
        }

        private static int DayIndex(DayOfWeek d)
        {
            return ((int) d + 6) % 7; // Monday first
        }

        private IEnumerable<RouteTimeTableEntry> AircraftEntries()
        {
            var f = _aircraft.Airliner;
            return f.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == f)).ToList();
        }

        private void LoadRows()
        {
            Rows.Clear();
            if (_aircraft == null)
            {
                Summary = "You have no delivered aircraft.";
                _blocks = new List<TimelineBlock>();
                SelectedBlock = null;
                BuildPreview();
                return;
            }

            var f = _aircraft.Airliner;
            var entries = AircraftEntries()
                .OrderBy(e => DayIndex(e.Day)).ThenBy(e => e.Time).ToList();
            double airMinutes = 0;

            foreach (var e in entries)
            {
                TimeSpan flight = e.TimeTable.Route.GetFlightTime(f.Airliner.Type);
                airMinutes += flight.TotalMinutes;
                TimeSpan arrive = e.Time.Add(flight);
                var entry = e;
                Rows.Add(
                    new TimetableRow
                    {
                        Entry = e,
                        Day = e.Day.ToString().Substring(0, 3),
                        Time = e.Time.ToString(@"hh\:mm"),
                        Arrives = new TimeSpan(arrive.Hours, arrive.Minutes, 0).ToString(@"hh\:mm")
                                  + (arrive.Days > 0 ? " +" + arrive.Days : ""),
                        Flight = e.Destination.FlightCode,
                        Route = e.DepartureAirport.Profile.IATACode + " > " + e.Destination.Airport.Profile.IATACode,
                        RemoveCommand = new RelayCommand(() => RemoveEntry(entry))
                    });
            }

            BuildBlocks(entries);
            BuildPreview();

            Summary = entries.Count == 0
                ? f.Name + " has no flights yet."
                : string.Format("{0}: {1} flights a week, about {2:0} hours in the air.", f.Name, entries.Count, airMinutes / 60);
        }

        private static void AddBlocks(
            List<TimelineBlock> list, DayOfWeek day, double startMinutes, double durationMinutes, string label, string detail, int color, bool preview, RouteTimeTableEntry entry)
        {
            int d = DayIndex(day);
            double end = startMinutes + Math.Max(durationMinutes, 10);
            if (end <= 1440)
            {
                list.Add(new TimelineBlock { Day = d, Start = startMinutes, End = end, Label = label, Detail = detail, ColorIndex = color, IsPreview = preview, Entry = entry });
                return;
            }

            list.Add(new TimelineBlock { Day = d, Start = startMinutes, End = 1440, Label = label, Detail = detail, ColorIndex = color, IsPreview = preview, Entry = entry });
            list.Add(new TimelineBlock { Day = (d + 1) % 7, Start = 0, End = end - 1440, Label = label, Detail = detail, ColorIndex = color, IsPreview = preview, Entry = entry });
        }

        private void BuildBlocks(List<RouteTimeTableEntry> entries)
        {
            var f = _aircraft.Airliner;
            var routes = f.Routes.ToList();
            var keep = _selectedBlock?.Entry;
            var list = new List<TimelineBlock>();
            foreach (var e in entries)
            {
                TimeSpan flight = e.TimeTable.Route.GetFlightTime(f.Airliner.Type);
                string route = e.DepartureAirport.Profile.IATACode + " > " + e.Destination.Airport.Profile.IATACode;
                TimeSpan arrive = e.Time.Add(flight);
                string detail = string.Format(
                    "{0} {1}  |  {2}  |  leaves {3}, arrives {4}{5}  |  {6}",
                    e.Day, e.Destination.FlightCode, route, e.Time.ToString(@"hh\:mm"),
                    new TimeSpan(arrive.Hours, arrive.Minutes, 0).ToString(@"hh\:mm"), arrive.Days > 0 ? " (+" + arrive.Days + ")" : "", FormatSpan(flight));
                AddBlocks(list, e.Day, e.Time.TotalMinutes, flight.TotalMinutes, e.Destination.FlightCode + " " + route, detail,
                          Math.Max(0, routes.IndexOf(e.TimeTable.Route)), false, e);
            }

            _blocks = list;
            _selectedBlock = list.FirstOrDefault(b => b.Entry == keep);
            OnPropertyChanged(nameof(SelectedBlock));
            OnPropertyChanged(nameof(SelectedBlockText));
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>The dashed flights that "Add round trips" would put on the timeline.</summary>
        private void BuildPreview()
        {
            var list = new List<TimelineBlock>();
            if (_aircraft != null && _route != null && _from != null && Days != null)
            {
                var f = _aircraft.Airliner;
                var route = _route.Route;
                Airport from = _from.Airport;
                Airport to = from == route.Destination1 ? route.Destination2 : route.Destination1;
                TimeSpan flight = route.GetFlightTime(f.Airliner.Type);
                TimeSpan back = _time.Time.Add(flight).Add(new TimeSpan(0, _turnaround, 0));
                int color = Math.Max(0, f.Routes.ToList().IndexOf(route));
                foreach (var day in Days.Where(d => d.IsChecked).Select(d => d.Day))
                {
                    AddBlocks(list, day, _time.Time.TotalMinutes, flight.TotalMinutes, from.Profile.IATACode + " > " + to.Profile.IATACode,
                              "New flight", color, true, null);
                    DayOfWeek backDay = (DayOfWeek) (((int) day + back.Days) % 7);
                    AddBlocks(list, backDay, back.Hours * 60 + back.Minutes, flight.TotalMinutes, to.Profile.IATACode + " > " + from.Profile.IATACode,
                              "New flight", color, true, null);
                }
            }

            _preview = list;
            TimelineChanged?.Invoke();
        }

        private void StopIfFlying(FleetAirliner f)
        {
            if (f.Status != FleetAirliner.AirlinerStatus.Stopped)
            {
                f.Status = FleetAirliner.AirlinerStatus.Stopped;
                Status += " " + f.Name + " was stopped; start it again under Flights > Fleet.";
            }
        }

        private void Finish(string message)
        {
            Status = message;
            StopIfFlying(_aircraft.Airliner);
            ReloadAircraft();
            _changed?.Invoke();
        }

        private void RemoveEntry(RouteTimeTableEntry entry)
        {
            if (_aircraft == null)
            {
                return;
            }

            var f = _aircraft.Airliner;
            var route = entry.TimeTable.Route;
            route.TimeTable.RemoveEntry(entry); // entries made by the route creator can still point at a temporary timetable
            entry.TimeTable.RemoveEntry(entry);

            if (!route.TimeTable.Entries.Any(e => e.Airliner == f))
            {
                f.RemoveRoute(route);
            }

            Finish("Flight " + entry.Destination.FlightCode + " removed.");
        }

        private void RemoveRouteFlights()
        {
            if (_aircraft == null || _route == null)
            {
                return;
            }

            var f = _aircraft.Airliner;
            int n = _route.Route.TimeTable.Entries.Count(e => e.Airliner == f);
            f.RemoveRoute(_route.Route);
            Finish(n + " flight(s) removed from " + _route.Text + ".");
        }

        private void Add(bool roundTrip)
        {
            if (_aircraft == null || _route == null || _from == null)
            {
                return;
            }

            var days = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList();
            if (days.Count == 0)
            {
                Status = "Tick at least one day.";
                return;
            }

            var f = _aircraft.Airliner;
            var route = _route.Route;
            Airport from = _from.Airport;
            Airport to = from == route.Destination1 ? route.Destination2 : route.Destination1;

            // flight numbers: typed by the player, or the next free ones
            string code1 = _flightCode.Trim();
            var free = Human.GetFlightCodes();
            if (code1.Length == 0)
            {
                code1 = free[0];
            }

            string code2 = free.FirstOrDefault(c => c != code1) ?? free[0];
            if (!string.IsNullOrEmpty(_flightCode.Trim()))
            {
                int idx = free.IndexOf(code1);
                code2 = idx >= 0 && idx + 1 < free.Count ? free[idx + 1] : free.First(c => c != code1);
            }

            TimeSpan flightTime = route.GetFlightTime(f.Airliner.Type);
            TimeSpan back = _time.Time.Add(flightTime).Add(new TimeSpan(0, _turnaround, 0));

            var rt = new RouteTimeTable(route);
            foreach (var day in days)
            {
                rt.AddEntry(
                    new RouteTimeTableEntry(route.TimeTable, day, _time.Time, new RouteEntryDestination(to, code1))
                    {
                        Airliner = f
                    });

                if (roundTrip)
                {
                    DayOfWeek backDay = (DayOfWeek) (((int) day + back.Days) % 7);
                    rt.AddEntry(
                        new RouteTimeTableEntry(
                            route.TimeTable,
                            backDay,
                            new TimeSpan(back.Hours, back.Minutes, 0),
                            new RouteEntryDestination(from, code2))
                        {
                            Airliner = f
                        });
                }
            }

            var existing = AircraftEntries().ToList();
            if (!TimeTableHelpers.IsRoutePlannerTimeTableValid(rt, f, existing))
            {
                Status = "These flights don't fit: they overlap another flight of " + f.Name
                         + " (it needs the flight time plus time on the ground), or one of the airports has no free gate time then. Try another time or fewer days.";
                return;
            }

            foreach (var e in rt.Entries)
            {
                route.TimeTable.AddEntry(e);
            }

            if (!f.Routes.Contains(route))
            {
                f.AddRoute(route);
            }

            Finish(string.Format("{0} flight(s) added ({1}).", rt.Entries.Count, roundTrip ? "round trips" : "one way"));
        }
    }
}
