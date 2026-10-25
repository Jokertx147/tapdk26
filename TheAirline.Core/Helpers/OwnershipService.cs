using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     What owning an aircraft costs with realistic costs: depreciation and the money tied up in it, 0.5% of its
    ///     current value a month (6% a year; a lease costs about 8.7% a year, so owning stays cheaper). It is paid
    ///     whether the aircraft flies or not, so idle aircraft cost money. Tough costs make it 40% more. Computer
    ///     airlines sell owned aircraft they have no use for.
    /// </summary>
    public static class OwnershipService
    {
        public const double MonthlyShare = 0.005;

        public static bool IsOwned(FleetAirliner f)
        {
            return f.Purchased == FleetAirliner.PurchasedType.Bought || f.Purchased == FleetAirliner.PurchasedType.BoughtDownPayment;
        }

        /// <summary>The month's ownership cost of an aircraft (0 when leased, not delivered yet, or with the original costs).</summary>
        public static double MonthlyCost(FleetAirliner f)
        {
            if (!FlightCosts.Enabled || f?.Airliner == null || !IsOwned(f)) return 0;
            if (f.Airliner.BuiltDate > GameObject.GetInstance().GameTime) return 0;
            double value = Math.Max(0, f.Airliner.GetPrice());
            return value*MonthlyShare*(FlightCosts.Level >= 2 ? 1.4 : 1.0);
        }

        /// <summary>Monthly: charges the airline for the aircraft it owns.</summary>
        public static double ProcessMonth(Airline airline)
        {
            if (!FlightCosts.Enabled) return 0;
            var now = GameObject.GetInstance().GameTime;
            double total = 0;
            foreach (var f in airline.Fleet.ToList())
            {
                double cost = MonthlyCost(f);
                if (cost <= 0) continue;
                total += cost;
                f.Data?.AddOperatingValue(new OperatingValue("Ownership", now.Year, now.Month, -cost));
            }
            if (total > 0) AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Rents, -total);
            return total;
        }

        /// <summary>How many idle aircraft a computer airline keeps in reserve.</summary>
        public static int Reserve(Airline airline)
        {
            return Math.Max(2, (int) Math.Round(airline.Fleet.Count*0.10));
        }

        /// <summary>
        ///     A computer airline with more idle owned aircraft than its reserve sells the oldest one, at 90% of its value.
        ///     Returns the aircraft sold, or null.
        /// </summary>
        public static FleetAirliner SellSurplus(Airline airline)
        {
            if (!FlightCosts.Enabled || airline == null || airline.IsHuman) return null;
            var now = GameObject.GetInstance().GameTime;
            List<FleetAirliner> idle;
            lock (airline.Fleet)
            {
                idle = airline.Fleet.Where(f => f.Airliner.BuiltDate <= now && !f.HasRoute && IsOwned(f)
                                                && f.Status == FleetAirliner.AirlinerStatus.Stopped).ToList();
            }
            if (idle.Count <= Reserve(airline)) return null;
            var sell = idle.OrderBy(f => f.Airliner.BuiltDate).First();
            double price = Math.Max(0, sell.Airliner.GetPrice()*0.9);
            lock (airline.Fleet)
            {
                airline.RemoveAirliner(sell);
            }
            foreach (var pilot in sell.Pilots.ToList()) sell.RemovePilot(pilot);
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, price);
            return sell;
        }
    }
}
