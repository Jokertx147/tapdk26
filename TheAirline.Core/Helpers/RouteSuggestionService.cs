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
    /// <summary>A route an idle aircraft could fly, with the forecast for its best number of round trips.</summary>
    public class RouteSuggestion
    {
        public FleetAirliner Airliner { get; set; }
        public Airport From { get; set; }
        public Airport To { get; set; }
        public double Km { get; set; }

        /// <summary>The route advisor's passengers a day, both ways.</summary>
        public int DailyDemand { get; set; }

        /// <summary>The forecast for the best number of round trips (with the planner's turnaround).</summary>
        public RouteForecast Forecast { get; set; }

        public int Turnaround { get; set; }

        /// <summary>The airports where gates must be leased first (none: the player has free gates at both ends).</summary>
        public List<Airport> GatesToLease { get; set; } = new List<Airport>();

        /// <summary>What the gates to lease cost a day (the planner's first contract, 2 years).</summary>
        public double GatesPerDay { get; set; }

        /// <summary>What the aircraft costs a day to own or lease, flying or not.</summary>
        public double OwnershipPerDay { get; set; }

        /// <summary>The flights' profit a day less the new gates: what the route adds to the airline.</summary>
        public double GainPerDay => Forecast.ProfitPerDay - GatesPerDay;

        /// <summary>After the aircraft's own cost too.</summary>
        public double NetPerDay => GainPerDay - OwnershipPerDay;

        public string Name => From.Profile.IATACode + "-" + To.Profile.IATACode;
    }

    /// <summary>
    ///     Routes for the player's idle passenger aircraft: the route advisor's best destinations from the player's main
    ///     airports, each forecast with that aircraft (the same forecast the route planner and the computer airlines use),
    ///     ranked by what they add a day.
    /// </summary>
    public static class RouteSuggestionService
    {
        /// <summary>Airports suggested from: the aircraft's home base and the player's busiest airports.</summary>
        public const int Origins = 5;

        /// <summary>Destinations the advisor gives for each airport, before the forecast.</summary>
        public const int PerOrigin = 12;

        /// <summary>The player's delivered passenger aircraft without a route (not leased out).</summary>
        public static List<FleetAirliner> IdleAircraft(Airline airline)
        {
            if (airline == null) return new List<FleetAirliner>();
            return airline.DeliveredFleet
                          .Where(f => (f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger || f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed)
                                      && f.Routes.Count == 0 && !AircraftDealService.IsLeasedOut(f) && f.UsableBy(airline) && f.Airliner.GetTotalSeatCapacity() > 0)
                          .OrderBy(f => f.Name).ToList();
        }

        /// <summary>The planner's turnaround for the aircraft: the shortest choice that its minimum time between flights fits.</summary>
        public static int Turnaround(FleetAirliner f)
        {
            int minutes = (int) FleetAirlinerHelpers.GetMinTimeBetweenFlights(f).TotalMinutes;
            int best = RouteCreator.TurnaroundOptions.FirstOrDefault(o => o >= minutes);
            return best == 0 ? RouteCreator.TurnaroundOptions.Last() : best;
        }

        private static List<Airport> OriginsFor(Airline airline, FleetAirliner f)
        {
            var list = new List<Airport>();
            if (f.Homebase != null) list.Add(f.Homebase);
            var routes = airline.Routes.ToList();
            foreach (Airport a in airline.Airports.ToList()
                                         .OrderByDescending(a => routes.Count(r => r.Destination1 == a || r.Destination2 == a))
                                         .ThenByDescending(a => (int) a.Profile.Size))
            {
                if (list.Count >= Origins) break;
                if (!list.Contains(a)) list.Add(a);
            }
            return list;
        }

        // the gates the player must lease at an airport for the route; false when there are none to be had
        private static bool Gates(Airline airline, Airport a, RouteSuggestion s)
        {
            if (AirportHelpers.HasFreeGates(a, airline, Terminal.TerminalType.Passenger)) return true;
            if (!RouteCreator.NeedsContract(a, Terminal.TerminalType.Passenger)) return false; // all its gates there are in use
            int gates = Math.Min(2, RouteCreator.GetFreeGates(a, Terminal.TerminalType.Passenger));
            if (gates <= 0) return false;
            s.GatesToLease.Add(a);
            s.GatesPerDay += AirportHelpers.GetYearlyContractPayment(a, AirportContract.ContractType.Full, gates, 2)/365.0;
            return true;
        }

        /// <summary>The best routes for an aircraft, the most added a day first.</summary>
        /// <param name="advice">The advisor's destinations by airport, shared between the aircraft of one search.</param>
        public static List<RouteSuggestion> For(Airline airline, FleetAirliner f, int max = 5, Dictionary<Airport, List<RouteAdvice>> advice = null)
        {
            advice = advice ?? new Dictionary<Airport, List<RouteAdvice>>();
            var result = new List<RouteSuggestion>();
            var flown = new HashSet<string>(airline.Routes.ToList().Where(r => r.Destination1 != null && r.Destination2 != null)
                                                   .Select(r => Key(r.Destination1, r.Destination2)));
            int turnaround = Turnaround(f);
            double ownership = OwnershipService.MonthlyCost(f)/30.4;
            var seen = new HashSet<string>();
            foreach (Airport from in OriginsFor(airline, f))
            {
                List<RouteAdvice> dests;
                if (!advice.TryGetValue(from, out dests))
                {
                    try { dests = RouteAdvisor.GetAdvice(from, PerOrigin*2); }
                    catch (Exception) { dests = new List<RouteAdvice>(); } // another airline changed its routes meanwhile
                    advice[from] = dests;
                }
                foreach (RouteAdvice a in dests.Take(PerOrigin*2))
                {
                    Airport to = a.Destination;
                    if (to == null || to == from) continue;
                    string key = Key(from, to);
                    if (flown.Contains(key) || !seen.Add(key)) continue;
                    if (a.Distance > f.Airliner.Range || f.Airliner.MinRunwaylength > Math.Min(from.GetMaxRunwayLength(), to.GetMaxRunwayLength())) continue;
                    var s = new RouteSuggestion {Airliner = f, From = from, To = to, Km = a.Distance, DailyDemand = a.DailyDemand, Turnaround = turnaround, OwnershipPerDay = ownership};
                    if (!Gates(airline, from, s) || !Gates(airline, to, s)) continue;
                    int maxTrips = RouteCreator.GetMaxRoundTrips(from, to, f.Airliner.Type, turnaround, OpsType.Regular);
                    List<RouteForecast> all;
                    s.Forecast = RouteForecastService.Best(airline, from, to, f, maxTrips, out all);
                    if (s.Forecast == null) continue;
                    result.Add(s);
                }
            }
            return result.OrderByDescending(s => s.GainPerDay).Take(max).ToList();
        }

        /// <summary>Suggestions for every idle aircraft (an airport's destinations are looked up once).</summary>
        public static Dictionary<FleetAirliner, List<RouteSuggestion>> ForIdle(Airline airline, int max = 5)
        {
            var advice = new Dictionary<Airport, List<RouteAdvice>>();
            var result = new Dictionary<FleetAirliner, List<RouteSuggestion>>();
            foreach (FleetAirliner f in IdleAircraft(airline)) result[f] = For(airline, f, max, advice);
            return result;
        }

        private static string Key(Airport a, Airport b)
        {
            string x = a.Profile.IATACode, y = b.Profile.IATACode;
            return string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x;
        }
    }
}
