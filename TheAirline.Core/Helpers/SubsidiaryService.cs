using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airlines.Subsidiary;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     The player's subsidiary airlines. They fly under their own name with their own money and are run by their
    ///     own management (the computer); the player starts them, moves money in and out, sells them or folds them
    ///     back into the main airline.
    /// </summary>
    public static class SubsidiaryService
    {
        public const int MaxSubsidiaries = 5;

        public static double MinStartMoney => GeneralHelpers.GetInflationPrice(2000000);

        public static List<SubsidiaryAirline> Mine(Airline airline)
        {
            return airline.Subsidiaries.ToList();
        }

        public static string GetCreateBlocker(Airline parent, string name, string iata, Airport homebase, double money)
        {
            if (parent.Subsidiaries.Count >= MaxSubsidiaries) return "You can have up to " + MaxSubsidiaries + " subsidiaries.";
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return "Give it a name.";
            if (Airlines.GetAllAirlines().Any(a => string.Equals(a.Profile.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return "Another airline is already called " + name.Trim() + ".";
            string code = (iata ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(code, "^[A-Z0-9]{2}$")) return "The airline code must be 2 letters or digits, like ZX.";
            if (Airlines.GetAllAirlines().Any(a => a.Profile.IATACode == code)) return "The code " + code + " is already used by another airline.";
            if (homebase == null) return "Pick a home airport.";
            if (money < MinStartMoney) return "Give it at least " + Money(MinStartMoney) + " to start with.";
            if (money > parent.Money) return "You don't have " + Money(money) + ".";
            return null;
        }

        public static SubsidiaryAirline Create(
            Airline parent, string name, string iata, Airport homebase, double money,
            Airline.AirlineMentality mentality, AirlineFocus market, Route.RouteType focus, out string message)
        {
            message = GetCreateBlocker(parent, name, iata, homebase, money);
            if (message != null) return null;
            var sub = AirlineHelpers.CreateSubsidiaryAirline(parent, money, name.Trim(), iata.Trim().ToUpperInvariant(), mentality, market, focus, homebase);
            sub.AutoManaged = true;
            sub.Holdings = new List<ShareHolding> { new ShareHolding(parent, SharesService.StartShares, money) };
            if (!sub.Airports.Contains(homebase)) sub.AddAirport(homebase);
            message = string.Format(
                "{0} ({1}) is flying from {2} with {3}. Its management will buy aircraft and open routes.",
                sub.Profile.Name, sub.Profile.IATACode, homebase.Profile.IATACode, Money(money));
            return sub;
        }

        /// <summary>Moves money from the parent to the subsidiary (positive) or back (negative).</summary>
        public static bool Transfer(Airline parent, SubsidiaryAirline sub, double amount, out string message)
        {
            var now = GameObject.GetInstance().GameTime;
            if (amount > 0)
            {
                if (amount > parent.Money)
                {
                    message = "You don't have " + Money(amount) + ".";
                    return false;
                }
                AirlineHelpers.AddAirlineInvoice(parent, now, Invoice.InvoiceType.AirlineExpenses, -amount);
                AirlineHelpers.AddAirlineInvoice(sub, now, Invoice.InvoiceType.AirlineExpenses, amount);
                message = Money(amount) + " moved to " + sub.Profile.Name + ".";
                return true;
            }
            double back = -amount;
            if (back <= 0 || back > sub.Money)
            {
                message = sub.Profile.Name + " has " + Money(Math.Max(0, sub.Money)) + ".";
                return false;
            }
            AirlineHelpers.AddAirlineInvoice(sub, now, Invoice.InvoiceType.AirlineExpenses, -back);
            AirlineHelpers.AddAirlineInvoice(parent, now, Invoice.InvoiceType.AirlineExpenses, back);
            message = Money(back) + " moved from " + sub.Profile.Name + " to your airline.";
            return true;
        }

        /// <summary>What a buyer pays for the subsidiary: 90% of its value.</summary>
        public static double SalePrice(SubsidiaryAirline sub)
        {
            return Math.Max(0, sub.GetValue()*1000000.0*0.9);
        }

        public static void Sell(Airline parent, SubsidiaryAirline sub, out string message)
        {
            double price = SalePrice(sub);
            AirlineHelpers.MakeSubsidiaryAirlineIndependent(sub);
            sub.Holdings = new List<ShareHolding>();
            AirlineHelpers.AddAirlineInvoice(parent, GameObject.GetInstance().GameTime, Invoice.InvoiceType.AirlineExpenses, price);
            message = sub.Profile.Name + " was sold for " + Money(price) + " and now flies on its own.";
        }

        /// <summary>Folds the subsidiary into the main airline: aircraft, gates, airports and money come over.</summary>
        public static bool FoldIn(Airline parent, SubsidiaryAirline sub, out string message)
        {
            message = AirlineTypeRules.MergeBlocker(parent, sub);
            if (message != null) return false;
            string name = sub.Profile.Name;
            int fleet = sub.Fleet.Count, routes = sub.Routes.Count;
            double money = sub.Money;
            foreach (var route in sub.Routes.ToList())
            {
                route.Airline = parent;
                sub.RemoveRoute(route);
                parent.AddRoute(route);
            }
            foreach (var pilot in sub.Pilots.ToList())
            {
                sub.RemovePilot(pilot);
                pilot.Airline = parent;
                parent.AddPilot(pilot);
            }
            AirlineHelpers.CloseSubsidiaryAirline(sub);
            message = string.Format("{0} is now part of your airline: {1} aircraft, {2} route(s) and {3} came over.", name, fleet, routes, Money(money));
            return true;
        }

        public static double LastMonthProfit(Airline a)
        {
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return Enum.GetValues(typeof (Invoice.InvoiceType)).Cast<Invoice.InvoiceType>()
                       .Where(t => t != Invoice.InvoiceType.AirlineExpenses)
                       .Sum(t => a.GetInvoicesAmountMonth(last.Year, last.Month, t));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }
    }
}
