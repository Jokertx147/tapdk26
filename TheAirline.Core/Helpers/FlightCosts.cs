using System;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>The extra costs of one flight that the original game did not charge.</summary>
    public class FlightCostParts
    {
        public double Ground, Maintenance, EnRoute, Crew, Distribution, Overhead;
        public double Total => Ground + Maintenance + EnRoute + Crew + Distribution + Overhead;
    }

    /// <summary>
    ///     Realistic flight costs. Besides fuel, landing fees and catering, every flight now pays for ground handling,
    ///     line maintenance per flight hour, en-route air traffic charges, flight crew time (an aircraft flying all
    ///     day needs several crews, not the one the game hires), ticket sales and distribution, and overhead. All
    ///     amounts are in the game's base-year money and follow inflation. Options sets the level (original,
    ///     realistic, tough).
    /// </summary>
    public static class FlightCosts
    {
        public static readonly string[] LevelNames = { "Original", "Realistic", "Tough" };

        /// <summary>0 original game costs, 1 realistic, 2 tough (the added costs count 40% more).</summary>
        public static int Level => GameObject.GetInstance()?.CostLevel ?? 1;

        public static bool Enabled => Level > 0;

        private static double Factor => Level >= 2 ? 1.4 : 1.0;

        public static string Describe(int level)
        {
            switch (level)
            {
                case 0: return "Original: flights pay only fuel, landing fees, passenger taxes, catering and cabin crew. Almost every route makes money.";
                case 2: return "Tough: like Realistic, but the added costs are 40% higher. Only well-run routes make money.";
                default: return "Realistic: flights also pay ground handling, maintenance per flight hour, en-route charges, crew time, ticket sales (5 to 8%) and overhead (7%), and owned aircraft cost 0.5% of their value a month whether they fly or not. Short busy routes still earn well, long thin ones can lose.";
            }
        }

        /// <summary>The game's usual economy fare for a distance (as the route creator suggests it).</summary>
        public static double UsualFare(double km)
        {
            return PricingService.MarketFare(km);
        }

        /// <summary>Seats, or for cargo aircraft 1.5 "seats" per unit of cargo space (a 757 freighter is about 360).</summary>
        public static double Capacity(Airliner a)
        {
            if (a.Type is AirlinerCargoType c) return Math.Max(10, c.CargoSize*1.5);
            return Math.Max(4, a.GetTotalSeatCapacity());
        }

        /// <summary>The share of ticket income paid for booking systems, agents and card fees.</summary>
        public static double DistributionShare(Airline airline)
        {
            bool online = airline.Facilities != null && airline.Facilities.Any(f => f != null && f.Uid == "105");
            return online ? 0.05 : 0.08;
        }

        public static FlightCostParts Calculate(Airliner airliner, double km, double hours, double tickets)
        {
            var p = new FlightCostParts();
            if (!Enabled || airliner == null) return p;
            double cap = Capacity(airliner);
            hours = Math.Max(0.3, hours);
            // base-year amounts (about 1/7 of 2007 dollars): a 150 seat jet pays about $1,050 handling in 2007
            p.Ground = GeneralHelpers.GetInflationPrice(10 + 1.1*cap);
            // line maintenance: a part per flight hour plus a part that grows with the aircraft's price
            p.Maintenance = hours*(GeneralHelpers.GetInflationPrice(4 + 0.75*cap) + 0.000008*airliner.Type.Price);
            // en-route charges per km, more for bigger aircraft
            p.EnRoute = km*GeneralHelpers.GetInflationPrice(0.012 + 0.00045*cap);
            // the extra crews needed to keep the aircraft flying (the hired pilots are paid separately)
            int crew = Math.Max(1, airliner.Type.CockpitCrew) + (airliner.Type is AirlinerPassengerType pt ? pt.CabinCrew : 0);
            p.Crew = hours*crew*GeneralHelpers.GetInflationPrice(18);
            p.Distribution = Math.Max(0, tickets)*DistributionShare(airliner.Airline);
            // offices, IT, marketing and everything else that isn't a flight
            p.Overhead = Math.Max(0, tickets)*0.07;
            double f = Factor;
            // the game pays little for cargo: freighters (no cabin, cheaper handling per load) pay half
            if (airliner.Type is AirlinerCargoType) f *= 0.5;
            p.Ground *= f;
            p.Maintenance *= f;
            p.EnRoute *= f;
            p.Crew *= f;
            p.Distribution *= f;
            p.Overhead *= f;
            return p;
        }

        /// <summary>Charges the extra costs of a landed flight to the airline, the route and the aircraft.</summary>
        public static void Charge(FleetAirliner airliner, Route route, double km, double hours, double tickets)
        {
            if (!Enabled) return;
            var p = Calculate(airliner.Airliner, km, hours, tickets);
            if (p.Total <= 0 || double.IsNaN(p.Total)) return;
            var airline = airliner.Airliner.Airline;
            var now = GameObject.GetInstance().GameTime;

            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.FlightExpenses, -(p.Ground + p.EnRoute));
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Maintenances, -p.Maintenance);
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Wages, -p.Crew);
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.AirlineExpenses, -(p.Distribution + p.Overhead));

            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.FlightExpenses, -(p.Ground + p.EnRoute + p.Distribution + p.Overhead)));
            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.Maintenances, -p.Maintenance));
            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.Wages, -p.Crew));

            var d = airliner.Data;
            if (d != null)
            {
                d.AddOperatingValue(new OperatingValue("Ground handling", now.Year, now.Month, -p.Ground));
                d.AddOperatingValue(new OperatingValue("Maintenance", now.Year, now.Month, -p.Maintenance));
                d.AddOperatingValue(new OperatingValue("En-route charges", now.Year, now.Month, -p.EnRoute));
                d.AddOperatingValue(new OperatingValue("Crew", now.Year, now.Month, -p.Crew));
                d.AddOperatingValue(new OperatingValue("Ticket sales", now.Year, now.Month, -p.Distribution));
                d.AddOperatingValue(new OperatingValue("Overhead", now.Year, now.Month, -p.Overhead));
            }
            airliner.Statistics?.AddStatisticsValue(now.Year, Models.General.Statistics.StatisticsTypes.GetStatisticsType("Airliner_Income"), -p.Total);
        }
    }
}
