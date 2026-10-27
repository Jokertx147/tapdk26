using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>What a passenger route flown by one aircraft is expected to do, at one number of round trips a day.</summary>
    public class RouteForecast
    {
        public int RoundTrips { get; set; }
        public double Load { get; set; }
        public double PassengersPerFlight { get; set; }
        public int Seats { get; set; }
        public double RevenuePerFlight { get; set; }
        public double CostPerFlight { get; set; }
        public double ProfitPerFlight => RevenuePerFlight - CostPerFlight;
        public double ProfitPerDay => ProfitPerFlight*RoundTrips*2;

        /// <summary>The rivals' share of the seats on the pair (0 when nobody else flies it).</summary>
        public int Rivals { get; set; }
    }

    /// <summary>
    ///     A forecast for a new passenger route before it is flown, worked out the way a flight's passengers and costs are:
    ///     the demand both ways (season, difficulty, the airline's focus and hubs, a usual service score), split over all
    ///     the flights on the airport pair and set against all the seats offered there, the airline's reputation at the
    ///     airport, the market fares, and the flight's costs (fuel, fees, crew, ground, maintenance, sales and overhead,
    ///     and the aircraft's ownership spread over its flights). It is an estimate: rivals change fares and schedules.
    /// </summary>
    public static class RouteForecastService
    {
        // a new route's usual service score (out of 10) when the forecast is made: the passenger demand is multiplied
        // by score/5 for a flight
        private const double UsualScore = 6.0;

        private static double FocusAndHub(Airline airline, Airport from, Airport to, double km)
        {
            double f = 1;
            bool sameCountry = from.Profile.Country == to.Profile.Country;
            switch (airline.MarketFocus)
            {
                case AirlineFocus.Global: if (km > 3000 && !sameCountry) f *= 1.15; break;
                case AirlineFocus.Regional: if (km < 1500) f *= 1.15; break;
                case AirlineFocus.Domestic: if (km < 1500 && sameCountry) f *= 1.15; break;
                case AirlineFocus.Local: if (km < 1000) f *= 1.15; break;
            }
            var hub = to.GetHubs().Find(h => h.Airline == airline);
            if (hub != null)
            {
                switch (hub.Type.Type)
                {
                    case HubType.TypeOfHub.FocusCity: if (sameCountry) f *= 1.15; break;
                    case HubType.TypeOfHub.RegionalHub: if (from.Profile.Country.Region == to.Profile.Country.Region) f *= 1.20; break;
                    case HubType.TypeOfHub.Hub: f *= 1.20; break;
                    case HubType.TypeOfHub.FortressHub: f *= 1.30; break;
                }
            }
            return f;
        }

        // the other airlines' (and the airline's own) flying passenger routes on the pair
        private static List<Route> Others(Airport a, Airport b)
        {
            return RouteIndex.Direct(a, b).Concat(RouteIndex.WithLeg(a, b))
                             .Where(r => (r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed) && r.HasAirliner).Distinct().ToList();
        }

        /// <summary>The flight costs of one flight with `pax` passengers paying `revenue`.</summary>
        private static double Cost(FleetAirliner f, Airline airline, Airport from, Airport to, double km, double pax, double revenue, double flightsPerMonth)
        {
            Airliner a = f.Airliner;
            double hours = km/Math.Max(100, a.Type.CruisingSpeed) + 0.5;
            int seats = Math.Max(1, a.GetTotalSeatCapacity());
            double fuelPrice = AirportHelpers.GetFuelPrice(from);
            int maxSeats = a.Type is AirlinerPassengerType pt ? pt.MaxSeatingCapacity : seats;
            double fuel = fuelPrice*km*maxSeats*a.FuelConsumption*0.55 + fuelPrice*km*a.FuelConsumption*pax*0.45;
            double fees = AirportHelpers.GetLandingFee(to, a) + 5*pax;
            double cabin = (a.Type is AirlinerPassengerType p2 ? p2.CabinCrew : 0)*hours*airline.Fees.GetValue(FeeTypes.GetType("Cabin Wage"));
            var parts = FlightCosts.Calculate(a, km, hours, 0);
            double ownership = flightsPerMonth > 0 ? OwnershipService.MonthlyCost(f)/flightsPerMonth : 0;
            double share = FlightCosts.Enabled ? (FlightCosts.DistributionShare(airline) + 0.07)*(FlightCosts.Level >= 2 ? 1.4 : 1) : 0;
            return fuel + fees + cabin + parts.Ground + parts.Maintenance + parts.EnRoute + parts.Crew + ownership + revenue*share;
        }

        /// <summary>The forecast for the aircraft flying the pair `roundTrips` times a day (both ways averaged).</summary>
        public static RouteForecast Forecast(Airline airline, Airport a, Airport b, FleetAirliner f, int roundTrips, Route exclude = null)
        {
            var result = new RouteForecast {RoundTrips = roundTrips, Seats = f.Airliner.GetTotalSeatCapacity()};
            if (roundTrips < 1 || result.Seats <= 0) return result;
            double km = MathHelpers.GetDistance(a, b);
            var game = GameObject.GetInstance();

            List<Route> others = Others(a, b).Where(r => r != exclude && !r.GetAirliners().Contains(f)).ToList();
            result.Rivals = others.Count(r => r.Airline != airline);
            // flights a day on the pair (both ways, as the flight engine counts them) and the seats offered
            double flightsPerDay = others.Sum(r => r.TimeTable.Entries.Count)/7.0 + 2*roundTrips;
            double capacity = others.Sum(r => r.GetAirliners().Select(x => x.Airliner.GetTotalSeatCapacity()).DefaultIfEmpty(0).Max()) + result.Seats;

            double pax = 0, revenue = 0;
            foreach (var dir in new[] {Tuple.Create(a, b), Tuple.Create(b, a)})
            {
                Airport from = dir.Item1, to = dir.Item2;
                double factors = PassengerHelpers.SeasonFactorFor(to)*game.Difficulty.PassengersLevel*UsualScore/5*FocusAndHub(airline, from, to, km);
                double reputation = PassengerHelpers.ReputationShare(from, airline);
                foreach (AirlinerClass c in f.Airliner.Classes)
                {
                    // as for a flight: the direct demand, passengers connecting to and from the airline's other routes
                    // at both ends, and passengers from airports nearby
                    double direct = from.GetDestinationPassengersRate(to, c.Type);
                    double connecting = ConnectionService.FlightDemand(airline, exclude, from, to, c.Type);
                    double nearby = PassengerHelpers.NearbyDemandFor(from, to, f, c.Type);
                    double demand = (direct + connecting + nearby)*factors;
                    double perFlight = demand/Math.Max(1, flightsPerDay);
                    double capacityShare = capacity > 0 ? Math.Min(1, perFlight/capacity) : 1;
                    double p = Math.Min(c.SeatingCapacity, c.SeatingCapacity*reputation*capacityShare*game.Difficulty.PriceLevel);
                    pax += p/2;
                    revenue += p*PricingService.MarketFare(from, to, c.Type)/2;
                }
            }
            result.PassengersPerFlight = pax;
            result.Load = Math.Min(1, pax/result.Seats);
            result.RevenuePerFlight = revenue;
            result.CostPerFlight = Cost(f, airline, a, b, km, pax, revenue, roundTrips*2*30.4);
            return result;
        }

        /// <summary>The forecasts for 1 to `maxTrips` round trips a day, and the one that earns the most a day.</summary>
        public static RouteForecast Best(Airline airline, Airport a, Airport b, FleetAirliner f, int maxTrips, out List<RouteForecast> all, Route exclude = null)
        {
            all = new List<RouteForecast>();
            for (int n = 1; n <= Math.Max(1, maxTrips); n++) all.Add(Forecast(airline, a, b, f, n, exclude));
            // the most profit a day; when every choice loses money, the smallest loss
            return all.OrderByDescending(x => x.ProfitPerDay).ThenBy(x => x.RoundTrips).First();
        }
    }
}
