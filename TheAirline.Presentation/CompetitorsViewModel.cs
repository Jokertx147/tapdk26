using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class CompetitorRow
    {
        public string Title { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }

        /// <summary>A short flag shown before the title (empty for none).</summary>
        public string Badge { get; set; }

        public bool HasBadge => !string.IsNullOrEmpty(Badge);

        /// <summary>A warning (a rival undercuts the player) rather than plain information.</summary>
        public bool IsWarning { get; set; }

        public Route MyRoute { get; set; }
        public Func<List<string>> Detail { get; set; }
    }

    /// <summary>Airlines > Competitors: who flies the player's routes and airports, their fares and loads, and their recent moves.</summary>
    public class CompetitorsViewModel : ObservableObject
    {
        public const string ByAirline = "Rival airlines", ByRoute = "My routes", ByAirport = "My airports", ByMove = "Recent moves";

        private readonly Action<Route> _open;
        private string _show = ByAirline, _filter, _summary, _detailTitle;
        private CompetitorRow _selected;

        public CompetitorsViewModel(Action<Route> open)
        {
            _open = open;
            LoadFilters();
            OpenCommand = new RelayCommand(() => { if (_selected?.MyRoute != null) _open?.Invoke(_selected.MyRoute); });
            RefreshCommand = new RelayCommand(Reload);
        }

        public List<string> ShowChoices { get; } = new List<string> {ByAirline, ByRoute, ByAirport, ByMove};

        public ObservableCollection<string> FilterChoices { get; } = new ObservableCollection<string> {"All"};
        public ObservableCollection<CompetitorRow> Rows { get; } = new ObservableCollection<CompetitorRow>();
        public ObservableCollection<string> DetailLines { get; } = new ObservableCollection<string>();
        public ICommand OpenCommand { get; }
        public ICommand RefreshCommand { get; }

        public string Show
        {
            get => _show;
            set
            {
                if (SetProperty(ref _show, value))
                {
                    LoadFilters();
                    Reload();
                }
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (value != null && SetProperty(ref _filter, value)) Reload();
            }
        }

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string DetailTitle { get => _detailTitle; private set => SetProperty(ref _detailTitle, value); }
        public bool HasSelection => _selected != null;
        public bool CanOpenRoute => _selected?.MyRoute != null;

        public CompetitorRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(CanOpenRoute));
                    LoadDetail();
                }
            }
        }

        private void LoadFilters()
        {
            FilterChoices.Clear();
            switch (_show)
            {
                case ByAirline:
                    foreach (var f in new[] {"All", "On my routes", "At my airports only"}) FilterChoices.Add(f);
                    break;
                case ByRoute:
                    foreach (var f in new[] {"All", "With rivals", "A rival is cheaper", "No rivals"}) FilterChoices.Add(f);
                    break;
                case ByAirport:
                    FilterChoices.Add("All");
                    break;
                default:
                    foreach (var f in new[] {"Last 30 days", "Last 90 days", "Last 180 days", "On my routes only"}) FilterChoices.Add(f);
                    break;
            }
            _filter = FilterChoices[0];
            OnPropertyChanged(nameof(Filter));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static string N(int n, string one, string many = null)
        {
            return n + " " + (n == 1 ? one : many ?? one + "s");
        }

        private static string Pct(double v)
        {
            return (v*100).ToString("0") + "%";
        }

        private static string Code(Airport a)
        {
            return a?.Profile.IATACode ?? "?";
        }

        private static string Pair(Route r)
        {
            return Code(r.Destination1) + " - " + Code(r.Destination2);
        }

        private static string Pair(Airport a, Airport b)
        {
            return Code(a) + " - " + Code(b);
        }

        private static string Name(Airline a)
        {
            return a == null ? "?" : a.Profile.IATACode + " " + a.Profile.Name;
        }

        private static string Load(double load)
        {
            return load > 0 ? Pct(load) + " full" : "load not known yet";
        }

        private static string FareWord(Route r)
        {
            return r.Type == Route.RouteType.Cargo ? "rate" : "fare";
        }

        public void Reload()
        {
            var me = GameObject.GetInstance()?.HumanAirline;
            if (me == null) return;
            var keep = _selected?.Title;
            Rows.Clear();
            try
            {
                switch (_show)
                {
                    case ByAirline: LoadAirlines(me); break;
                    case ByRoute: LoadRoutes(me); break;
                    case ByAirport: LoadAirports(me); break;
                    default: LoadMoves(me); break;
                }
            }
            catch (InvalidOperationException)
            {
                Summary = "The game changed while reading; press Refresh.";
            }
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Title == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
        }

        // ---- rival airlines

        private void LoadAirlines(Airline me)
        {
            List<RivalAirline> rivals = CompetitorService.Rivals(me);
            if (_filter == "On my routes") rivals = rivals.Where(r => r.SharedRoutes.Count > 0).ToList();
            if (_filter == "At my airports only") rivals = rivals.Where(r => r.SharedRoutes.Count == 0).ToList();
            foreach (RivalAirline r in rivals)
            {
                RivalAirline rival = r;
                string meet = (r.SharedRoutes.Count > 0 ? r.SharedRoutes.Count + " of your routes, " : "") + r.SharedAirports.Count + " of your airports";
                var line2 = new List<string>();
                if (r.FareLevel > 0) line2.Add("fares " + r.FareLevel.ToString("0.00") + " of the market");
                if (r.FareAgainstMine > 0) line2.Add(r.FareAgainstMine < 1 ? Pct(1 - r.FareAgainstMine) + " cheaper than you where you meet" : Pct(r.FareAgainstMine - 1) + " dearer than you where you meet");
                if (r.Load > 0) line2.Add(Pct(r.Load) + " full");
                if (r.LastMonthIncome > 0) line2.Add("last month " + Money(r.LastMonthResult) + " (" + Pct(r.LastMonthResult/r.LastMonthIncome) + " margin)");
                Rows.Add(new CompetitorRow
                {
                    Title = Name(r.Airline),
                    Line1 = meet + "  |  " + r.Fleet + " aircraft, " + N(r.Routes, "route") + ", " + r.Airline.Mentality.ToString().ToLowerInvariant() + " style",
                    Line2 = string.Join("  |  ", line2),
                    Badge = r.FareAgainstMine > 0 && r.FareAgainstMine < 0.97 ? "CHEAPER" : null,
                    IsWarning = true,
                    Detail = () => AirlineDetail(me, rival)
                });
            }
            int onRoutes = rivals.Count(r => r.SharedRoutes.Count > 0);
            Summary = rivals.Count == 0
                ? "No other airline flies your routes or from your airports."
                : string.Format("{0} from your airports; {1} of them also {2} your routes.", rivals.Count == 1 ? "1 airline flies" : rivals.Count + " airlines fly", onRoutes, onRoutes == 1 ? "flies" : "fly");
        }

        private List<string> AirlineDetail(Airline me, RivalAirline r)
        {
            var lines = new List<string>();
            if (r.SharedRoutes.Count > 0)
            {
                lines.Add("Your routes they fly too:");
                foreach (RouteRivals s in r.SharedRoutes.OrderBy(x => Pair(x.Mine)))
                {
                    foreach (RivalOnRoute x in s.Rivals.Where(x => x.Airline == r.Airline))
                    {
                        lines.Add(string.Format("  {0}: them {1} ({2}, {3} a week, {4})  |  you {5} ({6}, {7} a week)",
                            Pair(s.Mine), Money(x.Fare), Load(x.Load), x.WeeklyFlights, x.Aircraft, Money(s.MyFare), Load(s.MyLoad), s.MyWeeklyFlights));
                    }
                }
            }
            if (r.SharedAirports.Count > 0)
            {
                lines.Add("Your airports they fly from:");
                foreach (Airport ap in r.SharedAirports.OrderBy(Code))
                {
                    int routes = RouteIndex.At(ap).Count(x => x.Airline == r.Airline && x.HasAirliner);
                    int gates = 0;
                    try { gates = ap.GetAirlineContracts(r.Airline).Sum(c => c.NumberOfGates); } catch (Exception) { }
                    lines.Add(string.Format("  {0} {1}: {2}, {3}", Code(ap), ap.Profile.Town?.Name, N(routes, "route"), N(gates, "gate")));
                }
            }
            var moves = CompetitorService.Moves(me, 180).Where(m => m.Airline == r.Airline).Take(30).ToList();
            lines.Add(moves.Count == 0 ? "No moves at your airports in the last 180 days." : "Their moves at your airports:");
            lines.AddRange(moves.Select(m => "  " + MoveText(m, me)));
            return lines;
        }

        // ---- my routes

        private void LoadRoutes(Airline me)
        {
            List<RouteRivals> all = CompetitorService.ForRoutes(me);
            IEnumerable<RouteRivals> shown = all;
            switch (_filter)
            {
                case "With rivals": shown = all.Where(r => r.Rivals.Count > 0); break;
                case "A rival is cheaper": shown = all.Where(Undercut); break;
                case "No rivals": shown = all.Where(r => r.Rivals.Count == 0); break;
            }
            foreach (RouteRivals r in shown.OrderByDescending(r => r.Rivals.Count).ThenBy(r => Pair(r.Mine)))
            {
                RouteRivals rr = r;
                string word = FareWord(r.Mine);
                Rows.Add(new CompetitorRow
                {
                    Title = Pair(r.Mine) + (r.Mine.Type == Route.RouteType.Cargo ? "  (cargo)" : ""),
                    Line1 = r.Rivals.Count == 0
                        ? "No rival flies it"
                        : r.Rivals.Count + (r.Rivals.Count == 1 ? " rival: " : " rivals: ") + string.Join(", ", r.Rivals.Take(4).Select(x => x.Airline.Profile.IATACode + " " + Money(x.Fare) + " (" + (x.Load > 0 ? Pct(x.Load) : "?") + ")")) + (r.Rivals.Count > 4 ? " ..." : ""),
                    Line2 = "you " + Money(r.MyFare) + " " + word + " (" + Load(r.MyLoad) + ", " + N(r.MyWeeklyFlights, "flight") + " a week)" + (r.MarketFare > 0 ? "  |  market " + Money(r.MarketFare) : ""),
                    Badge = Undercut(r) ? "CHEAPER RIVAL" : r.Rivals.Count == 0 ? "ALONE" : null,
                    IsWarning = Undercut(r),
                    MyRoute = r.Mine,
                    Detail = () => RouteDetail(me, rr)
                });
            }
            int with = all.Count(r => r.Rivals.Count > 0), under = all.Count(Undercut);
            Summary = all.Count == 0
                ? "You have no routes yet."
                : string.Format("{0}: {1} with rivals, {2} where a rival is cheaper than you, {3} you fly alone.", N(all.Count, "route"), with, under, all.Count - with);
        }

        private static bool Undercut(RouteRivals r)
        {
            return r.MyFare > 0 && r.Rivals.Any(x => x.Fare > 0 && x.Fare < r.MyFare*0.97);
        }

        private List<string> RouteDetail(Airline me, RouteRivals r)
        {
            string word = FareWord(r.Mine);
            var lines = new List<string>
            {
                string.Format("You: {0} {1}, {2}, {3} a week ({4})", Money(r.MyFare), word, Load(r.MyLoad), N(r.MyWeeklyFlights, "flight"), r.Mine.GetAirliners().Count == 0 ? "no aircraft yet" : string.Join(", ", r.Mine.GetAirliners().Select(a => a.Airliner.Type.Name).Distinct()))
            };
            if (r.MarketFare > 0) lines.Add("Market fare: " + Money(r.MarketFare));
            if (r.Rivals.Count == 0) lines.Add("No other airline flies this pair.");
            foreach (RivalOnRoute x in r.Rivals)
            {
                string against = r.MyFare > 0 && x.Fare > 0 ? (x.Fare < r.MyFare ? ", " + Pct(1 - x.Fare/r.MyFare) + " cheaper than you" : x.Fare > r.MyFare ? ", " + Pct(x.Fare/r.MyFare - 1) + " dearer than you" : ", the same as you") : "";
                lines.Add(string.Format("{0}: {1} {2}{3}, {4}, {5} a week ({6})", Name(x.Airline), Money(x.Fare), word, against, Load(x.Load), N(x.WeeklyFlights, "flight"), x.Aircraft));
            }
            int total = r.MyWeeklyFlights + r.Rivals.Sum(x => x.WeeklyFlights);
            if (r.Rivals.Count > 0 && total > 0) lines.Add("Your share of the flights on the pair: " + Pct((double) r.MyWeeklyFlights/total));
            var moves = CompetitorService.Moves(me, 180).Where(m => SamePair(m, r.Mine)).ToList();
            if (moves.Count > 0)
            {
                lines.Add("Rivals' moves on this pair:");
                lines.AddRange(moves.Select(m => "  " + MoveText(m, me)));
            }
            return lines;
        }

        private static bool SamePair(RivalMove m, Route r)
        {
            return (m.A == r.Destination1 && m.B == r.Destination2) || (m.A == r.Destination2 && m.B == r.Destination1);
        }

        // ---- my airports

        private void LoadAirports(Airline me)
        {
            List<AirportRivals> all = CompetitorService.ForAirports(me).OrderByDescending(a => a.Airlines.Count).ThenBy(a => Code(a.Airport)).ToList();
            foreach (AirportRivals a in all)
            {
                AirportRivals ar = a;
                int routes = a.Airlines.Sum(x => x.Item2);
                Rows.Add(new CompetitorRow
                {
                    Title = Code(a.Airport) + "  " + a.Airport.Profile.Name,
                    Line1 = string.Format("you: {0}, {1}  |  {2} with {3}  |  {4}, {5} free", N(a.MyRoutes, "route"), N(a.MyGates, "gate"), N(a.Airlines.Count, "other airline"), N(routes, "route"), N(a.Gates, "gate"), a.FreeGates),
                    Line2 = a.Airlines.Count == 0 ? "No other airline flies from here" : "biggest: " + string.Join(", ", a.Airlines.Take(4).Select(x => x.Item1.Profile.IATACode + " " + N(x.Item2, "route"))),
                    Badge = a.Airlines.Count == 0 ? "ALONE" : a.FreeGates == 0 ? "NO FREE GATES" : null,
                    IsWarning = a.FreeGates == 0 && a.Airlines.Count > 0,
                    Detail = () => AirportDetail(me, ar)
                });
            }
            Summary = all.Count == 0
                ? "You fly from no airport yet."
                : string.Format("{0}: {1} where other airlines fly too, {2} with no free gates left.", N(all.Count, "airport"), all.Count(a => a.Airlines.Count > 0), all.Count(a => a.FreeGates == 0));
        }

        private List<string> AirportDetail(Airline me, AirportRivals a)
        {
            var lines = new List<string>
            {
                a.Airport.Profile.Name + ", " + a.Airport.Profile.Town?.Name + ", " + a.Airport.Profile.Country?.Name,
                string.Format("You: {0}, {1}. Gates at the airport: {2} ({3} free).", N(a.MyRoutes, "route"), N(a.MyGates, "gate"), a.Gates, a.FreeGates)
            };
            if (a.Airlines.Count == 0) lines.Add("No other airline flies from here.");
            else lines.Add("Other airlines here:");
            foreach (var x in a.Airlines) lines.Add(string.Format("  {0}: {1}, {2}", Name(x.Item1), N(x.Item2, "route"), N(x.Item3, "gate")));
            var moves = CompetitorService.Moves(me, 180).Where(m => m.A == a.Airport || m.B == a.Airport).Take(30).ToList();
            if (moves.Count > 0)
            {
                lines.Add("Rivals' moves here:");
                lines.AddRange(moves.Select(m => "  " + MoveText(m, me)));
            }
            return lines;
        }

        // ---- recent moves

        private void LoadMoves(Airline me)
        {
            int days = _filter == "Last 30 days" ? 30 : _filter == "Last 90 days" ? 90 : 180;
            var myPairs = new HashSet<string>(me.Routes.ToList().Select(r => Key(r.Destination1, r.Destination2)));
            List<RivalMove> moves = CompetitorService.Moves(me, days);
            var myAirports = new HashSet<Airport>(CompetitorService.MyAirports(me));
            if (_filter == "On my routes only") moves = moves.Where(m => myPairs.Contains(Key(m.A, m.B))).ToList();
            foreach (RivalMove m in moves.Take(500))
            {
                RivalMove move = m;
                bool onMine = myPairs.Contains(Key(m.A, m.B));
                Rows.Add(new CompetitorRow
                {
                    Title = DateFormatter.Date(m.Date) + "  " + MoveText(m, me, false),
                    Line1 = onMine ? "You fly " + Pair(m.A, m.B) + " too" : "At your airport " + string.Join(" and ", new[] {m.A, m.B}.Where(myAirports.Contains).Select(Code)),
                    Badge = onMine ? "YOUR ROUTE" : null,
                    IsWarning = onMine && (m.Kind == RivalMoveKind.Opened || m.Kind == RivalMoveKind.FareCut),
                    MyRoute = onMine ? me.Routes.ToList().FirstOrDefault(r => Key(r.Destination1, r.Destination2) == Key(m.A, m.B) && r.IsCargoRoute == m.IsCargo) : null,
                    Detail = () => MoveDetail(me, move)
                });
            }
            Summary = string.Format("{0} by rivals at your airports in the last {1} days: {2} routes opened, {3} given up, {4} fare cuts, {5} fare rises. (Routes given up and fare changes are counted from when the game was started or loaded.)",
                N(moves.Count, "move"), days, moves.Count(m => m.Kind == RivalMoveKind.Opened), moves.Count(m => m.Kind == RivalMoveKind.Dropped),
                moves.Count(m => m.Kind == RivalMoveKind.FareCut), moves.Count(m => m.Kind == RivalMoveKind.FareRise));
        }

        private static string Key(Airport a, Airport b)
        {
            string x = Code(a), y = Code(b);
            return string.CompareOrdinal(x, y) < 0 ? x + y : y + x;
        }

        private string MoveText(RivalMove m, Airline me, bool withDate = true)
        {
            string who = m.Airline?.Profile.IATACode ?? "?";
            string pair = Pair(m.A, m.B) + (m.IsCargo ? " (cargo)" : "");
            string what;
            switch (m.Kind)
            {
                case RivalMoveKind.Opened: what = who + " opened " + pair; break;
                case RivalMoveKind.Dropped: what = who + " gave up " + pair; break;
                case RivalMoveKind.FareCut: what = who + " cut " + (m.IsCargo ? "its rate" : "its fares") + " on " + pair + " by " + Pct(1 - m.Factor); break;
                default: what = who + " raised " + (m.IsCargo ? "its rate" : "its fares") + " on " + pair + " by " + Pct(m.Factor - 1); break;
            }
            return withDate ? DateFormatter.Date(m.Date) + ": " + what : what;
        }

        private List<string> MoveDetail(Airline me, RivalMove m)
        {
            var lines = new List<string> {MoveText(m, me), Name(m.Airline)};
            Route mine = me.Routes.ToList().FirstOrDefault(r => Key(r.Destination1, r.Destination2) == Key(m.A, m.B) && r.IsCargoRoute == m.IsCargo);
            if (mine != null)
            {
                var rr = CompetitorService.ForRoutes(me).FirstOrDefault(x => x.Mine == mine);
                if (rr != null) lines.AddRange(RouteDetail(me, rr));
            }
            else
            {
                Route theirs = RouteIndex.Direct(m.A, m.B).FirstOrDefault(r => r.Airline == m.Airline);
                if (theirs != null)
                {
                    lines.Add(string.Format("They fly it {0} times a week ({1}) at {2}.", theirs.TimeTable.Entries.Count, string.Join(", ", theirs.GetAirliners().Select(a => a.Airliner.Type.Name).Distinct()), Money(theirs is CargoRoute c ? c.PricePerUnit : theirs is PassengerRoute p ? AIPricingService.EconomyFare(p) : 0)));
                }
                lines.Add("You don't fly this pair.");
            }
            return lines;
        }

        private void LoadDetail()
        {
            DetailLines.Clear();
            DetailTitle = _selected?.Title;
            if (_selected?.Detail == null) return;
            try
            {
                foreach (string line in _selected.Detail()) DetailLines.Add(line);
            }
            catch (InvalidOperationException)
            {
                DetailLines.Add("The game changed while reading; press Refresh.");
            }
        }
    }
}
