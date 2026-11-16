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

    /// <summary>One aircraft's week, kept so a change to it can be taken back.</summary>
    internal class TimetableMemo
    {
        public string Label { get; set; }
        public FleetAirliner Airliner { get; set; }
        public List<MemoLine> Lines { get; set; }
    }

    internal class MemoLine
    {
        public Route Route { get; set; }
        public DayOfWeek Day { get; set; }
        public TimeSpan Time { get; set; }
        public string Code { get; set; }
        public Airport To { get; set; }
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

        /// <summary>
        ///     A stretch of ground time rather than a flight: the aircraft is at the gate being turned round, and
        ///     cannot leave again until it has passed. Drawn faint behind the flights.
        /// </summary>
        public bool IsGround { get; set; }
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
        private List<TimelineBlock> _ground = new List<TimelineBlock>();
        private List<TimelineBlock> _previewGround = new List<TimelineBlock>();
        private TimelineBlock _selectedBlock;
        private List<RouteTimeTableEntry> _justAdded = new List<RouteTimeTableEntry>();
        private readonly HashSet<RouteTimeTableEntry> _selected = new HashSet<RouteTimeTableEntry>();

        // What the player last chose to add, which is not the same as what is chosen right now. Rebuilding the
        // aircraft list empties the box bound to it, and the empty box writes its own null back before the list is
        // filled again, which wiped the choice and sent the route box back to the first route after every flight
        // added. These hold the choice across that, so adding four flights on one route needs the route picked once.
        private Route _wantRoute;
        private Airport _wantFrom;

        // One step back is this aircraft's whole week, which is a short list and makes every change undoable the same
        // way, whether it added, moved, renumbered, copied or removed flights.
        private readonly List<TimetableMemo> _memos = new List<TimetableMemo>();
        private const int MemoDepth = 25;
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
            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => _selected.Count > 0);
            ApplyEditCommand = new RelayCommand(ApplyEdit, () => _selected.Count == 1 && _selectedBlock?.Entry != null);
            UndoCommand = new RelayCommand(Undo, () => _memos.Count > 0);
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
            var memo = Snapshot("clearing " + f.Name + "'s week");
            foreach (var route in f.Routes.ToList())
            {
                f.RemoveRoute(route);
            }
            Remember(memo);
            Finish(n + " flight(s) removed from " + f.Name + ".");
        }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ApplyEditCommand { get; }
        public ICommand UndoCommand { get; }
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

        /// <summary>The time each flight spends at the gate afterwards, drawn behind the flights.</summary>
        public List<TimelineBlock> Ground => _ground;

        public List<TimelineBlock> PreviewGround => _previewGround;

        /// <summary>How long the chosen aircraft needs on the ground between flights.</summary>
        public int GroundMinutes => _aircraft == null ? 0 : (int) FleetAirlinerHelpers.GetMinTimeBetweenFlights(_aircraft.Airliner).TotalMinutes;

        /// <summary>
        ///     The flight the edit boxes act on: the last one clicked. Setting it picks that flight alone, as a plain
        ///     click does; SelectAlso and SelectRange build a selection of several.
        /// </summary>
        public TimelineBlock SelectedBlock
        {
            get { return _selectedBlock; }
            set
            {
                _selected.Clear();
                if (value?.Entry != null) _selected.Add(value.Entry);
                _selectedBlock = value;
                OnPropertyChanged(nameof(SelectedBlock));
                AfterSelectionChanged();
                TimelineChanged?.Invoke();
            }
        }

        /// <summary>Every flight picked, which a drag moves together and the remove button removes together.</summary>
        public IReadOnlyCollection<RouteTimeTableEntry> SelectedEntries => _selected;

        public int SelectedCount => _selected.Count;

        public bool HasSelection => _selected.Count > 0;

        /// <summary>The remove button says how many flights it would take away.</summary>
        public string RemoveSelectedText => _selected.Count > 1 ? "Remove these " + _selected.Count + " flights" : "Remove this flight";

        public bool IsSelected(RouteTimeTableEntry entry)
        {
            return entry != null && _selected.Contains(entry);
        }

        /// <summary>Adds a flight to the picked ones, or takes it out again: what ctrl-clicking does.</summary>
        public void SelectAlso(TimelineBlock block)
        {
            var entry = block?.Entry;
            if (entry == null) return;
            if (!_selected.Remove(entry))
            {
                _selected.Add(entry);
                _selectedBlock = block;
            }
            else if (_selectedBlock?.Entry == entry)
            {
                _selectedBlock = _blocks.FirstOrDefault(b => _selected.Contains(b.Entry));
            }

            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
            TimelineChanged?.Invoke();
        }

        /// <summary>Picks everything between the flight already picked and this one, as shift-clicking does.</summary>
        public void SelectRange(TimelineBlock block)
        {
            if (block?.Entry == null) return;
            var from = _selectedBlock?.Entry;
            if (from == null)
            {
                SelectedBlock = block;
                return;
            }

            double a = WeekMinutes(from), b = WeekMinutes(block.Entry);
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            Replace(AircraftEntries().Where(e => WeekMinutes(e) >= lo && WeekMinutes(e) <= hi), block);
        }

        /// <summary>Picks exactly these flights: what a rubber band drawn over the timeline leaves picked.</summary>
        public void SelectBlocks(IEnumerable<RouteTimeTableEntry> entries, bool addToSelection)
        {
            var list = (entries ?? Enumerable.Empty<RouteTimeTableEntry>()).Where(e => e != null).ToList();
            if (addToSelection) list.AddRange(_selected);
            var primary = list.OrderBy(WeekMinutes).FirstOrDefault();
            Replace(list, primary == null ? null : _blocks.FirstOrDefault(b => b.Entry == primary));
        }

        private void Replace(IEnumerable<RouteTimeTableEntry> entries, TimelineBlock primary)
        {
            _selected.Clear();
            foreach (var e in entries) _selected.Add(e);
            _selectedBlock = primary ?? _blocks.FirstOrDefault(b => _selected.Contains(b.Entry));
            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
            TimelineChanged?.Invoke();
        }

        /// <summary>Where a flight sits in the week, counting from Monday morning, so flights can be put in order.</summary>
        public static double WeekMinutes(RouteTimeTableEntry entry)
        {
            return DayIndex(entry.Day)*1440.0 + entry.Time.TotalMinutes;
        }

        private void RemoveSelected()
        {
            var going = _selected.ToList();
            if (going.Count == 0) return;
            if (going.Count == 1)
            {
                RemoveEntry(going[0]);
                return;
            }

            var memo = Snapshot("removing " + going.Count + " flights");
            foreach (var e in going) RemoveEntryQuietly(e);
            Remember(memo);
            _selected.Clear();
            _selectedBlock = null;
            Finish(going.Count + " flights removed.");
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

        public bool CanEditSelected => _selected.Count == 1 && _selectedBlock?.Entry != null;

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
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(RemoveSelectedText));
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public string SelectedBlockText
        {
            get
            {
                if (_selected.Count > 1) return _selected.Count + " flights picked. Drag any of them to move them all, or hold shift while dragging to copy them all, keeping their days and times apart.";
                if (_selectedBlock == null)
                    return "Click a flight to pick it, or drag across empty space to pick several; ctrl-click adds one, shift-click picks everything between. Drag a flight to move it. The dashed blocks show the flights you are about to add.";
                return _selectedBlock.Detail;
            }
        }

        /// <summary>Flights are placed on the quarter hour, as the Add panel offers, so a drag snaps to one.</summary>
        public const int SnapMinutes = 15;

        /// <summary>
        ///     How far a drag of so many minutes really shifts a flight: to the nearest quarter hour. A group is
        ///     shifted by this one amount rather than each flight snapping on its own, so the flights in it stay
        ///     exactly as far apart as they were.
        /// </summary>
        public static double SnapShift(double minutesMoved)
        {
            return Math.Round(minutesMoved/SnapMinutes)*SnapMinutes;
        }

        /// <summary>
        ///     Where a flight lands when it is dragged across so many day rows and so many minutes. The whole week
        ///     moves with the pointer, so a flight dragged off Sunday evening comes back on Monday, and times snap to
        ///     the quarter hour, which is what the Add panel offers.
        /// </summary>
        public static void DragTarget(DayOfWeek day, TimeSpan time, int daysMoved, double minutesMoved, out DayOfWeek newDay, out TimeSpan newTime, bool snapToQuarter = true)
        {
            const int week = 7*1440;
            double at = DayIndex(day)*1440.0 + time.TotalMinutes + daysMoved*1440.0 + minutesMoved;
            if (snapToQuarter) at = Math.Round(at/SnapMinutes)*SnapMinutes;
            at = ((at%week) + week)%week;
            int index = (int) (at/1440);
            newDay = (DayOfWeek) ((index + 1)%7); // Monday is row 0
            newTime = TimeSpan.FromMinutes(at - index*1440);
        }

        public bool CanUndo => _memos.Count > 0;

        /// <summary>What the undo button offers to take back, named so it is clear what will happen.</summary>
        public string UndoText => _memos.Count == 0 ? "Nothing to undo" : "Undo " + _memos[_memos.Count - 1].Label;

        // this aircraft's week as it stands, to go back to
        private TimetableMemo Snapshot(string label)
        {
            if (_aircraft == null) return null;
            return new TimetableMemo
            {
                Label = label,
                Airliner = _aircraft.Airliner,
                Lines = AircraftEntries().Select(e => new MemoLine
                {
                    Route = e.TimeTable.Route, Day = e.Day, Time = e.Time,
                    Code = e.Destination.FlightCode, To = e.Destination.Airport
                }).ToList()
            };
        }

        // kept only once the change it precedes has actually happened, so a refused move leaves nothing to undo
        private void Remember(TimetableMemo memo)
        {
            if (memo == null) return;
            _memos.Add(memo);
            while (_memos.Count > MemoDepth) _memos.RemoveAt(0);
            RaiseUndo();
        }

        private void RaiseUndo()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(UndoText));
            (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void Undo()
        {
            if (_memos.Count == 0) return;
            var memo = _memos[_memos.Count - 1];
            _memos.RemoveAt(_memos.Count - 1);

            var option = Aircraft.FirstOrDefault(a => a.Airliner == memo.Airliner);
            if (option == null)
            {
                Status = "That aircraft has left the fleet, so this cannot be undone.";
                RaiseUndo();
                return;
            }

            SelectedAircraft = option; // the change may have been made to another aircraft
            var f = memo.Airliner;
            foreach (var e in AircraftEntries().ToList()) RemoveEntryQuietly(e);

            int gone = 0;
            foreach (var line in memo.Lines)
            {
                if (!Human.Routes.Contains(line.Route))
                {
                    gone++; // the route itself has been closed since
                    continue;
                }

                var entry = new RouteTimeTableEntry(line.Route.TimeTable, line.Day, line.Time,
                                                    new RouteEntryDestination(line.To, line.Code)) { Airliner = f };
                line.Route.TimeTable.AddEntry(entry);
                if (!f.Routes.Contains(line.Route)) f.AddRoute(line.Route);
            }

            _justAdded = new List<RouteTimeTableEntry>();
            Finish("Undone: " + memo.Label + "." + (gone > 0 ? " " + gone + " flight(s) could not come back: their route has closed." : ""));
            RaiseUndo();
        }

        /// <summary>
        ///     Leaves a flight where it is and puts a copy of it at another day and time, with the next free flight
        ///     number: what dragging a flight with shift held does. Returns null when the copy fits, or why it does not.
        /// </summary>
        public string CopyEntry(RouteTimeTableEntry entry, DayOfWeek day, TimeSpan time)
        {
            if (_aircraft == null || entry == null) return "Pick an aircraft first.";
            var f = _aircraft.Airliner;
            var route = entry.TimeTable.Route;
            var memo = Snapshot("copying " + entry.Destination.FlightCode);
            string code = Human.GetNextFlightCode(0);
            var copy = new RouteTimeTableEntry(route.TimeTable, day, time,
                                               new RouteEntryDestination(entry.Destination.Airport, code)) { Airliner = f };
            route.TimeTable.AddEntry(copy);

            if (!TimeTableHelpers.IsTimeTableValid(f, AircraftEntries().ToList()))
            {
                route.TimeTable.RemoveEntry(copy);
                string why = "No copy of " + entry.Destination.FlightCode + " on " + day + " at " + time.ToString(@"hh\:mm")
                             + ": it would overlap another flight of " + f.Name + " (which needs the flight time plus its "
                             + "time on the ground), or the airport has no free gate time then.";
                Status = why;
                return why;
            }

            if (!f.Routes.Contains(route)) f.AddRoute(route);
            Remember(memo);
            _justAdded = new List<RouteTimeTableEntry> { copy };
            Finish("Flight " + entry.Destination.FlightCode + " copied to " + day + " at " + time.ToString(@"hh\:mm") + " as " + code + ".");
            return null;
        }

        /// <summary>
        ///     Copies every flight given, each landing the same number of day rows and minutes from where it is, so
        ///     the copies keep the shape of the group: a whole day's flying can be copied onto another day at once.
        ///     Each copy gets its own free flight number. Either all of them are made or none is.
        /// </summary>
        public string CopyEntries(IEnumerable<RouteTimeTableEntry> entries, int daysMoved, double minutesMoved)
        {
            if (_aircraft == null) return "Pick an aircraft first.";
            var going = (entries ?? Enumerable.Empty<RouteTimeTableEntry>()).Where(e => e != null).Distinct().OrderBy(WeekMinutes).ToList();
            if (going.Count == 0) return null;
            if (going.Count == 1)
            {
                DayOfWeek one; TimeSpan at;
                DragTarget(going[0].Day, going[0].Time, daysMoved, minutesMoved, out one, out at);
                return CopyEntry(going[0], one, at);
            }

            var f = _aircraft.Airliner;
            var free = Human.GetFlightCodes();
            if (free.Count < going.Count)
                return "There are not enough free flight numbers left to copy " + going.Count + " flights.";

            var memo = Snapshot("copying " + going.Count + " flights");
            double shift = SnapShift(minutesMoved);
            var copies = new List<Tuple<Route, RouteTimeTableEntry>>();
            for (int i = 0; i < going.Count; i++)
            {
                var e = going[i];
                DayOfWeek day; TimeSpan time;
                DragTarget(e.Day, e.Time, daysMoved, shift, out day, out time, false);
                var route = e.TimeTable.Route;
                var copy = new RouteTimeTableEntry(route.TimeTable, day, time,
                                                   new RouteEntryDestination(e.Destination.Airport, free[i])) { Airliner = f };
                route.TimeTable.AddEntry(copy);
                copies.Add(Tuple.Create(route, copy));
            }

            if (!TimeTableHelpers.IsTimeTableValid(f, AircraftEntries().ToList()))
            {
                foreach (var c in copies) c.Item1.TimeTable.RemoveEntry(c.Item2);
                string why = "No copies made: those " + going.Count + " flights would overlap each other or another "
                             + "flight of " + f.Name + " where they landed (each needs the flight time plus its time "
                             + "on the ground), or an airport has no free gate time then.";
                Status = why;
                return why;
            }

            foreach (var c in copies)
            {
                if (!f.Routes.Contains(c.Item1)) f.AddRoute(c.Item1);
            }

            Remember(memo);
            _justAdded = copies.Select(c => c.Item2).OrderBy(WeekMinutes).ToList();
            Finish(going.Count + " flights copied, numbered " + free[0] + " to " + free[going.Count - 1] + ".");
            return null;
        }

        /// <summary>
        ///     Moves every flight given by the same number of day rows and minutes, which is what dragging a picked
        ///     group does: they keep their days and times apart from one another. Either all of them move or none
        ///     does, so a group that would put two flights on top of each other is refused whole.
        /// </summary>
        public string MoveEntries(IEnumerable<RouteTimeTableEntry> entries, int daysMoved, double minutesMoved)
        {
            if (_aircraft == null) return "Pick an aircraft first.";
            var going = (entries ?? Enumerable.Empty<RouteTimeTableEntry>()).Where(e => e != null).Distinct().ToList();
            if (going.Count == 0) return null;
            if (going.Count == 1)
            {
                DayOfWeek one; TimeSpan at;
                DragTarget(going[0].Day, going[0].Time, daysMoved, minutesMoved, out one, out at);
                return MoveEntry(going[0], one, at);
            }

            // where each one lands, worked out before any of them moves. The drag snaps once and every flight is
            // shifted by that same amount, so the group keeps its own shape.
            double shift = SnapShift(minutesMoved);
            var memo = Snapshot("moving " + going.Count + " flights");
            var was = going.Select(e => Tuple.Create(e, e.Day, e.Time)).ToList();
            var to = new List<Tuple<RouteTimeTableEntry, DayOfWeek, TimeSpan>>();
            foreach (var e in going)
            {
                DayOfWeek day; TimeSpan time;
                DragTarget(e.Day, e.Time, daysMoved, shift, out day, out time, false);
                to.Add(Tuple.Create(e, day, time));
            }

            if (to.All(x => x.Item1.Day == x.Item2 && x.Item1.Time == x.Item3)) return null;

            foreach (var x in to)
            {
                x.Item1.Day = x.Item2;
                x.Item1.Time = x.Item3;
            }

            if (!TimeTableHelpers.IsTimeTableValid(_aircraft.Airliner, AircraftEntries().ToList()))
            {
                foreach (var x in was)
                {
                    x.Item1.Day = x.Item2;
                    x.Item1.Time = x.Item3;
                }

                string why = "Those " + going.Count + " flights stay where they are: moved together they would overlap "
                             + "another flight of " + _aircraft.Airliner.Name + " (which needs the flight time plus its "
                             + "time on the ground), or an airport has no free gate time then.";
                Status = why;
                return why;
            }

            Remember(memo);
            _justAdded = going.OrderBy(WeekMinutes).ToList();
            Finish(going.Count + " flights moved.");
            return null;
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
            var memo = Snapshot("moving " + entry.Destination.FlightCode);
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

            Remember(memo);
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
                if (codeChanged)
                {
                    entry.Destination.FlightCode = wasCode;
                    var memoC = Snapshot("renumbering " + wasCode);
                    entry.Destination.FlightCode = code;
                    Remember(memoC);
                    Finish("Flight " + wasCode + " is now " + code + ".");
                }

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
                if (value?.Route != null) _wantRoute = value.Route;
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
            set
            {
                if (value?.Airport != null) _wantFrom = value.Airport;
                if (SetProperty(ref _from, value)) BuildPreview();
            }
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

            _route = Routes.FirstOrDefault(r => r.Route == keep)
                     ?? Routes.FirstOrDefault(r => r.Route == _wantRoute)
                     ?? Routes.FirstOrDefault();
            if (_route?.Route != null) _wantRoute = _route.Route;
            OnPropertyChanged(nameof(SelectedRoute));
            LoadDirections();
        }

        private void LoadDirections()
        {
            var keep = _from?.Airport ?? _wantFrom;
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
            if (_from?.Airport != null) _wantFrom = _from.Airport;
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
                ? string.Format("{0} has no flights yet. It needs {1} minutes at the gate between one flight and the next.", f.Name, GroundMinutes)
                : string.Format(
                    "{0}: {1} flights a week, about {2:0} hours in the air. The faint band after each flight is the {3} minutes it needs at the gate before it can leave again.",
                    f.Name, entries.Count, airMinutes/60, GroundMinutes);
        }

        private static void AddBlocks(
            List<TimelineBlock> list, List<TimelineBlock> groundList, DayOfWeek day, double startMinutes, double durationMinutes,
            double groundMinutes, string label, string detail, int color, bool preview, RouteTimeTableEntry entry)
        {
            int d = DayIndex(day);
            double length = Math.Max(durationMinutes, 10);
            Span(list, d, startMinutes, length, b =>
            {
                b.Label = label;
                b.Detail = detail;
                b.ColorIndex = color;
                b.IsPreview = preview;
                b.Entry = entry;
                b.DurationMinutes = length;
            });

            // the time at the gate after it lands, which the aircraft needs before it can leave again
            if (groundList == null || groundMinutes <= 0) return;
            Span(groundList, d, startMinutes + length, groundMinutes, b =>
            {
                b.Detail = detail;
                b.ColorIndex = color;
                b.IsPreview = preview;
                b.Entry = entry;
                b.DurationMinutes = groundMinutes;
                b.IsGround = true;
            });
        }

        // a stretch of the week as one block, or two where it runs past midnight into the next day
        private static void Span(List<TimelineBlock> list, int dayIndex, double start, double length, Action<TimelineBlock> fill)
        {
            int day = (dayIndex + (int) (start/1440))%7;
            double from = start%1440;
            double end = from + length;

            var first = new TimelineBlock { Day = day, Start = from, End = Math.Min(end, 1440) };
            fill(first);
            list.Add(first);
            if (end <= 1440) return;

            var second = new TimelineBlock { Day = (day + 1)%7, Start = 0, End = end - 1440 };
            fill(second);
            list.Add(second);
        }

        private void BuildBlocks(List<RouteTimeTableEntry> entries)
        {
            var f = _aircraft.Airliner;
            var routes = f.Routes.ToList();
            var keep = _selectedBlock?.Entry;
            var list = new List<TimelineBlock>();
            var ground = new List<TimelineBlock>();
            double groundMinutes = FleetAirlinerHelpers.GetMinTimeBetweenFlights(f).TotalMinutes;
            foreach (var e in entries)
            {
                TimeSpan flight = e.TimeTable.Route.GetFlightTime(f.Airliner.Type);
                string route = e.DepartureAirport.Profile.IATACode + " > " + e.Destination.Airport.Profile.IATACode;
                TimeSpan arrive = e.Time.Add(flight);
                string detail = string.Format(
                    "{0} {1}  |  {2}  |  leaves {3}, arrives {4}{5}  |  {6}",
                    e.Day, e.Destination.FlightCode, route, e.Time.ToString(@"hh\:mm"),
                    new TimeSpan(arrive.Hours, arrive.Minutes, 0).ToString(@"hh\:mm"), arrive.Days > 0 ? " (+" + arrive.Days + ")" : "", FormatSpan(flight));
                AddBlocks(list, ground, e.Day, e.Time.TotalMinutes, flight.TotalMinutes, groundMinutes,
                          e.Destination.FlightCode + " " + route, detail,
                          Math.Max(0, routes.IndexOf(e.TimeTable.Route)), false, e);
            }

            _blocks = list;
            _ground = ground;
            // what was just placed or moved is picked, so it can be seen and moved again straight away; otherwise
            // whatever was picked before stays picked, less any flight that has gone
            TimelineBlock pick = null;
            foreach (var e in _justAdded)
            {
                pick = list.FirstOrDefault(b => b.Entry == e);
                if (pick != null) break;
            }

            if (_justAdded.Count > 1)
            {
                // a group that was just moved stays picked as a group, so it can be moved again
                _selected.Clear();
                foreach (var e in _justAdded)
                {
                    if (list.Any(b => b.Entry == e)) _selected.Add(e);
                }
            }
            else
            {
                _selected.RemoveWhere(e => list.All(b => b.Entry != e));
                if (pick?.Entry != null)
                {
                    _selected.Clear();
                    _selected.Add(pick.Entry);
                }
            }

            _justAdded = new List<RouteTimeTableEntry>();
            _selectedBlock = pick ?? list.FirstOrDefault(b => b.Entry == keep) ?? list.FirstOrDefault(b => _selected.Contains(b.Entry));
            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
        }

        /// <summary>The dashed flights that "Add round trips" would put on the timeline.</summary>
        private void BuildPreview()
        {
            var list = new List<TimelineBlock>();
            var ground = new List<TimelineBlock>();
            if (_aircraft != null && _route != null && _from != null && Days != null)
            {
                var f = _aircraft.Airliner;
                var route = _route.Route;
                Airport from = _from.Airport;
                Airport to = from == route.Destination1 ? route.Destination2 : route.Destination1;
                TimeSpan flight = route.GetFlightTime(f.Airliner.Type);
                TimeSpan back = _time.Time.Add(flight).Add(new TimeSpan(0, _turnaround, 0));
                int color = Math.Max(0, f.Routes.ToList().IndexOf(route));
                double groundMinutes = FleetAirlinerHelpers.GetMinTimeBetweenFlights(f).TotalMinutes;
                foreach (var day in Days.Where(d => d.IsChecked).Select(d => d.Day))
                {
                    AddBlocks(list, ground, day, _time.Time.TotalMinutes, flight.TotalMinutes, groundMinutes,
                              from.Profile.IATACode + " > " + to.Profile.IATACode, "New flight", color, true, null);
                    DayOfWeek backDay = (DayOfWeek) (((int) day + back.Days) % 7);
                    AddBlocks(list, ground, backDay, back.Hours * 60 + back.Minutes, flight.TotalMinutes, groundMinutes,
                              to.Profile.IATACode + " > " + from.Profile.IATACode, "New flight", color, true, null);
                }
            }

            _preview = list;
            _previewGround = ground;
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
            if (_aircraft == null) return;
            string code = entry.Destination.FlightCode;
            var memo = Snapshot("removing " + code);
            RemoveEntryQuietly(entry);
            Remember(memo);
            Finish("Flight " + code + " removed.");
        }

        // takes the flight out without reloading or saying so, for removing several at once
        private void RemoveEntryQuietly(RouteTimeTableEntry entry)
        {
            if (_aircraft == null) return;
            var f = _aircraft.Airliner;
            var route = entry.TimeTable.Route;
            route.TimeTable.RemoveEntry(entry); // entries made by the route creator can still point at a temporary timetable
            entry.TimeTable.RemoveEntry(entry);

            if (!route.TimeTable.Entries.Any(e => e.Airliner == f))
            {
                f.RemoveRoute(route);
            }
        }

        private void RemoveRouteFlights()
        {
            if (_aircraft == null || _route == null)
            {
                return;
            }

            var f = _aircraft.Airliner;
            int n = _route.Route.TimeTable.Entries.Count(e => e.Airliner == f);
            var memo = Snapshot("clearing " + _route.Route.Destination1.Profile.IATACode + "-" + _route.Route.Destination2.Profile.IATACode);
            f.RemoveRoute(_route.Route);
            Remember(memo);
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
            var memo = Snapshot("adding flights");

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

            memo.Label = "adding " + rt.Entries.Count + " flight(s)";
            Remember(memo);
            _justAdded = rt.Entries.OrderBy(e => DayIndex(e.Day)).ThenBy(e => e.Time).ToList();
            Finish(string.Format("{0} flight(s) added ({1}).", rt.Entries.Count, roundTrip ? "round trips" : "one way"));
        }
    }
}
