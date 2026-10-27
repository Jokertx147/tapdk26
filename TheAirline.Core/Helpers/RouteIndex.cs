using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Every airline's routes by airport pair, so the passenger and cargo calculation for one flight doesn't have to
    ///     look through all routes of all airlines. Rebuilt when a route is added or removed, a stopover changes, or the
    ///     day changes.
    /// </summary>
    public static class RouteIndex
    {
        private sealed class Snapshot
        {
            public int Version;
            public System.DateTime Day;
            public readonly Dictionary<Airport, Dictionary<Airport, List<Route>>> Direct = new Dictionary<Airport, Dictionary<Airport, List<Route>>>();
            public readonly Dictionary<Airport, Dictionary<Airport, List<Route>>> Legs = new Dictionary<Airport, Dictionary<Airport, List<Route>>>();
            public readonly Dictionary<Airport, List<Route>> At = new Dictionary<Airport, List<Route>>();
        }

        private static readonly object BuildLock = new object();
        private static Snapshot _snapshot;
        private static int _version;

        private static readonly List<Route> None = new List<Route>();

        /// <summary>Call when routes change (added, removed, stopover added) or a game is loaded.</summary>
        public static void Invalidate()
        {
            Interlocked.Increment(ref _version);
        }

        private static Snapshot Current()
        {
            var day = GameObject.GetInstance().GameTime.Date;
            int version = Volatile.Read(ref _version);
            var s = Volatile.Read(ref _snapshot);
            if (s != null && s.Version == version && s.Day == day) return s;
            lock (BuildLock)
            {
                version = Volatile.Read(ref _version);
                s = _snapshot;
                if (s != null && s.Version == version && s.Day == day) return s;
                s = new Snapshot { Version = version, Day = day };
                foreach (Airline airline in Airlines.GetAllAirlines().ToList())
                {
                    foreach (Route r in airline.Routes)
                    {
                        if (r?.Destination1 == null || r.Destination2 == null) continue;
                        AddBoth(s.Direct, r.Destination1, r.Destination2, r);
                        AddAt(s.At, r.Destination1, r);
                        if (r.Destination2 != r.Destination1) AddAt(s.At, r.Destination2, r);
                        foreach (var stop in r.Stopovers.ToList())
                        {
                            foreach (var leg in stop.Legs.ToList())
                            {
                                if (leg?.Destination1 == null || leg.Destination2 == null) continue;
                                AddBoth(s.Legs, leg.Destination1, leg.Destination2, r);
                            }
                        }
                    }
                }
                Volatile.Write(ref _snapshot, s);
                return s;
            }
        }

        private static void AddAt(Dictionary<Airport, List<Route>> map, Airport a, Route r)
        {
            List<Route> list;
            if (!map.TryGetValue(a, out list)) map[a] = list = new List<Route>();
            list.Add(r);
        }

        /// <summary>Every airline's routes that start or end at the airport, in airline order. Don't change the list.</summary>
        public static List<Route> At(Airport airport)
        {
            List<Route> list;
            if (airport != null && Current().At.TryGetValue(airport, out list)) return list;
            return None;
        }

        private static void AddBoth(Dictionary<Airport, Dictionary<Airport, List<Route>>> map, Airport a, Airport b, Route r)
        {
            Add(map, a, b, r);
            if (a != b) Add(map, b, a, r);
        }

        private static void Add(Dictionary<Airport, Dictionary<Airport, List<Route>>> map, Airport a, Airport b, Route r)
        {
            Dictionary<Airport, List<Route>> inner;
            if (!map.TryGetValue(a, out inner)) map[a] = inner = new Dictionary<Airport, List<Route>>();
            List<Route> list;
            if (!inner.TryGetValue(b, out list)) inner[b] = list = new List<Route>();
            // a route with several legs on the same pair is listed once
            if (list.Count == 0 || !ReferenceEquals(list[list.Count - 1], r)) list.Add(r);
        }

        private static List<Route> Get(Dictionary<Airport, Dictionary<Airport, List<Route>>> map, Airport a, Airport b)
        {
            Dictionary<Airport, List<Route>> inner;
            List<Route> list;
            if (a != null && b != null && map.TryGetValue(a, out inner) && inner.TryGetValue(b, out list)) return list;
            return None;
        }

        /// <summary>Routes flown nonstop between the two airports (either direction). Don't change the list.</summary>
        public static List<Route> Direct(Airport a, Airport b)
        {
            return Get(Current().Direct, a, b);
        }

        /// <summary>Routes with a stopover leg between the two airports (either direction). Don't change the list.</summary>
        public static List<Route> WithLeg(Airport a, Airport b)
        {
            return Get(Current().Legs, a, b);
        }

        /// <summary>Whether any airline has a route between the two airports.</summary>
        public static bool HasRoute(Airport a, Airport b)
        {
            return Direct(a, b).Count > 0;
        }
    }
}
