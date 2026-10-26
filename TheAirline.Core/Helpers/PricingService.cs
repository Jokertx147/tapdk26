using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Fares and how passengers react to them. The market fare follows distance with a falling rate per km
    ///     (about $105 for 400 km, $160 for 1,200 km, $250 for 3,000 km and $415 for 7,000 km in 2007). With
    ///     realistic costs on, demand falls when a fare is above the market fare and rises when it is below
    ///     (business and first class mind less), and on shared routes the cheaper, better airline gets more of the
    ///     passengers. With the original costs the game's original fares and price behaviour are kept.
    /// </summary>
    public static class PricingService
    {
        public static bool Realistic => FlightCosts.Enabled;

        /// <summary>The market economy fare for a distance, in today's money.</summary>
        public static double MarketFare(double km)
        {
            if (!Realistic)
            {
                double price = km*GeneralHelpers.GetInflationPrice(0.0084);
                double minimum = GeneralHelpers.GetInflationPrice(18);
                return price < minimum ? minimum + price/4 : price;
            }
            double baseYear = 11
                              + 0.0105*Math.Min(km, 1000)
                              + 0.007*Math.Max(0, Math.Min(km, 3000) - 1000)
                              + 0.006*Math.Max(0, Math.Min(km, 7000) - 3000)
                              + 0.005*Math.Max(0, km - 7000);
            return GeneralHelpers.GetInflationPrice(baseYear);
        }

        public static double MarketFare(Airport a, Airport b, AirlinerClass.ClassType type)
        {
            return MarketFare(MathHelpers.GetDistance(a, b))*GeneralHelpers.ClassToPriceFactor(type);
        }

        /// <summary>How much passengers mind the price: economy most, first class least.</summary>
        public static double Elasticity(AirlinerClass.ClassType type)
        {
            switch (type)
            {
                case AirlinerClass.ClassType.FirstClass: return 0.5;
                case AirlinerClass.ClassType.BusinessClass: return 0.8;
                default: return 1.3;
            }
        }

        /// <summary>The share of the normal demand at a fare: 1 at the market fare, less above it, more below it.</summary>
        public static double DemandFactor(double fare, double market, AirlinerClass.ClassType type)
        {
            if (!Realistic || market <= 0) return 1;
            if (fare <= 0) return 2;
            return Math.Max(0.03, Math.Min(2.0, Math.Pow(fare/market, -Elasticity(type))));
        }

        public static double DemandFactor(PassengerRoute route, AirlinerClass.ClassType type)
        {
            var c = route.GetRouteAirlinerClass(type);
            if (c == null) return 1;
            return DemandFactor(c.FarePrice, MarketFare(route.Destination1, route.Destination2, type), type);
        }

        /// <summary>
        ///     On a route several airlines fly: this route's attractiveness (service per dollar) against the average of
        ///     all of them, between 0.65 and 1.5. With one airline it is 1.
        /// </summary>
        public static double CompetitionFactor(Route current, List<Route> routes, AirlinerClass.ClassType type)
        {
            var mine = Attractiveness(current, type);
            var all = routes.Distinct().Select(r => Attractiveness(r, type)).Where(v => v > 0).ToList();
            if (mine <= 0 || all.Count < 2) return 1;
            // a gentle effect: twice as attractive gives about 1.4 times the share
            return Math.Max(0.65, Math.Min(1.5, Math.Sqrt(mine/all.Average())));
        }

        private static double Attractiveness(Route r, AirlinerClass.ClassType type)
        {
            var pr = r as PassengerRoute;
            var c = pr?.GetRouteAirlinerClass(type);
            if (c == null || c.FarePrice <= 0) return 0;
            double service = Math.Sqrt(10 + Math.Max(0, pr.GetServiceLevel(type)));
            double reputation = AdvertisingService.ReputationFactor(r.Airline)*RatingsService.CustomerFactor(r.Airline);
            return service*reputation/c.FarePrice;
        }

        // ---- cargo ----

        /// <summary>
        ///     The market rate per unit of cargo. With realistic costs a fixed part for handling plus a rate per km (about
        ///     $53 for 500 km, $102 for 1,500 km and $175 for 3,000 km in 2007), set so a freighter 75% full makes about 12%
        ///     over any distance; with the original costs the game's original rate (a flat rate per km).
        /// </summary>
        public static double CargoMarketRate(double km)
        {
            if (!Realistic) return km*GeneralHelpers.GetInflationPrice(0.0081);
            return GeneralHelpers.GetInflationPrice(4 + 0.0070*km);
        }

        public static double CargoMarketRate(Airport a, Airport b)
        {
            return CargoMarketRate(MathHelpers.GetDistance(a, b));
        }

        /// <summary>How much shippers mind the price: a little less than economy passengers.</summary>
        public const double CargoElasticity = 1.2;

        /// <summary>The share of the normal cargo demand at a rate: 1 at the market rate, less above it, more below it.</summary>
        public static double CargoDemandFactor(double price, double market)
        {
            if (!Realistic || market <= 0) return 1;
            if (price <= 0) return 2;
            return Math.Max(0.03, Math.Min(2.0, Math.Pow(price/market, -CargoElasticity)));
        }

        /// <summary>A cargo (or combi) route's rate per unit, 0 for other routes.</summary>
        public static double CargoPrice(Route r)
        {
            if (r is CargoRoute c) return c.PricePerUnit;
            if (r is CombiRoute m) return m.PricePerUnit;
            return 0;
        }

        /// <summary>On a cargo route several airlines fly, the cheaper and better-known one gets more of the cargo (0.65 to 1.5).</summary>
        public static double CargoCompetitionFactor(Route current, List<Route> routes)
        {
            Func<Route, double> attract = r =>
                {
                    double p = CargoPrice(r);
                    return p <= 0 ? 0 : AdvertisingService.ReputationFactor(r.Airline)*RatingsService.CustomerFactor(r.Airline)/p;
                };
            double mine = attract(current);
            var all = routes.Distinct().Select(attract).Where(v => v > 0).ToList();
            if (mine <= 0 || all.Count < 2) return 1;
            return Math.Max(0.65, Math.Min(1.5, Math.Sqrt(mine/all.Average())));
        }

        /// <summary>
        ///     The rate per unit at which a flight of the route's freighter, at the given share of its space filled, covers
        ///     its flight costs and its share of what the aircraft costs to own (not salaries or rents).
        /// </summary>
        public static double CargoBreakEvenRate(Route route, double load)
        {
            var f = route.GetAirliners().FirstOrDefault(x => x.Airliner?.Type is AirlinerCargoType);
            if (f == null) return 0;
            var a = f.Airliner;
            var t = (AirlinerCargoType) a.Type;
            double km = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            double hours = km/Math.Max(100, t.CruisingSpeed) + 0.5;
            double cap = Math.Max(1, t.CargoSize);
            double units = cap*Math.Max(0.1, load);
            double fuelPrice = AirportHelpers.GetFuelPrice(route.Destination1);
            double fuel = fuelPrice*km*cap*a.FuelConsumption*0.55 + fuelPrice*a.FuelConsumption*units*km*0.45;
            double fees = AirportHelpers.GetLandingFee(route.Destination2, a) + 5*units;
            var parts = FlightCosts.Calculate(a, km, hours, 0);
            double fixedCost = fuel + fees + parts.Ground + parts.Maintenance + parts.EnRoute + parts.Crew + OwnershipPerFlight(f);
            // sales and overhead grow with the income; freighters pay half the added costs
            double share = FlightCosts.Enabled ? (FlightCosts.DistributionShare(route.Airline) + 0.07)*(FlightCosts.Level >= 2 ? 1.4 : 1)*0.5 : 0;
            return fixedCost/Math.Max(1, units*(1 - share));
        }

        // ---- the numbers for the route page ----

        public class RouteMonth
        {
            public double Income, Costs;
            public double Profit => Income + Costs;
        }

        public static RouteMonth LastMonth(Route route)
        {
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            var inv = route.Invoices;
            var m = new RouteMonth();
            foreach (Invoice.InvoiceType t in Enum.GetValues(typeof (Invoice.InvoiceType)))
            {
                if (t == Invoice.InvoiceType.Total) continue;
                double v = inv.GetAmount(t, last.Year, last.Month);
                if (v > 0) m.Income += v;
                else m.Costs += v;
            }
            return m;
        }

        /// <summary>
        ///     The economy fare at which a flight with the route's aircraft, at the given share of seats filled, covers
        ///     its flight costs (fuel, fees, crew, maintenance, handling, sales and overhead) and its share of what the
        ///     aircraft costs to own; not salaries or rents.
        /// </summary>
        public static double BreakEvenFare(PassengerRoute route, double load)
        {
            var f = route.GetAirliners().FirstOrDefault();
            if (f?.Airliner == null) return 0;
            var a = f.Airliner;
            double km = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            double hours = km/Math.Max(100, a.Type.CruisingSpeed) + 0.5;
            int seats = Math.Max(1, a.GetTotalSeatCapacity());
            double pax = seats*Math.Max(0.1, load);
            double fuelPrice = AirportHelpers.GetFuelPrice(route.Destination1);
            int maxSeats = a.Type is AirlinerPassengerType pt ? pt.MaxSeatingCapacity : seats;
            double fuel = fuelPrice*km*maxSeats*a.FuelConsumption*0.55 + fuelPrice*km*a.FuelConsumption*pax*0.45;
            double fees = AirportHelpers.GetLandingFee(route.Destination2, a) + 5*pax;
            double cabin = (a.Type is AirlinerPassengerType p2 ? p2.CabinCrew : 0)*hours*route.Airline.Fees.GetValue(Models.Airlines.FeeTypes.GetType("Cabin Wage"));
            // the share-of-sales costs grow with the fare: solve fare*pax = fixed + fare*pax*share
            var parts = FlightCosts.Calculate(a, km, hours, 0);
            double fixedCost = fuel + fees + cabin + parts.Ground + parts.Maintenance + parts.EnRoute + parts.Crew + OwnershipPerFlight(f);
            double share = FlightCosts.Enabled ? (FlightCosts.DistributionShare(route.Airline) + 0.07)*(FlightCosts.Level >= 2 ? 1.4 : 1) : 0;
            // business and first seats pay more: count them as several economy seats
            double weight = route.Classes.Sum(c => a.GetAirlinerClass(c.Type) == null ? 0 : a.GetAirlinerClass(c.Type).SeatingCapacity*GeneralHelpers.ClassToPriceFactor(c.Type));
            double economyEquivalents = weight > 0 ? weight*Math.Max(0.1, load) : pax;
            return fixedCost/Math.Max(1, economyEquivalents*(1 - share));
        }

        /// <summary>An owned aircraft's monthly ownership cost spread over the flights it makes in a month.</summary>
        public static double OwnershipPerFlight(FleetAirliner f)
        {
            double monthly = OwnershipService.MonthlyCost(f);
            if (monthly <= 0) return 0;
            int weekly;
            try { weekly = f.Routes.ToList().SelectMany(r => r.TimeTable.Entries.ToList()).Count(e => e.Airliner == f); }
            catch (Exception) { weekly = 0; }
            double perMonth = weekly > 0 ? weekly*4.33 : 60;
            return monthly/perMonth;
        }

        /// <summary>The share of seats the route filled in the last flights it flew (0 when unknown).</summary>
        public static double RecentLoad(PassengerRoute route)
        {
            double v;
            try { v = route.GetFillingDegree(); }
            catch (Exception) { v = 0; }
            return double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Max(0, Math.Min(1, v));
        }
    }
}
