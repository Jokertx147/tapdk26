using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     One connection market of a route: passengers between Far and Near who change planes at Via, flying the
    ///     route between Near and Via and the feeder route between Via and Far.
    /// </summary>
    public class ConnectionMarket
    {
        public Route Route, Feeder;
        public Airport Near, Via, Far;
        public bool Partner;
        public string HubName;

        /// <summary>Passengers a day wanting to travel between Near and Far (both ways, all classes).</summary>
        public double Demand;

        /// <summary>The share of that demand that books the connection when it counts.</summary>
        public double Weight;

        /// <summary>Why the market brings no passengers, or null.</summary>
        public string Problem;

        public bool Counted => Problem == null;
        public double Added => Counted ? Demand*Weight : 0;
    }

    /// <summary>
    ///     Connecting passengers. A flight from A to B also carries passengers going on from B (or coming from
    ///     beyond A) on another route of the airline or of its alliance and codeshare partners. A connection counts
    ///     when the trip is less than three times the nonstop distance and fewer than two airlines fly the nonstop.
    ///     The airline's own feeder routes bring a quarter of the market, more through its hubs (30% at a focus
    ///     city, 33% at a regional hub, 35% at a hub, 40% at a fortress hub); a partner's routes bring a fifth.
    ///     The routes count connecting passengers each month.
    /// </summary>
    public static class ConnectionService
    {
        public const double OwnWeight = 0.25, PartnerWeight = 0.2;

        // ---- how many airlines fly each airport pair nonstop, built once a day ----
        private static readonly object CacheLock = new object();
        private static Dictionary<Airport, Dictionary<Airport, int>> _nonstop;
        private static DateTime _nonstopDate;

        public static void Invalidate()
        {
            lock (CacheLock) _nonstop = null;
        }

        private static Dictionary<Airport, Dictionary<Airport, int>> Nonstop()
        {
            var day = GameObject.GetInstance()?.GameTime.Date ?? DateTime.MinValue;
            var cache = _nonstop;
            if (cache != null && _nonstopDate == day) return cache;
            lock (CacheLock)
            {
                if (_nonstop != null && _nonstopDate == day) return _nonstop;
                var map = new Dictionary<Airport, Dictionary<Airport, int>>();
                int count = 0;
                foreach (var airline in Airlines.GetAllAirlines().ToList())
                {
                    foreach (var r in airline.Routes.ToList())
                    {
                        if (r?.Destination1 == null || r.Destination2 == null) continue;
                        Add(map, r.Destination1, r.Destination2);
                        Add(map, r.Destination2, r.Destination1);
                        count++;
                    }
                }
                _nonstop = map;
                _nonstopDate = day;
                return map;
            }
        }

        private static void Add(Dictionary<Airport, Dictionary<Airport, int>> map, Airport a, Airport b)
        {
            Dictionary<Airport, int> inner;
            if (!map.TryGetValue(a, out inner)) map[a] = inner = new Dictionary<Airport, int>();
            int n;
            inner.TryGetValue(b, out n);
            inner[b] = n + 1;
        }

        /// <summary>How many routes (of all airlines) fly between two airports.</summary>
        public static int NonstopRoutes(Airport a, Airport b)
        {
            Dictionary<Airport, int> inner;
            int n;
            return Nonstop().TryGetValue(a, out inner) && inner.TryGetValue(b, out n) ? n : 0;
        }

        // ---- the airline's network ----

        /// <summary>The partners whose routes can feed the airline's: alliance members and codeshare partners.</summary>
        public static List<Airline> Partners(Airline airline)
        {
            var list = new List<Airline>();
            foreach (var al in airline.Alliances.ToList())
                foreach (var m in al.Members.ToList())
                    if (m.Airline != airline && !list.Contains(m.Airline)) list.Add(m.Airline);
            foreach (var c in airline.Codeshares.ToList())
            {
                if (!(c.Airline2 == airline && c.Type == CodeshareAgreement.CodeshareType.OneWay) && c.Type != CodeshareAgreement.CodeshareType.BothWays) continue;
                var other = c.Airline1 == airline ? c.Airline2 : c.Airline1;
                if (other != airline && !list.Contains(other)) list.Add(other);
            }
            return list;
        }

        /// <summary>The share of a market the airline's own feeder brings through an airport (more at its hubs).</summary>
        public static double OwnWeightAt(Airline airline, Airport via, out string hubName)
        {
            hubName = null;
            var hub = via.GetHubs().Find(h => h.Airline == airline);
            if (hub == null) return OwnWeight;
            switch (hub.Type.Type)
            {
                case HubType.TypeOfHub.FocusCity: hubName = "focus city"; return 0.30;
                case HubType.TypeOfHub.RegionalHub: hubName = "regional hub"; return 0.33;
                case HubType.TypeOfHub.FortressHub: hubName = "fortress hub"; return 0.40;
                default: hubName = "hub"; return 0.35;
            }
        }

        /// <summary>The demand between two airports, both ways and all classes.</summary>
        public static double PairDemand(Airport a, Airport b)
        {
            return Rate(a, b, null) + Rate(b, a, null);
        }

        private static double Rate(Airport from, Airport to, AirlinerClass.ClassType? type)
        {
            if (type.HasValue) return from.GetDestinationPassengersRate(to, type.Value);
            double v = 0;
            foreach (AirlinerClass.ClassType t in Enum.GetValues(typeof (AirlinerClass.ClassType)))
                v += from.GetDestinationPassengersRate(to, t);
            return v;
        }

        /// <summary>
        ///     The connection markets at one end (via) of a leg between near and via. With a class, the demand is the
        ///     one-way daily demand from near (or to near when inbound) in that class, as the flight uses it; without one,
        ///     both ways in all classes.
        /// </summary>
        private static IEnumerable<ConnectionMarket> MarketsAt(Airline airline, Route route, Airport near, Airport via, AirlinerClass.ClassType? type, bool outbound)
        {
            double leg = MathHelpers.GetDistance(near, via);
            string hubName;
            double own = OwnWeightAt(airline, via, out hubName);
            var feeders = airline.Routes.ToList().Select(r => new { r, partner = false })
                                 .Concat(Partners(airline).SelectMany(p => p.Routes.ToList()).Select(r => new { r, partner = true }));
            var seen = new HashSet<Airport>();
            foreach (var f in feeders)
            {
                var r = f.r;
                if (r == null || r == route || r.IsCargoRoute) continue;
                if (r.Destination1 != via && r.Destination2 != via) continue;
                if (r.Destination1 == near || r.Destination2 == near) continue;
                var far = r.Destination1 == via ? r.Destination2 : r.Destination1;
                // one market per far airport: the own route first, then partners
                if (!seen.Add(far)) continue;
                var m = new ConnectionMarket
                {
                    Route = route, Feeder = r, Near = near, Via = via, Far = far, Partner = f.partner,
                    Weight = f.partner ? PartnerWeight : own, HubName = f.partner ? null : hubName
                };
                m.Demand = type.HasValue
                    ? (outbound ? Rate(near, far, type) : Rate(far, near, type))
                    : Rate(near, far, null) + Rate(far, near, null);
                double direct = MathHelpers.GetDistance(near, far);
                if (!r.HasAirliner) m.Problem = "no aircraft flies " + Code(r.Destination1) + "-" + Code(r.Destination2) + " yet";
                else if (r.GetDistance() + leg >= direct*3) m.Problem = "too far out of the way (" + ((r.GetDistance() + leg)/Math.Max(1, direct)).ToString("0.0") + " times the nonstop distance)";
                else
                {
                    int n = NonstopRoutes(near, far);
                    if (n >= 2) m.Problem = n + " routes already fly " + Code(near) + "-" + Code(far) + " nonstop";
                }
                if (m.Demand <= 0 && m.Problem == null) m.Problem = "nobody wants to travel " + Code(near) + "-" + Code(far);
                yield return m;
            }
        }

        public static string Code(Airport a)
        {
            return a?.Profile.IATACode ?? "?";
        }

        /// <summary>All connection markets of a route, at both ends, both ways and all classes.</summary>
        public static List<ConnectionMarket> Markets(Route route)
        {
            var airline = route.Airline;
            var list = new List<ConnectionMarket>();
            if (airline == null || route.IsCargoRoute) return list;
            list.AddRange(MarketsAt(airline, route, route.Destination1, route.Destination2, null, true));
            list.AddRange(MarketsAt(airline, route, route.Destination2, route.Destination1, null, true));
            return list.OrderByDescending(m => m.Added).ThenByDescending(m => m.Demand).ToList();
        }

        /// <summary>
        ///     The connecting demand a flight from current to destination adds in one class (passengers a day), as the
        ///     flight engine uses it: passengers going on beyond the destination and passengers from beyond the start.
        /// </summary>
        public static double FlightDemand(Airline airline, Route route, Airport current, Airport destination, AirlinerClass.ClassType type)
        {
            if (airline == null) return 0;
            double v = 0;
            foreach (var m in MarketsAt(airline, route, current, destination, type, true)) v += m.Added;
            foreach (var m in MarketsAt(airline, route, destination, current, type, false)) v += m.Added;
            return v;
        }

        // ---- counting ----

        private static int MonthKey(DateTime d)
        {
            return d.Year*12 + d.Month - 1;
        }

        /// <summary>Counts a flight's passengers and how many of them connect.</summary>
        public static void Record(Route route, int passengers, double connecting)
        {
            if (route == null || passengers <= 0) return;
            int key = MonthKey(GameObject.GetInstance().GameTime);
            lock (route)
            {
                if (route.ConnectMonth != key)
                {
                    bool previous = route.ConnectMonth == key - 1;
                    route.ConnectLastPassengers = previous ? route.ConnectPassengers : 0;
                    route.ConnectLastAllPassengers = previous ? route.ConnectAllPassengers : 0;
                    route.ConnectPassengers = 0;
                    route.ConnectAllPassengers = 0;
                    route.ConnectMonth = key;
                }
                route.ConnectAllPassengers += passengers;
                route.ConnectPassengers += (int) Math.Round(Math.Max(0, Math.Min(passengers, connecting)));
            }
        }

        /// <summary>Last month's passengers and connecting passengers of a route.</summary>
        public static void LastMonth(Route route, out int connecting, out int all)
        {
            int key = MonthKey(GameObject.GetInstance().GameTime);
            if (route.ConnectMonth == key) { connecting = route.ConnectLastPassengers; all = route.ConnectLastAllPassengers; }
            else if (route.ConnectMonth == key - 1) { connecting = route.ConnectPassengers; all = route.ConnectAllPassengers; }
            else { connecting = 0; all = 0; }
        }

        /// <summary>This month's counts so far.</summary>
        public static void ThisMonth(Route route, out int connecting, out int all)
        {
            int key = MonthKey(GameObject.GetInstance().GameTime);
            if (route.ConnectMonth == key) { connecting = route.ConnectPassengers; all = route.ConnectAllPassengers; }
            else { connecting = 0; all = 0; }
        }

        // ---- planning ----

        public class HubIdea
        {
            public Airport Destination;
            public double Nonstop, Connecting;
            public int Feeders;
            public double Total => Nonstop + Connecting;
        }

        /// <summary>
        ///     New destinations from an airport the airline flies from, ranked by the passengers a day a route there
        ///     would get: the nonstop demand both ways and the connecting demand from the airline's other routes there.
        /// </summary>
        public static List<HubIdea> HubIdeas(Airline airline, Airport hub, int count, double maxKm)
        {
            string hubName;
            double weight = OwnWeightAt(airline, hub, out hubName);
            var spokes = airline.Routes.ToList()
                                .Where(r => !r.IsCargoRoute && r.HasAirliner && (r.Destination1 == hub || r.Destination2 == hub))
                                .Select(r => r.Destination1 == hub ? r.Destination2 : r.Destination1)
                                .Distinct().ToList();
            var served = new HashSet<Airport>(spokes);
            var ideas = new List<HubIdea>();
            foreach (var d in hub.GetDestinationDemands())
            {
                if (d == null || d == hub || served.Contains(d) || !GeneralHelpers.IsAirportActive(d)) continue;
                double km = MathHelpers.GetDistance(hub, d);
                if (km < 50 || km > maxKm) continue;
                var idea = new HubIdea { Destination = d, Nonstop = Rate(hub, d, null) + Rate(d, hub, null) };
                foreach (var s in spokes)
                {
                    if (s == d) continue;
                    double direct = MathHelpers.GetDistance(s, d);
                    if (MathHelpers.GetDistance(s, hub) + km >= direct*3 || NonstopRoutes(s, d) >= 2) continue;
                    double dem = Rate(s, d, null) + Rate(d, s, null);
                    if (dem <= 0) continue;
                    idea.Connecting += dem*weight;
                    idea.Feeders++;
                }
                if (idea.Total > 0) ideas.Add(idea);
            }
            return ideas.OrderByDescending(i => i.Total).Take(count).ToList();
        }
    }
}
