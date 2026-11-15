using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TheAirline.Helpers;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    /// <summary>One bar: what something earned, and enough about it to know why.</summary>
    public class RankBar
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public string Amount { get; set; }
        public string Detail { get; set; }
        public bool IsLoss => Value < 0;

        /// <summary>The route this bar stands for, so clicking it can open the route; null for an aircraft.</summary>
        public Route Route { get; set; }
    }

    /// <summary>
    ///     My Airline > Best and worst: the routes, or the aircraft, that made the most and lost the most, drawn as
    ///     bars either side of nothing. Routes are judged on their own income and costs; an aircraft is judged on its
    ///     share of the profit of the routes it flies, less what it costs to own or lease (see AircraftProfitService).
    /// </summary>
    public class ProfitRankingViewModel : ObservableObject
    {
        public const string Routes = "Routes";
        public const string Aircraft = "Aircraft";
        public const string LastMonth = "Last month";
        public const string ThisMonth = "This month so far";
        public const string Last12 = "Last 12 months, a month on average";

        private readonly Action<Route> _open;
        private string _what = Routes;
        private string _period = LastMonth;
        private int _howMany = 8;
        private string _summary = "";
        private RankBar _selected;

        public ProfitRankingViewModel(Action<Route> open = null)
        {
            _open = open;
            OpenCommand = new RelayCommand(
                () => { if (_selected?.Route != null) _open?.Invoke(_selected.Route); },
                () => _selected?.Route != null);
        }

        public ObservableCollection<RankBar> Best { get; } = new ObservableCollection<RankBar>();
        public ObservableCollection<RankBar> Worst { get; } = new ObservableCollection<RankBar>();
        public System.Windows.Input.ICommand OpenCommand { get; }

        public List<string> WhatChoices { get; } = new List<string> { Routes, Aircraft };
        public List<string> PeriodChoices { get; } = new List<string> { LastMonth, ThisMonth, Last12 };
        public List<int> HowManyChoices { get; } = new List<int> { 5, 8, 12, 20 };

        public string What
        {
            get { return _what; }
            set { if (SetProperty(ref _what, value)) Reload(); }
        }

        public string Period
        {
            get { return _period; }
            set { if (SetProperty(ref _period, value)) Reload(); }
        }

        /// <summary>How many to show at each end.</summary>
        public int HowMany
        {
            get { return _howMany; }
            set { if (SetProperty(ref _howMany, value)) Reload(); }
        }

        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        public RankBar Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value)) (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The largest amount on the chart either way, so the bars can be drawn to scale.</summary>
        public double Largest => Best.Concat(Worst).Select(b => Math.Abs(b.Value)).DefaultIfEmpty(0).Max();

        public bool IsEmpty => Best.Count == 0 && Worst.Count == 0;

        /// <summary>What the amounts mean, for the chart's own label.</summary>
        public string AmountHeading => _period == Last12 ? "a month on average" : "in the month";

        public void Reload()
        {
            Best.Clear();
            Worst.Clear();
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null)
            {
                Summary = "No airline yet.";
                Raise();
                return;
            }

            List<RankBar> bars = _what == Aircraft ? AircraftBars(airline) : RouteBars(airline);
            var ordered = bars.OrderByDescending(b => b.Value).ToList();
            if (ordered.Count == 0)
            {
                Summary = _what == Aircraft
                    ? "No aircraft delivered yet."
                    : "No routes yet. Open one under Routes, and this will show which earn and which lose.";
                Raise();
                return;
            }

            // the best and the worst, and never the same one in both lists
            foreach (var b in ordered.Take(Math.Min(_howMany, ordered.Count))) Best.Add(b);
            foreach (var b in ordered.AsEnumerable().Reverse().Take(_howMany).Where(b => !Best.Contains(b)).Reverse()) Worst.Add(b);

            int losing = ordered.Count(b => b.Value < 0);
            double total = ordered.Sum(b => b.Value);
            string shown = Worst.Count == 0
                ? "All of them are shown, the best at the top."
                : "The " + Best.Count + " best and the " + Worst.Count + " worst are shown.";
            Summary = string.Format(
                "{0} {1}, {2} losing money. Together {3} {4}. {5}",
                ordered.Count, _what == Aircraft ? "aircraft" : "routes", losing, Money(total), AmountHeading, shown);
            Raise();
        }

        private void Raise()
        {
            OnPropertyChanged(nameof(Largest));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AmountHeading));
        }

        private List<RankBar> RouteBars(Models.Airlines.Airline airline)
        {
            var bars = new List<RankBar>();
            foreach (RouteFigures r in RouteProfitService.All(airline))
            {
                double value = _period == ThisMonth ? r.ThisMonth
                    : _period == Last12 ? (r.Months > 0 ? r.Last12/r.Months : 0)
                    : r.LastMonth;
                bars.Add(new RankBar
                {
                    Name = r.Route.Destination1.Profile.IATACode + "-" + r.Route.Destination2.Profile.IATACode,
                    Value = value,
                    Amount = Money(value),
                    Route = r.Route,
                    Detail = string.Format(
                        "{0} {1}  |  {2:0}% full  |  {3} flights a week{4}",
                        Money(value), AmountHeading, r.Load*100, r.WeeklyFlights,
                        string.IsNullOrEmpty(r.Aircraft) ? "" : "  |  " + r.Aircraft)
                });
            }

            return bars;
        }

        private List<RankBar> AircraftBars(Models.Airlines.Airline airline)
        {
            DateTime when = GameObject.GetInstance().GameTime;
            var bars = new List<RankBar>();

            List<AircraftFigures> figures;
            if (_period == Last12)
            {
                // the twelve months before this one, averaged over the months that have figures
                var sums = new Dictionary<Models.Airliners.FleetAirliner, AircraftFigures>();
                var months = new Dictionary<Models.Airliners.FleetAirliner, int>();
                for (int back = 1; back <= 12; back++)
                {
                    DateTime m = when.AddMonths(-back);
                    foreach (var fig in AircraftProfitService.All(airline, m.Year, m.Month))
                    {
                        if (Math.Abs(fig.RouteShare) < 0.5 && fig.WeeklyFlights == 0) continue;
                        AircraftFigures sum;
                        if (!sums.TryGetValue(fig.Airliner, out sum))
                        {
                            sums[fig.Airliner] = fig;
                            months[fig.Airliner] = 1;
                            continue;
                        }

                        sum.RouteShare += fig.RouteShare;
                        sum.Ownership += fig.Ownership;
                        months[fig.Airliner] = months[fig.Airliner] + 1;
                    }
                }

                figures = sums.Values.ToList();
                foreach (var fig in figures)
                {
                    int n = Math.Max(1, months[fig.Airliner]);
                    fig.RouteShare /= n;
                    fig.Ownership /= n;
                }

                // an aircraft that never flew in the year still costs what it costs
                foreach (var fig in AircraftProfitService.All(airline, when.Year, when.Month))
                {
                    if (!sums.ContainsKey(fig.Airliner)) figures.Add(fig);
                }
            }
            else
            {
                DateTime m = _period == ThisMonth ? when : when.AddMonths(-1);
                figures = AircraftProfitService.All(airline, m.Year, m.Month);
            }

            foreach (AircraftFigures a in figures)
            {
                bars.Add(new RankBar
                {
                    Name = a.Name,
                    Value = a.Profit,
                    Amount = Money(a.Profit),
                    Detail = string.Format(
                        "{0} {1}  |  {2}  |  {3}  |  {4} flights a week, {5:0}% full  |  its share of its routes {6}, it costs {7}",
                        Money(a.Profit), AmountHeading, a.TypeName,
                        a.Idle ? "no flights" : a.Routes, a.WeeklyFlights, a.Load*100,
                        Money(a.RouteShare), Money(a.Ownership))
                });
            }

            return bars;
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }
    }
}
