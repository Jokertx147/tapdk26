using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class RouteProfitRow
    {
        public RouteFigures Figures { get; set; }
        public string Name { get; set; }
        public string Profit { get; set; }
        public string Numbers { get; set; }
        public string Advice { get; set; }
        public bool IsLoss => Figures.Losing;
    }

    /// <summary>Routes > Route profits: what each route earns, sorted and filtered, with a hint for each.</summary>
    public class RouteProfitViewModel : ObservableObject
    {
        private readonly Action<Route> _open;
        private RouteProfitRow _selected;
        private string _summary, _sortBy = "Profit, last 12 months", _show = "All routes";

        public RouteProfitViewModel(Action<Route> open)
        {
            _open = open;
            OpenCommand = new RelayCommand(() => { if (_selected != null) _open?.Invoke(_selected.Figures.Route); });
        }

        public ObservableCollection<RouteProfitRow> Rows { get; } = new ObservableCollection<RouteProfitRow>();
        public ObservableCollection<string> Months { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Breakdown { get; } = new ObservableCollection<string>();
        public ICommand OpenCommand { get; }

        public List<string> SortChoices { get; } = new List<string>
        {
            "Profit, last 12 months", "Profit, last month", "Loss first", "Profit per flight", "Load factor", "Fare against the market", "Name"
        };

        public List<string> ShowChoices { get; } = new List<string> {"All routes", "Losing money", "Passenger routes", "Cargo routes", "No aircraft"};

        public string SortBy
        {
            get => _sortBy;
            set
            {
                if (SetProperty(ref _sortBy, value)) Reload();
            }
        }

        public string Show
        {
            get => _show;
            set
            {
                if (SetProperty(ref _show, value)) Reload();
            }
        }

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string Detail { get; private set; }
        public bool HasSelection => _selected != null;

        public RouteProfitRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    LoadDetail();
                }
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static string Code(Route r)
        {
            return r.Destination1.Profile.IATACode + " - " + r.Destination2.Profile.IATACode;
        }

        public void Reload()
        {
            var airline = GameObject.GetInstance()?.HumanAirline;
            if (airline == null) return;

            List<RouteFigures> all = RouteProfitService.All(airline);
            IEnumerable<RouteFigures> shown = all;
            switch (_show)
            {
                case "Losing money": shown = all.Where(f => f.Losing); break;
                case "Passenger routes": shown = all.Where(f => !f.IsCargo); break;
                case "Cargo routes": shown = all.Where(f => f.IsCargo); break;
                case "No aircraft": shown = all.Where(f => f.Route.GetAirliners().Count == 0); break;
            }

            switch (_sortBy)
            {
                case "Profit, last month": shown = shown.OrderByDescending(f => f.LastMonth); break;
                case "Loss first": shown = shown.OrderBy(f => f.Months > 0 ? f.Last12 : f.ThisMonth); break;
                case "Profit per flight": shown = shown.OrderByDescending(f => f.ProfitPerFlight); break;
                case "Load factor": shown = shown.OrderByDescending(f => f.Load); break;
                case "Fare against the market": shown = shown.OrderByDescending(f => f.FareRatio); break;
                case "Name": shown = shown.OrderBy(f => Code(f.Route)); break;
                default: shown = shown.OrderByDescending(f => f.Last12 + (f.Months == 0 ? f.ThisMonth : 0)); break;
            }

            var keep = _selected?.Figures.Route;
            Rows.Clear();
            foreach (RouteFigures f in shown)
            {
                string fare = f.FareRatio > 0 ? string.Format("{0} {1:0}% of the market", f.IsCargo ? "rate" : "fare", f.FareRatio*100) : "";
                Rows.Add(new RouteProfitRow
                {
                    Figures = f,
                    Name = Code(f.Route) + (f.IsCargo ? "  (cargo)" : ""),
                    Profit = f.Months > 0 ? Money(f.Last12) + " in " + (f.Months == 12 ? "12 months" : f.Months + (f.Months == 1 ? " month" : " months")) : Money(f.ThisMonth) + " this month",
                    Numbers = string.Join("  |  ", new[]
                    {
                        "last month " + Money(f.LastMonth),
                        f.Load > 0 ? string.Format("{0:0}% full", f.Load*100) : "no load yet",
                        f.WeeklyFlights + " flights a week",
                        f.ProfitPerFlight != 0 ? Money(f.ProfitPerFlight) + " a flight" : null,
                        fare.Length > 0 ? fare : null,
                        f.Aircraft
                    }.Where(x => x != null)),
                    Advice = f.Advice
                });
            }
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Figures.Route == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));

            if (all.Count == 0)
            {
                Summary = "You have no routes yet.";
                return;
            }
            double total = all.Sum(f => f.Last12);
            double lastMonth = all.Sum(f => f.LastMonth);
            int losing = all.Count(f => f.Losing);
            var best = all.OrderByDescending(f => f.Last12).First();
            var worst = all.OrderBy(f => f.Last12).First();
            Summary = string.Format("{0} routes  |  {1} in the last 12 months, {2} last month  |  {3} losing money  |  best {4} ({5}), worst {6} ({7})",
                all.Count, Money(total), Money(lastMonth), losing, Code(best.Route), Money(best.Last12), Code(worst.Route), Money(worst.Last12));
        }

        private void LoadDetail()
        {
            Months.Clear();
            Breakdown.Clear();
            Detail = null;
            if (_selected != null)
            {
                Route r = _selected.Figures.Route;
                DateTime now = GameObject.GetInstance().GameTime;
                Detail = Code(r) + ": " + _selected.Advice;
                for (int i = 0; i <= 12; i++)
                {
                    DateTime d = now.AddMonths(-i);
                    if (r.StartDate != default(DateTime) && new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month)) < r.StartDate) break;
                    double income, costs;
                    RouteProfitService.IncomeAndCosts(r, d.Year, d.Month, out income, out costs);
                    Months.Add(d.ToString("MMM yyyy") + (i == 0 ? " (so far)" : "") + ":  income " + Money(income) + ",  costs " + Money(-costs) + ",  profit " + Money(income + costs));
                }
                DateTime last = now.AddMonths(-1);
                var parts = RouteProfitService.Breakdown(r, last.Year, last.Month);
                if (parts.Count == 0)
                {
                    parts = RouteProfitService.Breakdown(r, now.Year, now.Month);
                    if (parts.Count > 0) Breakdown.Add("This month so far:");
                }
                else
                {
                    Breakdown.Add(last.ToString("MMMM yyyy") + ":");
                }
                foreach (var p in parts) Breakdown.Add("  " + p.Item1 + ": " + (p.Item2 > 0 ? "+" : "") + Money(p.Item2));
            }
            OnPropertyChanged(nameof(Detail));
        }
    }
}
