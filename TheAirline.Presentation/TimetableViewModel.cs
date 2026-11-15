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

    public class DayChoice
    {
        public DayOfWeek Day { get; set; }
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

        /// <summary>The whole flight's length, which a block cut in two at midnight does not show.</summary>
        public double DurationMinutes { get; set; }
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
        private List<RouteTimeTableEntry> _justAdded = new List<RouteTimeTableEntry>();
        private TimetableRow _selectedRow;
        private DayChoice _editDay;
        private TimeChoice _editTime;
        private List<TimeChoice> _editTimes = new List<TimeChoice>();
        private string _editFlightCode = "";
        private bool _syncing;

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
            _editTimes = Times;
            TurnaroundOptions = RouteCreator.TurnaroundOptions;
            DayChoices = Days.Select(d => new DayChoice { Day = d.Day, Text = d.Day.ToString() }).ToList();

            AddRoundTripCommand = new RelayCommand(() => Add(true), () => _aircraft != null && _route != null);
            AddOneWayCommand = new RelayCommand(() => Add(false), () => _aircraft != null && _route != null);
            RemoveRouteFlightsCommand = new RelayCommand(RemoveRouteFlights, () => _aircraft != null && _route != null);
            RemoveAllFlightsCommand = new RelayCommand(AskRemoveAll);
            ConfirmRemoveAllCommand = new RelayCommand(RemoveAllFlights);
            CancelRemoveAllCommand = new RelayCommand(() => RemoveAllText = null);
            RemoveSelectedCommand = new RelayCommand(
                () => { if (_selectedBlock?.Entry != null) RemoveEntry(_selectedBlock.Entry); },
                () => _selectedBlock?.Entry != null);
            ApplyEditCommand = new RelayCommand(ApplyEdit, () => _selectedBlock?.Entry != null);
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
        public List<DayChoice> DayChoices { get; }

        public ICommand AddRoundTripCommand { get; }
        public ICommand AddOneWayCommand { get; }
        public ICommand RemoveRouteFlightsCommand { get; }
        public ICommand RemoveAllFlightsCommand { get; }
        public ICommand ConfirmRemoveAllCommand { get; }
        public ICommand CancelRemoveAllCommand { get; }
        private string _removeAllText;
        public bool HasRemoveAll => _removeAllText != null;

        public string RemoveAllText
        {
            get { return _removeAllText; }
            private set { if (SetProperty(ref _removeAllText, value)) OnPropertyChanged(nameof(HasRemoveAll)); }
        }

        private void AskRemoveAll()
        {
            if (_aircraft == null) return;
            var f = _aircraft.Airliner;
            int n = f.Routes.Sum(r => r.TimeTable.Entries.Count(e => e.Airliner == f));
            if (n == 0)
            {
                Status = f.Name + " has no flights in a timetable.";
                return;
            }
            RemoveAllText = "Remove all " + n + " flight(s) of " + f.Name + " on its " + f.Routes.Count + " route(s)? The aircraft is stopped and keeps no schedule. The routes stay and can be flown by another aircraft.";
        }

        private void RemoveAllFlights()
        {
            RemoveAllText = null;
            if (_aircraft == null) return;
            var f = _aircraft.Airliner;
            int n = f.Routes.Sum(r => r.TimeTable.Entries.Count(e => e.Airliner == f));
            foreach (var route in f.Routes.ToList())
            {
                f.RemoveRoute(route);
            }
            Finish(n + " flight(s) removed from " + f.Name + ".");
        }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ApplyEditCommand { get; }
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
                    AfterSelectionChanged();
                    TimelineChanged?.Invoke();
                }
            }
        }

        /// <summary>The flight picked in the list below the timeline; the two pick the same flight.</summary>
        public TimetableRow SelectedRow
        {
            get { return _selectedRow; }
            set
            {
                if (!SetProperty(ref _selectedRow, value) || _syncing) return;
                _syncing = true;
                SelectedBlock = value?.Entry == null ? null : _blocks.FirstOrDefault(b => b.Entry == value.Entry);
                _syncing = false;
            }
        }

        public bool CanEditSelected => _selectedBlock?.Entry != null;

        /// <summary>The day the selected flight would move to.</summary>
        public DayChoice EditDay
        {
            get { return _editDay; }
            set { SetProperty(ref _editDay, value); }
        }

        /// <summary>Every quarter hour, plus the selected flight's own time when it falls between two.</summary>
        public List<TimeChoice> EditTimes
        {
            get { return _editTimes; }
            private set { SetProperty(ref _editTimes, value); }
        }

        public TimeChoice EditTime
        {
            get { return _editTime; }
            set { SetProperty(ref _editTime, value); }
        }

        public string EditFlightCode
        {
            get { return _editFlightCode; }
            set { SetProperty(ref _editFlightCode, value ?? ""); }
        }

        // the edit boxes follow whatever is picked, on the timeline or in the list
        private void AfterSelectionChanged()
        {
            var entry = _selectedBlock?.Entry;
            if (entry != null)
            {
                EditTimes = Times.Any(t => t.Time == entry.Time)
                    ? Times
                    : Times.Concat(new[] { new TimeChoice { Time = entry.Time, Text = entry.Time.ToString(@"hh\:mm") } })
                           .OrderBy(t => t.Time).ToList();
                EditDay = DayChoices.FirstOrDefault(d => d.Day == entry.Day);
                EditTime = EditTimes.FirstOrDefault(t => t.Time == entry.Time);
                EditFlightCode = entry.Destination.FlightCode ?? "";
            }

            if (!_syncing)
            {
                _syncing = true;
                SelectedRow = entry == null ? null : Rows.FirstOrDefault(r => r.Entry == entry);
                _syncing = false;
            }

            OnPropertyChanged(nameof(SelectedBlockText));
            OnPropertyChanged(nameof(CanEditSelected));
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public string SelectedBlockText => _selectedBlock == null
            ? "Click a flight on the timeline to see it, or drag it to another day or time. The dashed blocks show the flights you are about to add."
            : _selectedBlock.Detail;

        /// <summary>
        ///     Where a flight lands when it is dragged across so many day rows and so many minutes. The whole week
        ///     moves with the pointer, so a flight dragged off Sunday evening comes back on Monday, and times snap to
        ///     the quarter hour, which is what the Add panel offers.
        /// </summary>
        public static void DragTarget(DayOfWeek day, TimeSpan time, int daysMoved, double minutesMoved, out DayOfWeek newDay, out TimeSpan newTime)
        {
            const int week = 7*1440, snap = 15;
            double at = DayIndex(day)*1440.0 + time.TotalMinutes + daysMoved*1440.0 + minutesMoved;
            at = Math.Round(at/snap)*snap;
            at = ((at%week) + week)%week;
            int index = (int) (at/1440);
            newDay = (DayOfWeek) ((index + 1)%7); // Monday is row 0
            newTime = TimeSpan.FromMinutes(at - index*1440);
        }

        /// <summary>
        ///     Moves one flight to another day and time, which is what dragging a block does. The flight is put back
        ///     where it was when it would not fit, and the reason is returned; null means it moved.
        /// </summary>
        public string MoveEntry(RouteTimeTableEntry entry, DayOfWeek day, TimeSpan time)
        {
            if (_aircraft == null || entry == null) return "Pick an aircraft first.";
            if (entry.Day == day && entry.Time == time) return null;

            var f = _aircraft.Airliner;
            DayOfWeek wasDay = entry.Day;
            TimeSpan wasTime = entry.Time;
            entry.Day = day;
            entry.Time = time;
            if (!TimeTableHelpers.IsTimeTableValid(f, AircraftEntries().ToList()))
            {
                entry.Day = wasDay;
                entry.Time = wasTime;
                string why = "That leaves " + entry.Destination.FlightCode + " where it was: on " + day + " at "
                             + time.ToString(@"hh\:mm") + " it would overlap another flight of " + f.Name
                             + " (which needs the flight time plus its time on the ground), or the airport has no free gate time then.";
                Status = why;
                return why;
            }

            _justAdded = new List<RouteTimeTableEntry> { entry };
            Finish("Flight " + entry.Destination.FlightCode + " moved to " + day + " at " + time.ToString(@"hh\:mm") + ".");
            return null;
        }

        // the edit panel's button: the day, the time and the flight number of the picked flight at once
        private void ApplyEdit()
        {
            var entry = _selectedBlock?.Entry;
            if (entry == null || _editDay == null || _editTime == null) return;

            string code = _editFlightCode.Trim();
            string wasCode = entry.Destination.FlightCode;
            bool codeChanged = code.Length > 0 && code != wasCode;
            if (codeChanged)
            {
                var clash = AircraftEntries().FirstOrDefault(e => e != entry && e.Destination.FlightCode == code);
                if (clash == null && Human.Routes.SelectMany(r => r.TimeTable.Entries).Any(e => e != entry && e.Destination.FlightCode == code))
                {
                    Status = "Flight number " + code + " is already used by another of your flights.";
                    return;
                }
                if (clash != null)
                {
                    Status = "Flight number " + code + " is already used by this aircraft.";
                    return;
                }
                entry.Destination.FlightCode = code;
            }

            if (entry.Day == _editDay.Day && entry.Time == _editTime.Time)
            {
                if (codeChanged) Finish("Flight " + wasCode + " is now " + code + ".");
                return;
            }

            if (MoveEntry(entry, _editDay.Day, _editTime.Time) != null && codeChanged)
            {
                entry.Destination.FlightCode = wasCode; // the move was refused, so the number goes back too
            }
        }

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
                    if (_aircraft.Airliner.Airliner.Range < distance || _aircraft.Airliner.Airliner.MinRunwaylength > runway)
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
            double length = Math.Max(durationMinutes, 10);
            double end = startMinutes + length;
            if (end <= 1440)
            {
                list.Add(new TimelineBlock { Day = d, Start = startMinutes, End = end, Label = label, Detail = detail, ColorIndex = color, IsPreview = preview, Entry = entry, DurationMinutes = length });
                return;
            }

            list.Add(new TimelineBlock { Day = d, Start = startMinutes, End = 1440, Label = label, Detail = detail, ColorIndex = color, IsPreview = preview, Entry = entry, DurationMinutes = length });
            list.Add(new TimelineBlock { Day = (d + 1) % 7, Start = 0, End = end - 1440, Label = label, Detail = detail, ColorIndex = color, IsPreview = preview, Entry = entry, DurationMinutes = length });
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
            // what was just placed or moved is picked, so it can be seen and moved again straight away; otherwise
            // whatever was picked before stays picked
            TimelineBlock pick = null;
            foreach (var e in _justAdded)
            {
                pick = list.FirstOrDefault(b => b.Entry == e);
                if (pick != null) break;
            }

            _justAdded = new List<RouteTimeTableEntry>();
            _selectedBlock = pick ?? list.FirstOrDefault(b => b.Entry == keep);
            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
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

            _justAdded = rt.Entries.OrderBy(e => DayIndex(e.Day)).ThenBy(e => e.Time).ToList();
            Finish(string.Format("{0} flight(s) added ({1}).", rt.Entries.Count, roundTrip ? "round trips" : "one way"));
        }
    }
}
