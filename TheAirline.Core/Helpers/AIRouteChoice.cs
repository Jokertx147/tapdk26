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
    ///     How the computer airlines use the route forecast (the same one the route planner shows): which of the
    ///     destinations with the most demand earns the most, and when a passenger route that loses money is given up.
    ///     (Tried and measured over a game year: cutting round trips by the forecast earned the airlines less than filling
    ///     the day, so they keep filling it.)
    /// </summary>
    public static class AIRouteChoice
    {
        // how many of the destinations with the most demand are forecast
        public const int Candidates = 6;

        /// <summary>
        ///     A route the forecast says would fly emptier than this is not opened. Measured over 93 new routes of the
        ///     computer airlines: every one of the 54 forecast under 15% full lost money a month later, together about
        ///     2.7 million a day, and not one recovered (the ones forecast under 5% full ended at 7% full). Above it
        ///     they are mixed, and by 30% most pay, so the floor only rules out the hopeless.
        /// </summary>
        public static double MinLoad = 0.15;

        /// <summary>
        ///     When the destinations with the most demand are all too empty, this many are forecast before the airline
        ///     gives up for now: a thin home airport should send it further afield, not stop it growing.
        /// </summary>
        public const int WideCandidates = 24;

        // a route is given time to find its passengers before it is given up
        public const int DaysBeforeDrop = 60;

        /// <summary>Counters for the tests: destinations forecast, not the one with the most demand, all forecast to lose, routes given up.</summary>
        public static int Picked, PickedOther, AllLosing, Dropped;

        /// <summary>Counters for the tests: searches that went past the first candidates to find a route with passengers, and that found none.</summary>
        public static int Widened, TooEmpty;

        /// <summary>Counters for the tests: route searches that found no start airport, no destination, no aircraft; that used an idle aircraft; that bought or leased one.</summary>
        public static int NoStart, NoDestination, NoAircraft, UsedIdle, GotAircraft, IdleRoutes, EmptyDropped;

        /// <summary>For the probes: called when a computer airline's passenger route gets its timetable (route, aircraft, round trips).</summary>
        public static Action<Route, FleetAirliner, int> RecordCreation;

        /// <summary>How many round trips a day fit between 06:00 and 22:00 (or in the morning and evening for a business route).</summary>
        public static int DayTrips(Airport a, Airport b, FleetAirliner f, bool business = false)
        {
            TimeSpan min = MathHelpers.GetFlightTime(a.Profile.Coordinates.ConvertToGeoCoordinate(), b.Profile.Coordinates.ConvertToGeoCoordinate(), f.Airliner.Type)
                                      .Add(FleetAirlinerHelpers.GetMinTimeBetweenFlights(f));
            if (min.TotalMinutes <= 0) return 1;
            return (int) ((business ? 4 : 16)*60/(2*min.TotalMinutes));
        }

        /// <summary>
        ///     What the airline charges against the market fare on the routes it flies (its new routes are priced the same
        ///     way soon after they open), within its mentality's band; 1 for an airline without routes yet.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Airline, Tuple<DateTime, double>> FareCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Airline, Tuple<DateTime, double>>();

        public static double FareFactor(Airline airline)
        {
            // worked out once a game day per airline
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (FareCache.TryGetValue(airline, out cached) && cached.Item1 == today) return cached.Item2;
            double factor = WorkOutFareFactor(airline);
            FareCache.AddOrUpdate(airline, Tuple.Create(today, factor));
            return factor;
        }

        private static double WorkOutFareFactor(Airline airline)
        {
            double low, high;
            AIPricingService.Band(airline.Mentality, out low, out high);
            var ratios = new List<double>();
            foreach (Route r in airline.Routes.ToArray())
            {
                if (!(r is PassengerRoute pr) || !pr.HasAirliner) continue;
                double market = PricingService.MarketFare(r.Destination1, r.Destination2, AirlinerClass.ClassType.EconomyClass);
                double fare = AIPricingService.EconomyFare(pr);
                if (market > 0 && fare > 0) ratios.Add(fare/market);
            }
            if (ratios.Count == 0) return 1;
            return Math.Max(low, Math.Min(high, ratios.Average()));
        }

        /// <summary>A passenger aircraft of the airline's fleet to forecast a destination with: the idle one it would fly, else the middle one by seats that reaches it.</summary>
        public static FleetAirliner Reference(Airline airline, double km)
        {
            return new FleetSnapshot(airline).Reference(km);
        }

        // the airline's passenger aircraft, read once for all the destinations of one search
        private class FleetSnapshot
        {
            private readonly List<FleetAirliner> _bySeats, _idleByRange;

            public FleetSnapshot(Airline airline)
            {
                List<FleetAirliner> fleet;
                lock (airline.Fleet)
                {
                    fleet = airline.Fleet.Where(f => f.Airliner.Type.TypeAirliner != AirlinerType.TypeOfAirliner.Cargo && f.Airliner.GetTotalSeatCapacity() > 0).ToList();
                }
                DateTime now = GameObject.GetInstance().GameTime;
                _bySeats = fleet.OrderBy(f => f.Airliner.GetTotalSeatCapacity()).ToList();
                _idleByRange = fleet.Where(f => !f.HasRoute && f.Airliner.BuiltDate <= now && f.UsableBy(airline) && AirlineTypeRules.CanUseType(airline, f.Airliner.Type))
                                    .OrderBy(f => f.Airliner.Range).ToList();
            }

            public FleetAirliner Reference(double km)
            {
                // the idle aircraft the route would get (as AIHelpers picks one: the shortest range that reaches)
                FleetAirliner idle = _idleByRange.FirstOrDefault(f => f.Airliner.Range > km);
                if (idle != null) return idle;
                var reach = _bySeats.Where(f => f.Airliner.Range >= km).ToList();
                return reach.Count == 0 ? null : reach[reach.Count/2];
            }
        }

        /// <summary>The forecast for the best number of round trips (up to what fits in a day).</summary>
        public static RouteForecast Best(Airline airline, Airport a, Airport b, FleetAirliner f, Route exclude = null, bool business = false, double fareFactor = 1)
        {
            int max = Math.Max(1, DayTrips(a, b, f, business));
            List<RouteForecast> all;
            if (!business) return RouteForecastService.Best(airline, a, b, f, max, out all, exclude, fareFactor);
            // a business route flies the same number of round trips in the morning and in the evening
            RouteForecast best = null;
            foreach (var fc in RouteForecastService.ForecastEach(airline, a, b, f, Enumerable.Range(1, max).Select(k => 2*k), exclude, fareFactor))
            {
                if (best == null || fc.ProfitPerDay > best.ProfitPerDay) best = fc;
            }
            return best;
        }

        /// <summary>
        ///     Of the destinations with the most demand, the one forecast to earn the most a day. (Not opening a route the
        ///     forecast says won't pay was tried: new airlines then couldn't grow, and most lost money all year.)
        /// </summary>
        public static Airport PickDestination(Airline airline, Airport origin, List<Airport> ranked)
        {
            if (ranked == null || ranked.Count == 0) return null;
            using (Profiler.Measure("route search: forecast destinations"))
            {
                double fares = FareFactor(airline);
                var fleet = new FleetSnapshot(airline);
                Airport best = null, bestAnyLoad = null;
                double bestProfit = double.MinValue;
                bool forecast = false;
                int looked = 0;
                foreach (Airport d in ranked)
                {
                    // the destinations with the most demand first; further down the list only while they are all empty
                    if (looked >= Candidates && best != null) break;
                    if (looked >= WideCandidates) break;
                    double km = MathHelpers.GetDistance(origin, d);
                    FleetAirliner f = fleet.Reference(km);
                    if (f == null) continue;
                    looked++;
                    // at the round trips that earn the most, which asks whether there is a route here worth flying
                    // at all. (Judging it instead at the trips the airline will really fly, which fill the day, was
                    // measured over five game years and came out worse: it turned away routes that are fine at a
                    // lower frequency, and the airline's habit of filling the day is a separate matter.)
                    RouteForecast fc = Best(airline, origin, d, f, null, false, fares);
                    forecast = true;
                    if (bestAnyLoad == null) bestAnyLoad = d;
                    // a route the forecast leaves nearly empty never fills up: it is passed over, however it compares
                    // with the other empty ones
                    if (fc.Load < MinLoad) continue;
                    if (best == null || fc.ProfitPerDay > bestProfit)
                    {
                        bestProfit = fc.ProfitPerDay;
                        best = d;
                    }
                }
                if (!forecast) return ranked[0]; // nothing to forecast with yet: the most demand
                if (best == null)
                {
                    // nowhere within reach has the passengers to fill a flight: no route today, and the aircraft is
                    // left for another search (or, once it has sat long enough, for sale)
                    System.Threading.Interlocked.Increment(ref TooEmpty);
                    return null;
                }
                if (looked > Candidates) System.Threading.Interlocked.Increment(ref Widened);
                System.Threading.Interlocked.Increment(ref Picked);
                // a route that fills its seats but is still forecast to lose money is flown: an idle aircraft costs its
                // ownership too, and a new route finds more passengers as the airline becomes known there
                if (bestProfit < 0) System.Threading.Interlocked.Increment(ref AllLosing);
                if (best != ranked[0]) System.Threading.Interlocked.Increment(ref PickedOther);
                return best;
            }
        }

        /// <summary>Round trips a day the route flies now (one aircraft; a business route counts its morning and evening).</summary>
        public static int CurrentTrips(Route route)
        {
            // a Monday's flights, both ways (a business route doesn't fly at the weekend)
            return route.TimeTable.Entries.Count(e => e.Day == DayOfWeek.Monday)/2;
        }

        /// <summary>
        ///     Whether a computer airline gives up a passenger route: it has flown two months, loses money with half its
        ///     seats empty or more, lost money over those two months, and the forecast says no number of round trips
        ///     would pay for its flights (the aircraft's ownership is paid whether it flies or not). A full route that
        ///     loses money is kept: its fares go up instead.
        /// </summary>
        public static bool ShouldDrop(Airline airline, Route route, double balance)
        {
            if (!(route is PassengerRoute) || !route.HasAirliner || route.TimeTable.Entries.Count == 0) return false;
            if (balance >= -1000 || route.FillingDegree >= 0.5) return false;
            var airliners = route.GetAirliners();
            if (airliners.Count != 1) return false;
            DateTime today = GameObject.GetInstance().GameTime;
            if ((today - route.StartDate).TotalDays < DaysBeforeDrop) return false;
            if (route.GetBalance(today.AddDays(-DaysBeforeDrop), today) >= 0) return false;

            FleetAirliner f = airliners[0];
            RouteForecast best = Best(airline, route.Destination1, route.Destination2, f, route, IsBusiness(route, f));
            return best.ProfitPerDay + OwnershipService.MonthlyCost(f)/30.4 < 0;
        }

        public static bool IsBusiness(Route route, FleetAirliner f)
        {
            TimeSpan min = route.GetFlightTime(f.Airliner.Type).Add(FleetAirlinerHelpers.GetMinTimeBetweenFlights(f));
            return min.TotalMinutes <= 120;
        }
    }
}
