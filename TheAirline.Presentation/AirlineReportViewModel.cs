using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>One figure at the top of the report, with its change on the month before.</summary>
    public class ReportTile
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string Change { get; set; }

        /// <summary>1 better than the month before, -1 worse, 0 the same or not known.</summary>
        public int Trend { get; set; }

        public bool IsUp => Trend > 0;
        public bool IsDown => Trend < 0;
    }

    public class ReportMoneyRow
    {
        public string Name { get; set; }
        public string Amount { get; set; }
        public string Previous { get; set; }
        public bool IsIncome { get; set; }
    }

    /// <summary>A line of the report that may lead somewhere (a route, the competitors, the fleet, the finances).</summary>
    public class ReportLinkRow
    {
        public string Title { get; set; }
        public string Text { get; set; }
        public string LinkText { get; set; }
        public bool HasLink => Command != null;
        public bool IsWarning { get; set; }
        public ICommand Command { get; set; }
    }

    /// <summary>My Airline > Monthly report: a month's money, flying, best and worst routes, closest rivals and what needs attention.</summary>
    public class AirlineReportViewModel : ObservableObject
    {
        private readonly Action<ReportItem> _open;
        private List<Tuple<int, int>> _months = new List<Tuple<int, int>>();
        private string _month, _summary, _cashLine, _capitalLine;
        private bool _loading;

        public AirlineReportViewModel(Action<ReportItem> open)
        {
            _open = open;
            RefreshCommand = new RelayCommand(Reload);
        }

        public ObservableCollection<string> MonthChoices { get; } = new ObservableCollection<string>();
        public ObservableCollection<ReportTile> Tiles { get; } = new ObservableCollection<ReportTile>();
        public ObservableCollection<ReportMoneyRow> Money { get; } = new ObservableCollection<ReportMoneyRow>();
        public ObservableCollection<ReportLinkRow> Best { get; } = new ObservableCollection<ReportLinkRow>();
        public ObservableCollection<ReportLinkRow> Worst { get; } = new ObservableCollection<ReportLinkRow>();
        public ObservableCollection<ReportLinkRow> Rivals { get; } = new ObservableCollection<ReportLinkRow>();
        public ObservableCollection<ReportLinkRow> Attention { get; } = new ObservableCollection<ReportLinkRow>();
        public ICommand RefreshCommand { get; }

        /// <summary>The report shown (for the tests).</summary>
        public AirlineReport Report { get; private set; }

        public string PreviousHeader { get; private set; }
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string CapitalLine { get => _capitalLine; private set => SetProperty(ref _capitalLine, value); }
        public bool HasCapital => !string.IsNullOrEmpty(_capitalLine);
        public string CashLine { get => _cashLine; private set => SetProperty(ref _cashLine, value); }
        public bool HasBest => Best.Count > 0;
        public bool HasWorst => Worst.Count > 0;
        public bool HasRivals => Rivals.Count > 0;
        public bool NothingToDo => Attention.Count == 0;

        public string Month
        {
            get => _month;
            set
            {
                if (value != null && SetProperty(ref _month, value) && !_loading) Load();
            }
        }

        private static string Name(Tuple<int, int> m, DateTime now)
        {
            return AirlineReportService.MonthName(m.Item1, m.Item2) + (m.Item1 == now.Year && m.Item2 == now.Month ? " (so far)" : "");
        }

        /// <summary>Reads the months again; keeps the month shown (the last month that ended, at first).</summary>
        public void Reload()
        {
            var game = GameObject.GetInstance();
            if (game?.HumanAirline == null) return;
            DateTime now = game.GameTime;
            _months = AirlineReportService.ReportMonths(game.HumanAirline);
            _loading = true;
            string keep = _month;
            MonthChoices.Clear();
            foreach (var m in _months) MonthChoices.Add(Name(m, now));
            if (keep == null || !MonthChoices.Contains(keep)) keep = MonthChoices.Count > 1 ? MonthChoices[1] : MonthChoices.FirstOrDefault();
            _month = keep;
            _loading = false;
            OnPropertyChanged(nameof(Month));
            Load();
        }

        /// <summary>Shows the report of a month (from the report news).</summary>
        public void ShowMonth(int year, int month)
        {
            Reload();
            int i = _months.FindIndex(m => m.Item1 == year && m.Item2 == month);
            if (i >= 0 && MonthChoices[i] != _month)
            {
                _month = MonthChoices[i];
                OnPropertyChanged(nameof(Month));
                Load();
            }
        }

        private static string M(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static string Signed(double v)
        {
            return (v >= 0 ? "+" : "-") + M(Math.Abs(v));
        }

        private static string MonthShort(int month)
        {
            return CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);
        }

        // a change on the month before: an amount, or points for a share
        private static ReportTile Tile(string label, string value, double now, double before, bool hasBefore, string beforeMonth, bool soFar, Func<double, string> show, bool share = false, bool lowerIsBetter = false)
        {
            var t = new ReportTile {Label = label, Value = value};
            if (!hasBefore) return t;
            if (soFar || share && before <= 0)
            {
                t.Change = beforeMonth + ": " + show(before);
                return t;
            }
            double diff = now - before;
            if (share) t.Change = string.Format("{0:+0.0;-0.0;0.0} pts on {1}", diff*100, beforeMonth);
            else if (Math.Abs(before) >= 1) t.Change = string.Format("{0:+0;-0;0}% on {1}", diff/Math.Abs(before)*100, beforeMonth);
            else t.Change = beforeMonth + ": " + show(before);
            if (Math.Abs(diff) > 1e-9 && (share || Math.Abs(before) >= 1)) t.Trend = (diff > 0) != lowerIsBetter ? 1 : -1;
            return t;
        }

        private void Load()
        {
            var game = GameObject.GetInstance();
            int i = MonthChoices.IndexOf(_month);
            Tiles.Clear();
            Money.Clear();
            Best.Clear();
            Worst.Clear();
            Rivals.Clear();
            Attention.Clear();
            if (game?.HumanAirline == null || i < 0)
            {
                Report = null;
                Summary = "No report yet.";
                Notify();
                return;
            }
            AirlineReport r = AirlineReportService.Build(game.HumanAirline, _months[i].Item1, _months[i].Item2);
            Report = r;
            string pm = MonthShort(r.PreviousOps.Month);
            PreviousHeader = r.HasPrevious ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(r.PreviousOps.Month) : "";

            Summary = r.SoFar
                ? string.Format("{0}, {1} of {2} days: operating result {3}{4}. Monthly charges such as wages and leases are paid early in the month.",
                                r.Title, r.DaysCounted, r.DaysInMonth, M(r.Result), r.HasPrevious ? string.Format(" ({0} for all of {1})", M(r.PreviousResult), PreviousHeader) : "")
                : string.Format("{0}: you {1} {2} on operations{3}.", r.Title, r.Result >= 0 ? "earned" : "lost", M(Math.Abs(r.Result)),
                                r.HasPrevious ? string.Format(", against {0} in {1}", M(r.PreviousResult), PreviousHeader) : "");

            MonthOps o = r.Ops, p = r.PreviousOps;
            Tiles.Add(Tile("Operating result", M(r.Result), r.Result, r.PreviousResult, r.HasPrevious, pm, r.SoFar, M));
            Tiles.Add(Tile("Income", M(r.Income), r.Income, r.PreviousIncome, r.HasPrevious, pm, r.SoFar, M));
            Tiles.Add(Tile("Costs", M(-r.Costs), -r.Costs, -r.PreviousCosts, r.HasPrevious, pm, r.SoFar, v => M(v), false, true));
            Tiles.Add(new ReportTile {Label = r.SoFar ? "Cash now" : "Cash at the month's end", Value = r.SoFar || o.Closed ? M(r.Cash) : "not known", Change = p.Closed ? pm + " end: " + M(r.PreviousCash) : null});
            Tiles.Add(Tile("Flights", o.Flights.ToString("#,0"), o.Flights, p.Flights, r.HasPrevious, pm, r.SoFar, v => v.ToString("#,0")));
            if (o.Seats > 0 || p.Seats > 0)
            {
                Tiles.Add(Tile("Passengers", o.Passengers.ToString("#,0"), o.Passengers, p.Passengers, r.HasPrevious, pm, r.SoFar, v => v.ToString("#,0")));
                Tiles.Add(Tile("Seats filled", o.Seats > 0 ? (o.Load*100).ToString("0") + "%" : "-", o.Load, p.Load, r.HasPrevious && p.Seats > 0, pm, false, v => (v*100).ToString("0") + "%", true));
            }
            if (o.CargoCapacity > 0 || p.CargoCapacity > 0)
            {
                Tiles.Add(Tile("Cargo", o.Cargo.ToString("#,0") + " t", o.Cargo, p.Cargo, r.HasPrevious, pm, r.SoFar, v => v.ToString("#,0") + " t"));
                Tiles.Add(Tile("Cargo space filled", o.CargoCapacity > 0 ? (o.CargoLoad*100).ToString("0") + "%" : "-", o.CargoLoad, p.CargoLoad, r.HasPrevious && p.CargoCapacity > 0, pm, false, v => (v*100).ToString("0") + "%", true));
            }
            Tiles.Add(Tile("On time", o.Flights > 0 ? (o.OnTimeShare*100).ToString("0") + "%" : "-", o.OnTimeShare, p.OnTimeShare, r.HasPrevious && p.Flights > 0, pm, false, v => (v*100).ToString("0") + "%", true));
            Tiles.Add(new ReportTile
            {
                Label = "Fleet", Value = r.SoFar || o.Closed ? o.Fleet + (o.Idle > 0 ? " (" + o.Idle + " idle)" : "") : "not known",
                Change = o.Cancelled > 0 ? o.Cancelled + " flights cancelled" : null
            });

            foreach (ReportMoneyLine m in r.Money)
                Money.Add(new ReportMoneyRow {Name = m.Name, Amount = M(m.Amount), Previous = r.HasPrevious ? M(m.Previous) : "", IsIncome = m.Amount > 0});
            Money.Add(new ReportMoneyRow {Name = "Operating result", Amount = M(r.Result), Previous = r.HasPrevious ? M(r.PreviousResult) : "", IsIncome = r.Result >= 0});
            CapitalLine = r.Capital.Count == 0 ? "" : "Not in the result: " + string.Join(", ", r.Capital.Select(c => c.Name.ToLowerInvariant() + " " + Signed(c.Amount))) + ".";
            CashLine = r.SoFar || o.Closed
                ? string.Format("{0} {1}{2}.", r.SoFar ? "Cash now:" : "Cash at the month's end:", M(r.Cash), p.Closed ? string.Format(" ({0} on the month before)", Signed(r.Cash - r.PreviousCash)) : "")
                : "";

            foreach (ReportRoute x in r.Best) Best.Add(RouteRow(x));
            foreach (ReportRoute x in r.Worst) Worst.Add(RouteRow(x));

            foreach (ReportRival x in r.Rivals)
            {
                ReportRival rv = x;
                Rivals.Add(new ReportLinkRow
                {
                    Title = x.Airline.Profile.Name,
                    Text = string.Format("{0}, {1} result {2}{3}{4}",
                                         x.SharedRoutes == 0 ? "at " + x.SharedAirports + (x.SharedAirports == 1 ? " of your airports" : " of your airports") : x.SharedRoutes + (x.SharedRoutes == 1 ? " shared route" : " shared routes"),
                                         r.SoFar ? "so far" : "month", M(x.Result),
                                         x.Load > 0 ? string.Format(", {0:0}% full", x.Load*100) : "",
                                         x.FareAgainstMine <= 0 ? "" : Math.Abs(1 - x.FareAgainstMine) < 0.005 ? ", the same fares as yours"
                                             : string.Format(", fares {0:0}% {1} yours", Math.Abs(1 - x.FareAgainstMine)*100, x.FareAgainstMine < 1 ? "below" : "above")),
                    IsWarning = x.FareAgainstMine > 0 && x.FareAgainstMine < 0.97,
                    LinkText = "Competitors",
                    Command = new RelayCommand(() => _open?.Invoke(new ReportItem {Kind = ReportItemKind.Competitors, Airline = rv.Airline}))
                });
            }

            foreach (ReportItem item in r.Attention)
            {
                ReportItem it = item;
                Attention.Add(new ReportLinkRow
                {
                    Text = item.Text, IsWarning = true,
                    LinkText = item.Kind == ReportItemKind.Route ? "Open the route" : item.Kind == ReportItemKind.Competitors ? "Show in Competitors" : item.Kind == ReportItemKind.Fleet ? "Fleet" : item.Kind == ReportItemKind.IdleAircraft ? "Suggest routes" : item.Kind == ReportItemKind.Finances ? "Finances" : null,
                    Command = item.Kind == ReportItemKind.None ? null : new RelayCommand(() => _open?.Invoke(it))
                });
            }
            Notify();
        }

        private ReportLinkRow RouteRow(ReportRoute x)
        {
            ReportRoute rt = x;
            return new ReportLinkRow
            {
                Title = x.Name + (x.IsCargo ? " (cargo)" : "") + "  " + Signed(x.Profit),
                Text = x.Flights == 0
                    ? "no flights recorded"
                    : string.Format("{0} flights, {1}, {2:0}% full", x.Flights, x.IsCargo ? x.Carried.ToString("#,0") + " t" : x.Carried.ToString("#,0") + " passengers", x.Load*100),
                IsWarning = x.Profit < 0,
                LinkText = "Open",
                Command = new RelayCommand(() => _open?.Invoke(new ReportItem {Kind = ReportItemKind.Route, RouteId = rt.RouteId}))
            };
        }

        private void Notify()
        {
            OnPropertyChanged(nameof(Report));
            OnPropertyChanged(nameof(PreviousHeader));
            OnPropertyChanged(nameof(HasCapital));
            OnPropertyChanged(nameof(HasBest));
            OnPropertyChanged(nameof(HasWorst));
            OnPropertyChanged(nameof(HasRivals));
            OnPropertyChanged(nameof(NothingToDo));
        }
    }
}
