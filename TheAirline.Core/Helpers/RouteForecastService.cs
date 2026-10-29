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

        // what doesn't depend on the number of round trips: the other flights and seats on the pair, and each class's
        // demand, share and fare both ways (worked out once, then tried for each number of round trips)
        private class Prepared
        {
            public Airline Airline;
            public Airport A, B;
            public FleetAirliner F;
            public double Km;
            public int Seats, Rivals;
            public double OtherFlightsPerDay, Capacity;
            public readonly List<double[]> Classes = new List<double[]>(); // demand, seats, share, fare
        }

        private static Prepared Prepare(Airline airline, Airport a, Airport b, FleetAirliner f, Route exclude, double fareFactor)
        {
            var p = new Prepared {Airline = airline, A = a, B = b, F = f, Seats = f.Airliner.GetTotalSeatCapacity(), Km = MathHelpers.GetDistance(a, b)};
            if (p.Seats <= 0) return p;
            var game = GameObject.GetInstance();

            List<Route> others = Others(a, b).Where(r => r != exclude && !r.GetAirliners().Contains(f)).ToList();
            p.Rivals = others.Count(r => r.Airline != airline);
            // flights a day on the pair (both ways, as the flight engine counts them) and the seats offered
            p.OtherFlightsPerDay = others.Sum(r => r.TimeTable.Entries.Count)/7.0;
            p.Capacity = others.Sum(r => r.GetAirliners().Select(x => x.Airliner.GetTotalSeatCapacity()).DefaultIfEmpty(0).Max()) + p.Seats;

            foreach (var dir in new[] {Tuple.Create(a, b), Tuple.Create(b, a)})
            {
                Airport from = dir.Item1, to = dir.Item2;
                double factors = PassengerHelpers.SeasonFactorFor(to)*game.Difficulty.PassengersLevel*UsualScore/5*FocusAndHub(airline, from, to, p.Km);
                double reputation = PassengerHelpers.ReputationShare(from, airline);
                foreach (AirlinerClass c in f.Airliner.Classes)
                {
                    // as for a flight: the direct demand, passengers connecting to and from the airline's other routes
                    // at both ends, and passengers from airports nearby
                    double direct = from.GetDestinationPassengersRate(to, c.Type);
                    double connecting = ConnectionService.FlightDemand(airline, exclude, from, to, c.Type);
                    double nearby = PassengerHelpers.NearbyDemandFor(from, to, f, c.Type);
                    double demand = (direct + connecting + nearby)*factors;
                    double fare = PricingService.MarketFare(from, to, c.Type)*fareFactor;
                    var own = exclude is PassengerRoute flown && flown.HasAirliner ? flown.GetRouteAirlinerClass(c.Type) : null;
                    if (own != null && own.FarePrice > 0) fare = own.FarePrice;
                    p.Classes.Add(new[] {demand, c.SeatingCapacity, reputation*game.Difficulty.PriceLevel, fare});
                }
            }
            return p;
        }

        private static RouteForecast Evaluate(Prepared p, int roundTrips)
        {
            var result = new RouteForecast {RoundTrips = roundTrips, Seats = p.Seats, Rivals = p.Rivals};
            if (roundTrips < 1 || p.Seats <= 0) return result;
            double flightsPerDay = p.OtherFlightsPerDay + 2*roundTrips;
            double pax = 0, revenue = 0;
            foreach (double[] c in p.Classes)
            {
                double perFlight = c[0]/Math.Max(1, flightsPerDay);
                double capacityShare = p.Capacity > 0 ? Math.Min(1, perFlight/p.Capacity) : 1;
                double seats = c[1];
                double n = Math.Min(seats, seats*c[2]*capacityShare);
                pax += n/2;
                revenue += n*c[3]/2;
            }
            result.PassengersPerFlight = pax;
            result.Load = Math.Min(1, pax/result.Seats);
            result.RevenuePerFlight = revenue;
            result.CostPerFlight = Cost(p.F, p.Airline, p.A, p.B, p.Km, pax, revenue, roundTrips*2*30.4);
            return result;
        }

        /// <summary>The forecast for the aircraft flying the pair `roundTrips` times a day (both ways averaged).</summary>
        /// <param name="fareFactor">the fares as a share of the market fares (a route that flies already uses its own fares)</param>
        public static RouteForecast Forecast(Airline airline, Airport a, Airport b, FleetAirliner f, int roundTrips, Route exclude = null, double fareFactor = 1)
        {
            if (roundTrips < 1) return new RouteForecast {RoundTrips = roundTrips, Seats = f.Airliner.GetTotalSeatCapacity()};
            return Evaluate(Prepare(airline, a, b, f, exclude, fareFactor), roundTrips);
        }

        /// <summary>The forecasts for each of the given numbers of round trips a day (the demand worked out once).</summary>
        public static List<RouteForecast> ForecastEach(Airline airline, Airport a, Airport b, FleetAirliner f, IEnumerable<int> roundTrips, Route exclude = null, double fareFactor = 1)
        {
            var p = Prepare(airline, a, b, f, exclude, fareFactor);
            return roundTrips.Select(n => Evaluate(p, n)).ToList();
        }

        /// <summary>The forecasts for 1 to `maxTrips` round trips a day, and the one that earns the most a day.</summary>
        public static RouteForecast Best(Airline airline, Airport a, Airport b, FleetAirliner f, int maxTrips, out List<RouteForecast> all, Route exclude = null, double fareFactor = 1)
        {
            all = ForecastEach(airline, a, b, f, Enumerable.Range(1, Math.Max(1, maxTrips)), exclude, fareFactor);
            // the most profit a day; when every choice loses money, the smallest loss
            return all.OrderByDescending(x => x.ProfitPerDay).ThenBy(x => x.RoundTrips).First();
        }
    }
}
