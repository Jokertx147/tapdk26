using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Passengers;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     A suggested new passenger route from an airport, with the numbers behind the suggestion.
    /// </summary>
    public class RouteAdvice
    {
        public Airport Destination { get; set; }

        public double Distance { get; set; }

        //passengers per day in both directions, all classes
        public int DailyDemand { get; set; }

        public int Competitors { get; set; }

        public double RevenuePerDay { get; set; }

        //revenue after the competitors have taken their share; this is what the list is ranked by
        public double ScoreRevenuePerDay { get; set; }

        public bool FleetCanFly { get; set; }

        public string Advice { get; set; }
    }

    /// <summary>
    ///     Finds the routes from an airport that look the most lucrative for the human airline.
    ///     The numbers are estimates from demand, fares and competition; they do not include running costs.
    /// </summary>
    public static class RouteAdvisor
    {
        public static List<RouteAdvice> GetAdvice(Airport origin, int maxResults = 30)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            DateTime now = GameObject.GetInstance().GameTime;

            List<Airline> airlines = Airlines.GetAllAirlines();

            // how many other airlines already fly each airport pair, and which pairs the player already flies
            var competitors = new Dictionary<string, HashSet<Airline>>();
            var ownPairs = new HashSet<string>();

            foreach (Airline other in airlines)
            {
                foreach (Route route in new List<Route>(other.Routes))
                {
                    if (route.Type == Route.RouteType.Cargo)
                    {
                        continue;
                    }

                    string key = PairKey(route.Destination1, route.Destination2);

                    if (other == airline)
                    {
                        ownPairs.Add(key);
                        continue;
                    }

                    HashSet<Airline> set;
                    if (!competitors.TryGetValue(key, out set))
                    {
                        set = new HashSet<Airline>();
                        competitors[key] = set;
                    }

                    set.Add(other);
                }
            }

            List<AirlinerType> types = AirlinerTypes.GetTypes(
                t => t.Produced.From <= now && t.Produced.To > now && t.TypeAirliner != AirlinerType.TypeOfAirliner.Cargo);
            double maxRange = types.Count > 0 ? types.Max(t => t.Range) : double.MaxValue;

            // what the player's own fleet can do
            List<FleetAirliner> fleet = airline.Fleet.ToList();

            var classes = Enum.GetValues(typeof (AirlinerClass.ClassType)).Cast<AirlinerClass.ClassType>().ToList();

            var result = new List<RouteAdvice>();

            foreach (Airport destination in Airports.GetAllAirports())
            {
                if (destination == origin || destination.Profile?.Country == null || !GeneralHelpers.IsAirportActive(destination))
                {
                    continue;
                }

                string key = PairKey(origin, destination);

                if (ownPairs.Contains(key))
                {
                    continue;
                }

                double distance = MathHelpers.GetDistance(origin, destination);

                if (distance <= Route.MinRouteDistance || distance > maxRange)
                {
                    continue;
                }

                if (!AirlineHelpers.HasAirlineLicens(airline, origin, destination)
                    || !AIHelpers.IsRouteInCorrectArea(origin, destination)
                    || FlightRestrictions.HasRestriction(origin.Profile.Country, destination.Profile.Country, now, FlightRestriction.RestrictionType.Flights)
                    || FlightRestrictions.HasRestriction(destination.Profile.Country, origin.Profile.Country, now, FlightRestriction.RestrictionType.Flights)
                    || FlightRestrictions.HasRestriction(airline, origin.Profile.Country, destination.Profile.Country, now))
                {
                    continue;
                }

                double fare = PassengerHelpers.GetPassengerPrice(origin, destination);
                int demand = 0;
                double revenue = 0;

                foreach (AirlinerClass.ClassType type in classes)
                {
                    int both = origin.GetDestinationPassengersRate(destination, type)
                               + destination.GetDestinationPassengersRate(origin, type);

                    demand += both;
                    revenue += both * fare * GeneralHelpers.ClassToPriceFactor(type);
                }

                if (demand <= 0)
                {
                    continue;
                }

                int others = competitors.ContainsKey(key) ? competitors[key].Count : 0;

                long minRunway = Math.Min(origin.GetMaxRunwayLength(), destination.GetMaxRunwayLength());

                bool fleetCanFly = fleet.Any(
                    f => f.Airliner.Type.TypeAirliner != AirlinerType.TypeOfAirliner.Cargo
                         && f.Airliner.Type.Range >= distance && f.Airliner.Type.MinRunwaylength <= minRunway);

                result.Add(
                    new RouteAdvice
                    {
                        Destination = destination,
                        Distance = distance,
                        DailyDemand = demand,
                        Competitors = others,
                        RevenuePerDay = revenue,
                        ScoreRevenuePerDay = revenue / (1 + others),
                        FleetCanFly = fleetCanFly
                    });
            }

            List<RouteAdvice> best = result.OrderByDescending(r => r.ScoreRevenuePerDay).Take(maxResults).ToList();

            if (best.Count > 0)
            {
                double strongDemand = result.OrderByDescending(r => r.DailyDemand).ElementAt(Math.Max(0, result.Count / 10)).DailyDemand;

                foreach (RouteAdvice advice in best)
                {
                    var notes = new List<string>();

                    notes.Add(advice.Competitors == 0 ? "No competitors" : advice.Competitors + " competitor(s)");

                    if (advice.DailyDemand >= strongDemand)
                    {
                        notes.Add("strong demand");
                    }

                    if (!advice.FleetCanFly)
                    {
                        notes.Add("your fleet cannot fly it yet");
                    }

                    advice.Advice = string.Join(", ", notes);
                }
            }

            return best;
        }

        private static string PairKey(Airport a, Airport b)
        {
            string x = a.Profile.IATACode ?? "";
            string y = b.Profile.IATACode ?? "";

            return string.CompareOrdinal(x, y) < 0 ? x + "|" + y : y + "|" + x;
        }
    }
}
