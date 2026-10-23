using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class ConnectionRouteRow
    {
        public Route Route { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
    }

    public class ConnectionRow
    {
        public ConnectionMarket Market { get; set; }
        public string Journey { get; set; }
        public string Details { get; set; }
        public bool Counted { get; set; }
        public double Opacity => Counted ? 1.0 : 0.55;
    }

    public class HubIdeaRow
    {
        public Airport Destination { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
        public ICommand PlanCommand { get; set; }
    }

    /// <summary>Routes > Connections: which routes feed each other, how many passengers connect, and where to fly next from a hub.</summary>
    public class ConnectionsViewModel : ObservableObject
    {
        private readonly Action<Airport, Airport> _planRoute;
        private ConnectionRouteRow _selected;
        private AirportItem _hub;
        private string _summary, _routeSummary, _ideasSummary, _maxDistance = "5000";

        public ConnectionsViewModel(Action<Airport, Airport> planRoute)
        {
            _planRoute = planRoute;
            Rows = new ObservableCollection<ConnectionRouteRow>();
            Markets = new ObservableCollection<ConnectionRow>();
            Hubs = new ObservableCollection<AirportItem>();
            Ideas = new ObservableCollection<HubIdeaRow>();
        }

        public ObservableCollection<ConnectionRouteRow> Rows { get; }
        public ObservableCollection<ConnectionRow> Markets { get; }
        public ObservableCollection<AirportItem> Hubs { get; }
        public ObservableCollection<HubIdeaRow> Ideas { get; }

        public string Explanation =>
            "Passengers also fly with you when they change planes. A flight from A to B carries people going on from B on " +
            "another of your routes (or an alliance or codeshare partner's), and people who came to A from somewhere else. " +
            "A connection counts when the trip is less than three times the nonstop distance and fewer than two routes fly " +
            "the nonstop. Your own routes bring a quarter of that market, more where the airport is your hub (30 to 40%); a " +
            "partner's bring a fifth. Demand numbers are both ways and all classes, on the scale of Airports > Demand.";

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string RouteSummary { get => _routeSummary; private set => SetProperty(ref _routeSummary, value); }
        public string IdeasSummary { get => _ideasSummary; private set => SetProperty(ref _ideasSummary, value); }

        public ConnectionRouteRow Selected
        {
            get => _selected;
            set { if (SetProperty(ref _selected, value)) LoadMarkets(); }
        }

        public AirportItem Hub
        {
            get => _hub;
            set { if (SetProperty(ref _hub, value)) LoadIdeas(); }
        }

        /// <summary>The longest new route to suggest, in km.</summary>
        public string MaxDistance
        {
            get => _maxDistance;
            set { if (SetProperty(ref _maxDistance, value ?? "")) LoadIdeas(); }
        }

        private static string N(double v)
        {
            return v < 10 && v > 0 ? v.ToString("0.#") : v.ToString("N0");
        }

        private static string Code(Airport a)
        {
            return ConnectionService.Code(a);
        }

        public void Reload()
        {
            var me = GameObject.GetInstance()?.HumanAirline;
            if (me == null) return;
            ConnectionService.Invalidate();
            var keep = _selected?.Route;
            Rows.Clear();
            int allConn = 0, allPax = 0;
            foreach (var r in me.Routes.ToList().Where(r => !r.IsCargoRoute)
                                .OrderBy(r => Code(r.Destination1)).ThenBy(r => Code(r.Destination2)))
            {
                int conn, pax, conn2, pax2;
                ConnectionService.LastMonth(r, out conn, out pax);
                ConnectionService.ThisMonth(r, out conn2, out pax2);
                allConn += conn;
                allPax += pax;
                string numbers = pax > 0
                    ? string.Format("last month {0:N0} of {1:N0} passengers connected ({2:0}%)", conn, pax, 100.0*conn/pax)
                    : pax2 > 0
                        ? string.Format("this month so far {0:N0} of {1:N0} passengers connected ({2:0}%)", conn2, pax2, 100.0*conn2/pax2)
                        : r.HasAirliner ? "no flights counted yet" : "no aircraft on it";
                Rows.Add(new ConnectionRouteRow { Route = r, Name = Code(r.Destination1) + " - " + Code(r.Destination2), Numbers = numbers });
            }
            Summary = Rows.Count == 0
                ? "You have no passenger routes yet."
                : allPax > 0
                    ? string.Format("Last month {0:N0} of your {1:N0} passengers ({2:0}%) were changing planes.", allConn, allPax, 100.0*allConn/allPax)
                    : "Connecting passengers are counted from this month on; last month's numbers show here next month.";
            _selected = null;
            Selected = Rows.FirstOrDefault(x => x.Route == keep) ?? Rows.FirstOrDefault();

            var hub = _hub?.Airport;
            Hubs.Clear();
            foreach (var a in me.Airports.ToList()
                                .OrderByDescending(a => me.Routes.Count(r => r.Destination1 == a || r.Destination2 == a))
                                .ThenBy(a => a.Profile.Name))
                Hubs.Add(new AirportItem(a, true));
            _hub = null;
            Hub = Hubs.FirstOrDefault(h => h.Airport == hub) ?? Hubs.FirstOrDefault();
            if (Hub == null)
            {
                Ideas.Clear();
                IdeasSummary = null;
            }
        }

        private void LoadMarkets()
        {
            Markets.Clear();
            var r = _selected?.Route;
            if (r == null)
            {
                RouteSummary = null;
                return;
            }
            var list = ConnectionService.Markets(r);
            foreach (var m in list)
            {
                string feeder = Code(m.Feeder.Destination1) + "-" + Code(m.Feeder.Destination2);
                string on = m.Partner ? m.Feeder.Airline.Profile.Name + "'s " + feeder : "your " + feeder;
                Markets.Add(new ConnectionRow
                {
                    Market = m,
                    Counted = m.Counted,
                    Journey = Code(m.Far) + " - " + Code(m.Via) + " - " + Code(m.Near) + "   (" + m.Far.Profile.Town?.Name + " to " + m.Near.Profile.Town?.Name + ")",
                    Details = m.Counted
                        ? string.Format("Demand {1}-{2}: {0}; about {3} of it connects at {4} ({5:0}%{6}) on {7}",
                                        N(m.Demand), Code(m.Far), Code(m.Near), N(m.Added), Code(m.Via), m.Weight*100,
                                        m.HubName != null ? ", your " + m.HubName : "", on)
                        : "Doesn't count: " + m.Problem + ". (" + on + ")"
                });
            }
            double nonstop = ConnectionService.PairDemand(r.Destination1, r.Destination2);
            double added = list.Sum(m => m.Added);
            int counted = list.Count(m => m.Counted);
            RouteSummary = list.Count == 0
                ? string.Format("No other route of yours or your partners' starts at {0} or {1}, so nobody connects to {0}-{1}. Demand for {0}-{1} itself: {2}.",
                                Code(r.Destination1), Code(r.Destination2), N(nonstop))
                : string.Format("Demand for {1}-{2} itself: {0}. Connections add about {3} more ({4} of {5} connections count).",
                                N(nonstop), Code(r.Destination1), Code(r.Destination2), N(added), counted, list.Count);
        }

        private void LoadIdeas()
        {
            Ideas.Clear();
            var me = GameObject.GetInstance()?.HumanAirline;
            var hub = _hub?.Airport;
            if (me == null || hub == null)
            {
                IdeasSummary = null;
                return;
            }
            double max;
            if (!double.TryParse(_maxDistance.Replace(",", ""), out max) || max <= 0) max = 5000;
            string hubName;
            double w = ConnectionService.OwnWeightAt(me, hub, out hubName);
            int spokes = me.Routes.ToList().Count(r => !r.IsCargoRoute && r.HasAirliner && (r.Destination1 == hub || r.Destination2 == hub));
            foreach (var i in ConnectionService.HubIdeas(me, hub, 15, max))
            {
                var dest = i.Destination;
                Ideas.Add(new HubIdeaRow
                {
                    Destination = dest,
                    Name = Code(dest) + "  " + dest.Profile.Name + ", " + dest.Profile.Country?.Name,
                    Numbers = string.Format("{0:N0} km  |  demand {1} nonstop{2}  |  {3} in all",
                                            MathHelpers.GetDistance(hub, dest), N(i.Nonstop),
                                            i.Feeders > 0 ? " + " + N(i.Connecting) + " connecting from " + i.Feeders + " of your routes" : "",
                                            N(i.Total)),
                    PlanCommand = new RelayCommand(() => _planRoute?.Invoke(hub, dest))
                });
            }
            IdeasSummary = string.Format(
                "New destinations from {0}, by the demand a route there would get. {1} from {0}; connections there bring {2:0}% of their market{3}.",
                Code(hub), spokes == 1 ? "1 of your routes flies" : spokes + " of your routes fly", w*100,
                hubName != null ? " (it is your " + hubName + ")" : ". Making it a hub under Airports > Manage raises that to 30-40%");
        }
    }
}
