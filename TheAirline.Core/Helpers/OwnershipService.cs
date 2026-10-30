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
    ///     airlines sell owned aircraft they have no use for, slowly (one a week) and at a used-market price (75%); an
    ///     aircraft that has found no route for four months goes faster (up to two a day): it is the wrong aircraft for the
    ///     airline's network.
    /// </summary>
    public static class OwnershipService
    {
        public const double MonthlyShare = 0.005;

        /// <summary>What a sold aircraft fetches, as a share of its value, and how often an airline can sell one.</summary>
        public const double SalePriceShare = 0.75;
        public const int DaysBetweenSales = 7;

        private static readonly WeakDates<Airline> LastSale = new WeakDates<Airline>();

        /// <summary>An aircraft idle this long (owned, beyond the reserve) is sold without waiting for the weekly sale.</summary>
        public const int LongIdleDays = 120;
        public const int LongIdleSalesPerDay = 2;

        // since when each aircraft has been idle (not saved: after loading a game the count starts again)
        private static readonly WeakDates<FleetAirliner> IdleSince = new WeakDates<FleetAirliner>();

        /// <summary>Counter for the tests: aircraft sold after a long time idle.</summary>
        public static int LongIdleSold;

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
        ///     A computer airline with more idle owned aircraft than its reserve sells the oldest one at 75% of its value,
        ///     at most one a week. Returns the aircraft sold, or null.
        /// </summary>
        public static FleetAirliner SellSurplus(Airline airline)
        {
            if (!FlightCosts.Enabled || airline == null || airline.IsHuman) return null;
            var now = GameObject.GetInstance().GameTime;
            List<FleetAirliner> idle;
            lock (airline.Fleet)
            {
                idle = airline.Fleet.Where(f => f.Airliner.BuiltDate <= now && !f.HasRoute && IsOwned(f) && f.UsableBy(airline)
                                                && (f.Status == FleetAirliner.AirlinerStatus.Stopped || IdleAfterRoute(f, now))).ToList();
            }
            int reserve = Reserve(airline);

            // keep count of how long each aircraft has been idle
            List<FleetAirliner> longIdle;
            lock (IdleSince)
            {
                foreach (var f in idle)
                {
                    DateTime since;
                    if (!IdleSince.TryGetValue(f, out since)) IdleSince[f] = now;
                }
                List<FleetAirliner> flying;
                lock (airline.Fleet) flying = airline.Fleet.Where(f => f.HasRoute).ToList();
                foreach (var f in flying) IdleSince.Remove(f);
                longIdle = idle.Where(f => { DateTime since; return IdleSince.TryGetValue(f, out since) && (now - since).TotalDays >= LongIdleDays; })
                               .OrderBy(f => f.Airliner.BuiltDate).ToList();
            }
            if (idle.Count <= reserve) return null;

            // the wrong aircraft for the network: sold without waiting for the weekly sale
            FleetAirliner soldLong = null;
            for (int i = 0; i < Math.Min(LongIdleSalesPerDay, Math.Min(longIdle.Count, idle.Count - reserve)); i++)
            {
                soldLong = Sell(airline, longIdle[i], now);
                System.Threading.Interlocked.Increment(ref LongIdleSold);
            }
            if (soldLong != null) return soldLong;

            lock (LastSale)
            {
                DateTime last;
                if (LastSale.TryGetValue(airline, out last) && (now - last).TotalDays < DaysBetweenSales) return null;
                LastSale[airline] = now;
            }
            return Sell(airline, idle.OrderBy(f => f.Airliner.BuiltDate).First(), now);
        }

        // an aircraft whose route was given up keeps its "to route start" status; once it is on the ground it is idle
        private static bool IdleAfterRoute(FleetAirliner f, DateTime now)
        {
            if (f.Status != FleetAirliner.AirlinerStatus.ToRouteStart || f.Routes.Count > 0) return false;
            var flight = f.CurrentFlight;
            try
            {
                return flight == null || flight.Entry == null || flight.GetExpectedLandingTime() <= now;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static FleetAirliner Sell(Airline airline, FleetAirliner sell, DateTime now)
        {
            double price = Math.Max(0, sell.Airliner.GetPrice()*SalePriceShare);
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
