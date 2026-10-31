using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
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

    /// <summary>Whether a route that flies would earn more with more or fewer round trips a day.</summary>
    public class TripAdvice
    {
        public int Now { get; set; }
        public int Max { get; set; }
        public RouteForecast Current { get; set; }
        public RouteForecast Best { get; set; }
        public double Gain => Best.ProfitPerDay - Current.ProfitPerDay;

        /// <summary>Flying Best.RoundTrips instead of Now earns clearly more (not just a rounding difference).</summary>
        public bool Change { get; set; }

        public string Text { get; set; }
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
        // a new route's service score (out of 10) when the airline has no route flying yet: the passenger demand is
        // multiplied by score/5 for a flight
        private const double UsualScore = 6.0;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Airline, Tuple<DateTime, double>> ScoreCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Airline, Tuple<DateTime, double>>();

        /// <summary>
        ///     The service score a new route of the airline can expect: the average of its passenger routes that fly
        ///     (aircraft, cabin, meals and fares much as on them), worked out once a game day; 6 without routes yet.
        /// </summary>
        public static double UsualScoreOf(Airline airline)
        {
            if (airline == null) return UsualScore;
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (ScoreCache.TryGetValue(airline, out cached) && cached.Item1 == today) return cached.Item2;
            var scores = new List<double>();
            foreach (Route r in airline.Routes.ToArray())
            {
                if (r is PassengerRoute && r.HasAirliner)
                {
                    try { scores.Add(PassengerHelpers.RouteScoreFor(r)); }
                    catch (Exception) { }
                }
            }
            double score = scores.Count > 0 ? scores.Average() : UsualScore;
            ScoreCache.AddOrUpdate(airline, Tuple.Create(today, score));
            return score;
        }

        // the airline's reputation share at an airport as its flights will get it: where it flies already, today's; at
        // an airport it doesn't serve yet (its reputation there rises once it flies there), its average where it does
        private static double ReputationFor(Airline airline, Airport airport)
        {
            double here = PassengerHelpers.ReputationShare(airport, airline);
            if (RouteIndex.At(airport).Any(r => r.Airline == airline && r.HasAirliner)) return here;
            return Math.Max(here, UsualReputationOf(airline));
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Airline, Tuple<DateTime, double>> ReputationCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Airline, Tuple<DateTime, double>>();

        /// <summary>The airline's average reputation share (0.75 to 1) at the airports it flies from, once a game day.</summary>
        public static double UsualReputationOf(Airline airline)
        {
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (ReputationCache.TryGetValue(airline, out cached) && cached.Item1 == today) return cached.Item2;
            var served = airline.Routes.ToArray().Where(r => r is PassengerRoute && r.HasAirliner).SelectMany(r => new[] {r.Destination1, r.Destination2}).Distinct().ToList();
            double v = served.Count > 0 ? served.Average(a => PassengerHelpers.ReputationShare(a, airline)) : 0.75;
            ReputationCache.AddOrUpdate(airline, Tuple.Create(today, v));
            return v;
        }

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
            // as a landing charges them: the flight's own time, fuel by seats and passengers, the landing fee and the
            // passenger tax, cabin wages, and the extra costs (handling, maintenance, en-route, crews, ticket sales,
            // overhead) from the tickets
            double hours = MathHelpers.GetFlightTime(from.Profile.Coordinates.ConvertToGeoCoordinate(), to.Profile.Coordinates.ConvertToGeoCoordinate(), a.Type).TotalHours;
            int seats = Math.Max(1, a.GetTotalSeatCapacity());
            double fuelPrice = AirportHelpers.GetFuelPrice(from);
            int maxSeats = a.Type is AirlinerPassengerType pt ? pt.MaxSeatingCapacity : seats;
            double fuel = fuelPrice*km*maxSeats*a.FuelConsumption*0.55 + fuelPrice*km*a.FuelConsumption*pax*0.45;
            double fees = AirportHelpers.GetLandingFee(to, a) + 5*pax;
            double cabin = (a.Type is AirlinerPassengerType p2 ? p2.CabinCrew : 0)*hours*airline.Fees.GetValue(FeeTypes.GetType("Cabin Wage"));
            var parts = FlightCosts.Calculate(a, km, hours, revenue);
            double ownership = flightsPerMonth > 0 ? OwnershipService.MonthlyCost(f)/flightsPerMonth : 0;
            return fuel + fees + cabin + parts.Total + ownership;
        }

        // what a passenger brings besides the ticket (in-flight sales less meals), from the route's last month (or
        // this month so far); 0 for a route that doesn't fly yet
        private static double ExtrasPerPassenger(Route route)
        {
            if (route == null) return 0;
            DateTime now = GameObject.GetInstance().GameTime;
            int connecting, carried;
            ConnectionService.LastMonth(route, out connecting, out carried);
            DateTime m = now.AddMonths(-1);
            if (carried <= 0)
            {
                carried = route.ConnectMonth == now.Year*12 + now.Month - 1 ? route.ConnectAllPassengers : 0;
                m = now;
            }
            if (carried <= 0) return 0;
            double extras = RouteProfitService.Month(route, Invoice.InvoiceType.Fees, m.Year, m.Month) + RouteProfitService.Month(route, Invoice.InvoiceType.OnFlightIncome, m.Year, m.Month);
            return extras/carried;
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
            public double OtherFlightsPerDay, Capacity, Extras;
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

            // a route that flies already: its own service score, fares and competition, as its flights get them
            var flown = exclude as PassengerRoute;
            if (flown != null && !flown.HasAirliner) flown = null;
            double score = flown != null ? PassengerHelpers.RouteScoreFor(flown) : UsualScoreOf(airline);
            List<Route> pairRoutes = null;
            if (flown != null) pairRoutes = others.Concat(new[] {(Route) flown}).ToList();
            p.Extras = ExtrasPerPassenger(flown);

            foreach (var dir in new[] {Tuple.Create(a, b), Tuple.Create(b, a)})
            {
                Airport from = dir.Item1, to = dir.Item2;
                double factors = PassengerHelpers.SeasonFactorFor(to)*PassengerHelpers.HolidayFactorFor(to)*PassengerHelpers.HolidayFactorFor(from)
                                 *game.Difficulty.PassengersLevel*score/5*FocusAndHub(airline, from, to, p.Km);
                double reputation = ReputationFor(airline, from);
                foreach (AirlinerClass c in f.Airliner.Classes)
                {
                    // as for a flight: the direct demand, passengers connecting to and from the airline's other routes
                    // at both ends, and passengers from airports nearby
                    double direct = from.GetDestinationPassengersRate(to, c.Type);
                    double connecting = ConnectionService.FlightDemand(airline, exclude, from, to, c.Type);
                    double nearby = PassengerHelpers.NearbyDemandFor(from, to, f, c.Type);
                    double market = PricingService.MarketFare(from, to, c.Type);
                    double fare = market*fareFactor;
                    var own = flown?.GetRouteAirlinerClass(c.Type);
                    if (own != null && own.FarePrice > 0) fare = own.FarePrice;
                    // fewer passengers above the market fare, more below it; on a shared route, the share its
                    // service and fare win
                    double price = own != null ? PricingService.DemandFactor(flown, c.Type) : PricingService.DemandFactor(fare, market, c.Type);
                    double competition = flown != null && PricingService.Realistic ? PricingService.CompetitionFactor(flown, pairRoutes, c.Type) : 1;
                    double demand = (direct + connecting + nearby)*factors*price;
                    p.Classes.Add(new[] {demand, c.SeatingCapacity, reputation*competition*game.Difficulty.PriceLevel, fare});
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
            result.RevenuePerFlight = revenue + pax*p.Extras;
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

        private static string Cash(double v)
        {
            return (v < 0 ? "-$" : "$") + Math.Abs(Math.Round(v)).ToString("N0");
        }

        /// <summary>
        ///     For a passenger route flown by one aircraft that flies nothing else, every day at the same times: the
        ///     forecast for each number of round trips from 1 to what fits between 06:00 and 22:00 (or more, if it flies
        ///     more already), with the route's own service score, fares and competition. Null when the route doesn't
        ///     qualify.
        /// </summary>
        public static TripAdvice AdviseTrips(Airline airline, Route route)
        {
            if (!(route is PassengerRoute) || route.HasStopovers || route.TimeTable?.Entries == null) return null;
            var airliners = route.GetAirliners();
            if (airliners.Count != 1) return null;
            FleetAirliner f = airliners[0];
            if (f.Routes.Count != 1) return null;
            int now = route.TimeTable.Entries.Count(e => e.Day == DayOfWeek.Monday)/2;
            if (now < 1 || route.TimeTable.Entries.Count != 14*now) return null;
            int max = Math.Max(now, RouteCreator.GetMaxRoundTrips(route.Destination1, route.Destination2, f.Airliner.Type, (int) FleetAirlinerHelpers.GetMinTimeBetweenFlights(f).TotalMinutes, OpsType.Regular));
            var all = ForecastEach(airline, route.Destination1, route.Destination2, f, Enumerable.Range(1, max), route);
            var advice = new TripAdvice {Now = now, Max = max, Current = all[now - 1], Best = all.OrderByDescending(x => x.ProfitPerDay).ThenBy(x => Math.Abs(x.RoundTrips - now)).First()};
            advice.Change = advice.Best.RoundTrips != now && advice.Gain > Math.Max(300, Math.Abs(advice.Current.ProfitPerDay)*0.05);
            if (advice.Change)
            {
                int n = advice.Best.RoundTrips;
                string what = n == now + 1 ? "One more round trip a day (" + n + ")"
                            : n == now - 1 ? "One round trip a day fewer (" + n + ")"
                            : "Flying " + n + " round trips a day instead of " + now;
                advice.Text = string.Format("{0} is forecast to earn about {1} more a day ({2} a day, about {3:0}% full).", what, Cash(advice.Gain), Cash(advice.Best.ProfitPerDay), advice.Best.Load*100);
            }
            return advice;
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
