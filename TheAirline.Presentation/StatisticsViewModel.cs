using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.General.Statistics;

namespace TheAirline.Presentation
{
    /// <summary>One line of the chart: an airline's figure for each period (null where it isn't known).</summary>
    public class ChartSeries
    {
        public string Name { get; set; }
        public string Color { get; set; }
        public double?[] Values { get; set; }
        public bool IsMine { get; set; }
    }

    /// <summary>What the chart draws: the periods along the bottom, a line per airline, and how a value is written.</summary>
    public class ChartData
    {
        public string[] Labels { get; set; } = new string[0];
        public List<ChartSeries> Series { get; set; } = new List<ChartSeries>();
        public Func<double, string> Format { get; set; } = v => v.ToString("#,0");

        /// <summary>The axis's shorter style (e.g. $50.7M, 12.5k).</summary>
        public Func<double, string> AxisFormat { get; set; } = v => v.ToString("#,0");

        /// <summary>A share (0 to 1): the axis goes from 0 to 100%.</summary>
        public bool IsShare { get; set; }
    }

    /// <summary>An airline that can be shown on the chart.</summary>
    public class ChartAirline : ObservableObject
    {
        private bool _shown;
        public Airline Airline { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public bool IsMine { get; set; }
        public Action Changed { get; set; }

        public bool Shown
        {
            get => _shown;
            set
            {
                if (SetProperty(ref _shown, value)) Changed?.Invoke();
            }
        }
    }

    public class ChartCell
    {
        public string Text { get; set; }
        public string Color { get; set; }
    }

    /// <summary>A row of the chart's table: a period and each shown airline's value.</summary>
    public class ChartTableRow
    {
        public string Period { get; set; }
        public List<ChartCell> Cells { get; set; } = new List<ChartCell>();
    }

    public class LeagueRow
    {
        public string Rank { get; set; }
        public string Name { get; set; }
        public bool IsMine { get; set; }
        public string Passengers { get; set; }
        public string Flights { get; set; }
        public string Load { get; set; }
        public string OnTime { get; set; }
        public string Result { get; set; }
        public string Fleet { get; set; }
    }

    public class AirportStatRow
    {
        public string Airport { get; set; }
        public string Arrivals { get; set; }
        public string Passengers { get; set; }
        public string LastYear { get; set; }
        public string Change { get; set; }
        public bool IsUp { get; set; }
        public bool IsDown { get; set; }
        public string Share { get; set; }
    }

    /// <summary>
    ///     My Airline > Statistics: a figure by month or by year for the player's airline and its closest rivals (chart and
    ///     table), every airline's year so far, and the player's busiest airports this year against last.
    /// </summary>
    public class StatisticsViewModel : ObservableObject
    {
        public const string ByMonth = "By month", ByYear = "By year";

        public static readonly string[] Figures =
        {
            "Passengers", "Flights", "Seats filled", "On time", "Cargo", "Income", "Costs", "Operating result", "Cash"
        };

        public static readonly string[] LeagueSorts = {"Passengers", "Flights", "Seats filled", "On time", "Operating result", "Fleet"};

        private string _figure = Figures[0], _period = ByMonth, _leagueSort = LeagueSorts[0], _summary, _leagueTitle, _airportsTitle;
        private ChartData _chart = new ChartData();
        private bool _loading;
        private Airline _for;

        public StatisticsViewModel()
        {
            RefreshCommand = new RelayCommand(Reload);
        }

        public RelayCommand RefreshCommand { get; }
        public List<string> FigureChoices => Figures.ToList();
        public List<string> PeriodChoices => new List<string> {ByMonth, ByYear};
        public List<string> LeagueSortChoices => LeagueSorts.ToList();
        public ObservableCollection<ChartAirline> Airlines { get; } = new ObservableCollection<ChartAirline>();
        public ObservableCollection<ChartTableRow> Table { get; } = new ObservableCollection<ChartTableRow>();
        public ObservableCollection<LeagueRow> League { get; } = new ObservableCollection<LeagueRow>();
        public ObservableCollection<AirportStatRow> AirportRows { get; } = new ObservableCollection<AirportStatRow>();

        public ChartData Chart { get => _chart; private set => SetProperty(ref _chart, value); }
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public List<ChartCell> TableHeader { get; private set; } = new List<ChartCell>();
        public string LeagueTitle { get => _leagueTitle; private set => SetProperty(ref _leagueTitle, value); }
        public string AirportsTitle { get => _airportsTitle; private set => SetProperty(ref _airportsTitle, value); }
        public bool HasAirports => AirportRows.Count > 0;

        public string Figure
        {
            get => _figure;
            set
            {
                if (value != null && SetProperty(ref _figure, value) && !_loading) LoadChart();
            }
        }

        public string Period
        {
            get => _period;
            set
            {
                if (value != null && SetProperty(ref _period, value) && !_loading) LoadChart();
            }
        }

        public string LeagueSort
        {
            get => _leagueSort;
            set
            {
                if (value != null && SetProperty(ref _leagueSort, value) && !_loading) LoadLeague();
            }
        }

        // ---- the figures ----

        private static StatisticsType T(string name) => StatisticsTypes.GetStatisticsType(name);

        private static string Money(double v) => Convert.ToString(new ValueCurrencyFormatter().Convert(v));

        /// <summary>A year's flying from the game's statistics (kept by year since the statistics fix).</summary>
        private static MonthOps YearOps(Airline a, int year)
        {
            var s = a.Statistics;
            var o = new MonthOps {Year = year, Month = 12};
            o.Flights = (int) s.GetStatisticsValue(year, T("Arrivals"));
            o.OnTime = (int) s.GetStatisticsValue(year, T("On-Time"));
            o.Passengers = (long) s.GetStatisticsValue(year, T("Passengers"));
            o.Seats = (long) s.GetStatisticsValue(year, T("Capacity"));
            o.Cargo = s.GetStatisticsValue(year, T("Cargo"));
            return o;
        }

        private static void YearMoney(Airline a, int year, out double income, out double costs)
        {
            income = 0;
            costs = 0;
            foreach (Invoice.InvoiceType t in Enum.GetValues(typeof(Invoice.InvoiceType)))
            {
                if (t == Invoice.InvoiceType.Total || t == Invoice.InvoiceType.Purchases || t == Invoice.InvoiceType.Loans) continue;
                double v = a.GetInvoicesAmountYear(year, t);
                if (v > 0) income += v;
                else costs += v;
            }
        }

        /// <summary>The figure for an airline in a period (month 0: the whole year); null when it isn't known.</summary>
        public static double? Value(string figure, Airline a, int year, int month)
        {
            bool now = year == GameObject.GetInstance().GameTime.Year && (month == 0 || month == GameObject.GetInstance().GameTime.Month);
            MonthOps o = month == 0 ? YearOps(a, year) : AirlineReportService.Ops(a, year, month);
            double income, costs;
            switch (figure)
            {
                case "Passengers": return o.Passengers;
                case "Flights": return o.Flights;
                case "Seats filled": return o.Seats > 0 ? o.Load : (double?) null;
                case "On time": return o.Flights > 0 ? o.OnTimeShare : (double?) null;
                case "Cargo": return o.Cargo;
                case "Income":
                case "Costs":
                case "Operating result":
                    if (month == 0) YearMoney(a, year, out income, out costs);
                    else AirlineReportService.IncomeAndCosts(a, year, month, out income, out costs);
                    return figure == "Income" ? income : figure == "Costs" ? -costs : income + costs;
                case "Cash":
                    if (now) return a.Money;
                    if (month == 0)
                    {
                        MonthOps dec = AirlineReportService.Ops(a, year, 12);
                        return dec.Closed ? dec.Cash : (double?) null;
                    }
                    return o.Closed ? o.Cash : (double?) null;
            }
            return null;
        }

        /// <summary>A number in short: 950, 12.5k, 3.4M, 1.2B.</summary>
        public static string Compact(double v)
        {
            double a = Math.Abs(v);
            string sign = v < 0 ? "-" : "";
            if (a >= 1e9) return sign + (a/1e9).ToString("0.#") + "B";
            if (a >= 1e6) return sign + (a/1e6).ToString("0.#") + "M";
            if (a >= 1e4) return sign + (a/1e3).ToString("0") + "k";
            if (a >= 1e3) return sign + (a/1e3).ToString("0.#") + "k";
            return sign + a.ToString("0");
        }

        private static Func<double, string> AxisFormatFor(string figure)
        {
            switch (figure)
            {
                case "Seats filled":
                case "On time":
                    return v => (v*100).ToString("0") + "%";
                case "Cargo":
                    return v => Compact(v) + " t";
                case "Income":
                case "Costs":
                case "Operating result":
                case "Cash":
                    // the currency's sign from the game's formatter, with the number in short
                    string one = Money(1);
                    int digit = one.IndexOfAny("0123456789".ToCharArray());
                    string prefix = digit > 0 ? one.Substring(0, digit).Replace("-", "") : "";
                    return v => (v < 0 ? "-" : "") + prefix + Compact(Math.Abs(v));
                default:
                    return Compact;
            }
        }

        private static Func<double, string> FormatFor(string figure)
        {
            switch (figure)
            {
                case "Seats filled":
                case "On time":
                    return v => (v*100).ToString("0") + "%";
                case "Cargo":
                    return v => v.ToString("#,0") + " t";
                case "Income":
                case "Costs":
                case "Operating result":
                case "Cash":
                    return Money;
                default:
                    return v => v.ToString("#,0");
            }
        }

        // ---- loading ----

        public void Reload()
        {
            var game = GameObject.GetInstance();
            Airline me = game?.HumanAirline;
            if (me == null) return;
            _loading = true;
            // the player and its closest rivals (as on the map, in the map's colours); the first three rivals shown
            var keep = Airlines.Where(x => x.Shown).Select(x => x.Airline).ToList();
            bool first = _for != me || Airlines.Count == 0;
            _for = me;
            Airlines.Clear();
            Airlines.Add(new ChartAirline {Airline = me, Name = me.Profile.IATACode + " " + me.Profile.Name + " (you)", Color = "#145ac8", IsMine = true, Shown = true, Changed = LoadChart});
            List<RivalAirline> rivals;
            try { rivals = CompetitorService.Rivals(me); }
            catch (Exception) { rivals = new List<RivalAirline>(); }
            var others = rivals.Select(r => r.Airline).ToList();
            // a new airline with no rivals yet: the biggest airlines
            if (others.Count < 5)
                others.AddRange(TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => a != me && !others.Contains(a)).OrderByDescending(a => a.Fleet.Count).Take(5 - others.Count));
            int i = 0;
            foreach (Airline a in others.Take(WorldMapViewModel.RivalColors.Length - 1))
            {
                Airlines.Add(new ChartAirline
                {
                    Airline = a, Name = a.Profile.IATACode + " " + a.Profile.Name, Color = WorldMapViewModel.RivalColors[i],
                    Shown = first ? i < 3 : keep.Contains(a), Changed = LoadChart
                });
                i++;
            }
            _loading = false;
            LoadChart();
            LoadLeague();
            LoadAirports();
        }

        /// <summary>The periods shown: the last 13 months (oldest first), or every year of the game.</summary>
        private List<Tuple<int, int, string>> Periods()
        {
            var game = GameObject.GetInstance();
            var list = new List<Tuple<int, int, string>>();
            if (_period == ByYear)
            {
                for (int y = game.StartDate.Year; y <= game.GameTime.Year; y++)
                    list.Add(Tuple.Create(y, 0, y == game.GameTime.Year ? y + " so far" : y.ToString()));
                return list;
            }
            foreach (var m in AirlineReportService.ReportMonths(game.HumanAirline).AsEnumerable().Reverse())
            {
                string name = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m.Item2) + " " + (m.Item1 % 100).ToString("00");
                list.Add(Tuple.Create(m.Item1, m.Item2, m.Item1 == game.GameTime.Year && m.Item2 == game.GameTime.Month ? name + "*" : name));
            }
            return list;
        }

        private void LoadChart()
        {
            if (_loading || _for == null) return;
            var periods = Periods();
            var data = new ChartData
            {
                Labels = periods.Select(p => p.Item3).ToArray(), Format = FormatFor(_figure), AxisFormat = AxisFormatFor(_figure),
                IsShare = _figure == "Seats filled" || _figure == "On time"
            };
            foreach (ChartAirline a in Airlines.Where(x => x.Shown))
            {
                data.Series.Add(new ChartSeries
                {
                    Name = a.Airline.Profile.IATACode, Color = a.Color, IsMine = a.IsMine,
                    Values = periods.Select(p => Value(_figure, a.Airline, p.Item1, p.Item2)).ToArray()
                });
            }
            Chart = data;

            Table.Clear();
            TableHeader = data.Series.Select(x => new ChartCell {Text = x.Name, Color = x.Color}).ToList();
            OnPropertyChanged(nameof(TableHeader));
            for (int k = periods.Count - 1; k >= 0; k--)
            {
                int idx = k;
                Table.Add(new ChartTableRow
                {
                    Period = periods[k].Item3,
                    Cells = data.Series.Select(x => new ChartCell {Text = x.Values[idx].HasValue ? data.Format(x.Values[idx].Value) : "-", Color = x.Color}).ToList()
                });
            }

            // what the chart says about the player
            ChartSeries mine = data.Series.FirstOrDefault(s => s.IsMine);
            int last = periods.Count - 1, prev = periods.Count - 2;
            if (mine == null || last < 0) Summary = "";
            else
            {
                // the latest full period (a month or year still running isn't compared)
                int full = periods[last].Item3.EndsWith("*") || periods[last].Item3.EndsWith("so far") ? last - 1 : last;
                if (full < 0 || !mine.Values[full].HasValue) Summary = _figure + ": " + (mine.Values[last].HasValue ? data.Format(mine.Values[last].Value) + " so far" : "nothing yet") + ".";
                else
                {
                    double v = mine.Values[full].Value;
                    string text = string.Format("{0} in {1}: {2}", _figure, periods[full].Item3, data.Format(v));
                    if (full > 0 && mine.Values[full - 1].HasValue)
                    {
                        double before = mine.Values[full - 1].Value;
                        if (data.IsShare) text += string.Format(" ({0:+0.0;-0.0;0.0} points on {1})", (v - before)*100, periods[full - 1].Item3);
                        else if (Math.Abs(before) >= 1) text += string.Format(" ({0:+0;-0;0}% on {1})", (v - before)/Math.Abs(before)*100, periods[full - 1].Item3);
                    }
                    var ranked = data.Series.Where(s => s.Values[full].HasValue).OrderByDescending(s => _figure == "Costs" ? -s.Values[full].Value : s.Values[full].Value).ToList();
                    if (ranked.Count > 1) text += string.Format(", {0} of the {1} airlines shown", Ordinal(ranked.IndexOf(mine) + 1), ranked.Count);
                    Summary = text + ".";
                }
            }
        }

        private static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }

        // every airline's year so far
        private void LoadLeague()
        {
            if (_for == null) return;
            int year = GameObject.GetInstance().GameTime.Year;
            LeagueTitle = "Every airline in " + year + " so far";
            var rows = new List<Tuple<Airline, MonthOps, double, int>>();
            foreach (Airline a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().ToList())
            {
                double income, costs;
                YearMoney(a, year, out income, out costs);
                rows.Add(Tuple.Create(a, YearOps(a, year), income + costs, a.Fleet.Count));
            }
            Func<Tuple<Airline, MonthOps, double, int>, double> key;
            switch (_leagueSort)
            {
                case "Flights": key = r => r.Item2.Flights; break;
                case "Seats filled": key = r => r.Item2.Seats > 0 ? r.Item2.Load : -1; break;
                case "On time": key = r => r.Item2.Flights > 0 ? r.Item2.OnTimeShare : -1; break;
                case "Operating result": key = r => r.Item3; break;
                case "Fleet": key = r => r.Item4; break;
                default: key = r => r.Item2.Passengers; break;
            }
            League.Clear();
            int rank = 0;
            foreach (var r in rows.OrderByDescending(key).ThenBy(r => r.Item1.Profile.Name))
            {
                rank++;
                MonthOps o = r.Item2;
                League.Add(new LeagueRow
                {
                    Rank = rank.ToString(), Name = r.Item1.Profile.IATACode + " " + r.Item1.Profile.Name, IsMine = r.Item1 == _for,
                    Passengers = o.Passengers.ToString("#,0"), Flights = o.Flights.ToString("#,0"),
                    Load = o.Seats > 0 ? (o.Load*100).ToString("0") + "%" : "-", OnTime = o.Flights > 0 ? (o.OnTimeShare*100).ToString("0") + "%" : "-",
                    Result = Money(r.Item3), Fleet = r.Item4.ToString()
                });
            }
        }

        // the player's busiest airports: this year against last, and its share of each airport's flights
        private void LoadAirports()
        {
            AirportRows.Clear();
            if (_for == null) return;
            int year = GameObject.GetInstance().GameTime.Year;
            AirportsTitle = "Your busiest airports in " + year + " so far";
            var rows = new List<Tuple<Airport, double, double, double, double>>();
            foreach (Airport ap in TheAirline.Models.Airports.Airports.GetAllActiveAirports())
            {
                double arr = ap.Statistics.GetStatisticsValue(year, _for, T("Arrivals"));
                if (arr <= 0) continue;
                double pax = ap.Statistics.GetStatisticsValue(year, _for, T("Passengers"));
                double last = ap.Statistics.GetStatisticsValue(year - 1, _for, T("Passengers"));
                double all = ap.Statistics.GetTotalValue(year, T("Arrivals"));
                rows.Add(Tuple.Create(ap, arr, pax, last, all));
            }
            foreach (var r in rows.OrderByDescending(r => r.Item2).Take(15))
            {
                double change = r.Item4 > 0 ? (r.Item3 - r.Item4)/r.Item4 : 0;
                AirportRows.Add(new AirportStatRow
                {
                    Airport = r.Item1.Profile.IATACode + " " + r.Item1.Profile.Name, Arrivals = r.Item2.ToString("#,0"), Passengers = r.Item3.ToString("#,0"),
                    LastYear = r.Item4 > 0 ? r.Item4.ToString("#,0") : "-", Change = r.Item4 > 0 ? string.Format("{0:+0;-0;0}%", change*100) : "new",
                    IsUp = r.Item4 > 0 && change > 0, IsDown = r.Item4 > 0 && change < 0,
                    Share = r.Item5 > 0 ? (r.Item2/r.Item5*100).ToString("0") + "% of its flights" : ""
                });
            }
            OnPropertyChanged(nameof(HasAirports));
        }
    }
}
