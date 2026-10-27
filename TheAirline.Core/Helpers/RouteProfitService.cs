using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>A route's money and traffic, as the route profit report shows it.</summary>
    public class RouteFigures
    {
        public Route Route { get; set; }
        public bool IsCargo { get; set; }
        public double LastMonth { get; set; }
        public double ThisMonth { get; set; }
        public double Last12 { get; set; }

        /// <summary>Months with figures in the last 12 (a new route has fewer).</summary>
        public int Months { get; set; }

        public double Load { get; set; }
        public int CarriedLastMonth { get; set; }
        public int WeeklyFlights { get; set; }

        /// <summary>The route's price over the market price (economy fare, or cargo rate); 0 when unknown.</summary>
        public double FareRatio { get; set; }

        public double ProfitPerFlight { get; set; }
        public string Aircraft { get; set; }
        public string Advice { get; set; }
        public bool Losing => (Months > 0 ? Last12 : ThisMonth) < 0;
    }

    /// <summary>
    ///     Profit per route: what each route earned last month, this month so far and in the last 12 months, with its
    ///     load, the fare against the market and a hint on what to do. The figures are the route's own income and costs
    ///     (tickets, in-flight sales, fuel and handling, crew, maintenance, fees), before what the aircraft themselves
    ///     cost to own or lease.
    /// </summary>
    public static class RouteProfitService
    {
        private static readonly Invoice.InvoiceType[] Types =
            Enum.GetValues(typeof (Invoice.InvoiceType)).Cast<Invoice.InvoiceType>().Where(t => t != Invoice.InvoiceType.Total).ToArray();

        /// <summary>The route's amount of one type in a month (stopover legs included).</summary>
        public static double Month(Route route, Invoice.InvoiceType type, int year, int month)
        {
            double v = route.GetInvoices().GetAmount(type, year, month);
            foreach (var stop in route.Stopovers.ToList())
                foreach (Route leg in stop.Legs.ToList())
                    v += leg.GetInvoices().GetAmount(type, year, month);
            return v;
        }

        /// <summary>The route's profit in a month.</summary>
        public static double Month(Route route, int year, int month)
        {
            return Types.Sum(t => Month(route, t, year, month));
        }

        /// <summary>Income (positive amounts) and costs (negative amounts) of a month.</summary>
        public static void IncomeAndCosts(Route route, int year, int month, out double income, out double costs)
        {
            income = 0;
            costs = 0;
            foreach (var t in Types)
            {
                double v = Month(route, t, year, month);
                if (v > 0) income += v;
                else costs += v;
            }
        }

        /// <summary>The month's amounts by kind, largest first ("Tickets +120,000").</summary>
        public static List<Tuple<string, double>> Breakdown(Route route, int year, int month)
        {
            return Types.Select(t => Tuple.Create(Name(t), Month(route, t, year, month))).Where(x => Math.Abs(x.Item2) >= 0.5)
                        .OrderByDescending(x => Math.Abs(x.Item2)).ToList();
        }

        private static string Name(Invoice.InvoiceType t)
        {
            switch (t)
            {
                case Invoice.InvoiceType.Tickets: return "Tickets and cargo";
                case Invoice.InvoiceType.OnFlightIncome: return "In-flight sales";
                case Invoice.InvoiceType.FlightExpenses: return "Fuel, handling and overhead";
                case Invoice.InvoiceType.Maintenances: return "Maintenance";
                case Invoice.InvoiceType.Wages: return "Crew";
                case Invoice.InvoiceType.Fees: return "Fees";
                case Invoice.InvoiceType.AirlineExpenses: return "Other expenses";
                default: return t.ToString();
            }
        }

        public static RouteFigures Figures(Route route)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            var f = new RouteFigures {Route = route, IsCargo = route is CargoRoute};
            f.ThisMonth = Month(route, now.Year, now.Month);
            DateTime last = now.AddMonths(-1);
            f.LastMonth = Month(route, last.Year, last.Month);
            DateTime started = route.StartDate == default(DateTime) ? DateTime.MinValue : route.StartDate;
            for (int i = 1; i <= 12; i++)
            {
                DateTime d = now.AddMonths(-i);
                if (new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month)) < started) break;
                f.Last12 += Month(route, d.Year, d.Month);
                f.Months++;
            }

            f.Load = ConnectionService.MonthLoad(route);
            int connecting, all;
            ConnectionService.LastMonth(route, out connecting, out all);
            f.CarriedLastMonth = all;
            f.WeeklyFlights = route.TimeTable.Entries.Count;
            double flightsLastMonth = f.WeeklyFlights*DateTime.DaysInMonth(last.Year, last.Month)/7.0;
            f.ProfitPerFlight = flightsLastMonth > 0 && f.Months > 0 ? f.LastMonth/flightsLastMonth : 0;

            try
            {
                if (route is PassengerRoute pr)
                {
                    double market = PricingService.MarketFare(route.Destination1, route.Destination2, AirlinerClass.ClassType.EconomyClass);
                    f.FareRatio = market > 0 ? AIPricingService.EconomyFare(pr)/market : 0;
                }
                else if (route is CargoRoute)
                {
                    double market = PricingService.CargoMarketRate(route.Destination1, route.Destination2);
                    f.FareRatio = market > 0 ? PricingService.CargoPrice(route)/market : 0;
                }
            }
            catch (Exception)
            {
                f.FareRatio = 0;
            }

            var aircraft = route.GetAirliners();
            f.Aircraft = aircraft.Count == 0 ? "no aircraft" : string.Join(", ", aircraft.Select(a => a.Name + " (" + a.Airliner.Type.Name + ")"));
            f.Advice = Advice(f, aircraft.Count, now - started);
            return f;
        }

        private static string Advice(RouteFigures f, int aircraft, TimeSpan age)
        {
            string fare = f.IsCargo ? "rate" : "fare";
            if (aircraft == 0) return "No aircraft flies it: put one on it, or delete the route.";
            if (age.TotalDays < 31) return "New route: the numbers are still building up.";
            bool losing = (f.Months > 0 ? f.LastMonth : f.ThisMonth) < 0;
            if (losing)
            {
                if (f.Load > 0 && f.Load < 0.5 && f.FareRatio > 1.1) return "Losing money with few " + (f.IsCargo ? "loads" : "passengers") + " and a " + fare + " above the market: lower the " + fare + ".";
                if (f.Load > 0 && f.Load < 0.5) return "Losing money with few " + (f.IsCargo ? "loads" : "passengers") + ": a smaller aircraft or fewer flights would cost less.";
                if (f.Load >= 0.9) return "Full but losing money: raise the " + fare + ".";
                if (f.FareRatio > 0 && f.FareRatio < 0.9) return "Losing money with a " + fare + " below the market: raise the " + fare + ".";
                return "Costs more than it earns: check the " + fare + ", or fly it with a cheaper aircraft.";
            }
            if (f.Load >= 0.92) return "Nearly full: room for a higher " + fare + " or a bigger aircraft.";
            if (f.FareRatio > 0 && f.FareRatio < 0.85) return "The " + fare + " is well below the market: you could ask more.";
            if (f.Load > 0 && f.Load < 0.5) return "Profitable, but more than half the seats are empty: a smaller aircraft could earn more.";
            return "Doing fine.";
        }

        /// <summary>The figures of all the airline's routes.</summary>
        public static List<RouteFigures> All(Models.Airlines.Airline airline)
        {
            return airline.Routes.ToList().Select(Figures).ToList();
        }
    }
}
