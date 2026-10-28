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

        // a route is given time to find its passengers before it is given up
        public const int DaysBeforeDrop = 60;

        /// <summary>Counters for the tests: destinations forecast, not the one with the most demand, all forecast to lose, routes given up.</summary>
        public static int Picked, PickedOther, AllLosing, Dropped;

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
        public static double FareFactor(Airline airline)
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
            List<FleetAirliner> fleet;
            lock (airline.Fleet)
            {
                fleet = airline.Fleet.Where(f => f.Airliner.Type.TypeAirliner != AirlinerType.TypeOfAirliner.Cargo
                                                 && f.Airliner.Range >= km && f.Airliner.GetTotalSeatCapacity() > 0).ToList();
            }
            if (fleet.Count == 0) return null;
            // the idle aircraft the route would get (as AIHelpers picks one: the shortest range that reaches)
            DateTime now = GameObject.GetInstance().GameTime;
            FleetAirliner idle = fleet.Where(f => !f.HasRoute && f.Airliner.BuiltDate <= now && f.Airliner.Range > km && AirlineTypeRules.CanUseType(airline, f.Airliner.Type))
                                      .OrderBy(f => f.Airliner.Range).FirstOrDefault();
            if (idle != null) return idle;
            fleet.Sort((x, y) => x.Airliner.GetTotalSeatCapacity().CompareTo(y.Airliner.GetTotalSeatCapacity()));
            return fleet[fleet.Count/2];
        }

        /// <summary>The forecast for the best number of round trips (up to what fits in a day).</summary>
        public static RouteForecast Best(Airline airline, Airport a, Airport b, FleetAirliner f, Route exclude = null, bool business = false, double fareFactor = 1)
        {
            int max = Math.Max(1, DayTrips(a, b, f, business));
            List<RouteForecast> all;
            if (!business) return RouteForecastService.Best(airline, a, b, f, max, out all, exclude, fareFactor);
            // a business route flies the same number of round trips in the morning and in the evening
            RouteForecast best = null;
            for (int k = 1; k <= max; k++)
            {
                var fc = RouteForecastService.Forecast(airline, a, b, f, 2*k, exclude, fareFactor);
                if (best == null || fc.ProfitPerDay > best.ProfitPerDay) best = fc;
            }
            return best;
        }

        /// <summary>
        ///     Of the destinations with the most demand, the one forecast to earn the most a day.
        /// </summary>
        public static Airport PickDestination(Airline airline, Airport origin, List<Airport> ranked)
        {
            if (ranked == null || ranked.Count == 0) return null;
            using (Profiler.Measure("route search: forecast destinations"))
            {
                double fares = FareFactor(airline);
                Airport best = null;
                double bestProfit = double.MinValue;
                foreach (Airport d in ranked.Take(Candidates))
                {
                    double km = MathHelpers.GetDistance(origin, d);
                    FleetAirliner f = Reference(airline, km);
                    if (f == null) continue;
                    RouteForecast fc = Best(airline, origin, d, f, null, false, fares);
                    if (fc.ProfitPerDay > bestProfit)
                    {
                        bestProfit = fc.ProfitPerDay;
                        best = d;
                    }
                }
                if (best == null) return ranked[0]; // nothing to forecast with yet: the most demand
                System.Threading.Interlocked.Increment(ref Picked);
                // even when every one is forecast to lose money it is flown: an idle aircraft costs its ownership too,
                // and a new route finds more passengers as the airline becomes known there
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
