using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>Selling and leasing out the airline's own aircraft: lease an idle aircraft to another airline for rent, or list it for sale at a price.</summary>
    public static class AircraftDealService
    {
        public static readonly int[] LeaseMonths = {12, 36, 60};
        public static readonly double[] SalePrices = {0.9, 1.0, 1.15};
        // Random.Shared is safe to use from the parallel airline threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        public static bool IsLeasedOut(FleetAirliner f)
        {
            return f.LeaseOutTo != null && f.LeaseOutUntil > GameObject.GetInstance().GameTime;
        }

        public static bool IsListed(FleetAirliner f)
        {
            return f.SaleAsk > 0;
        }

        /// <summary>Why the aircraft cannot be leased out or listed, or null when it can.</summary>
        public static string GetBlocker(FleetAirliner f)
        {
            GameObject game = GameObject.GetInstance();
            if (f.Purchased != FleetAirliner.PurchasedType.Bought) return "only an aircraft you own outright can be leased out or sold this way";
            if (f.Airliner.BuiltDate > game.GameTime) return "not delivered yet";
            if (f.Routes.Count > 0) return "it still has routes";
            if (f.Status != FleetAirliner.AirlinerStatus.Stopped) return "it is not stopped";
            if (CharterService.IsOnCharter(f)) return "it is out on a charter";
            if (IsLeasedOut(f)) return "it is already leased out";
            if (FerryService.IsFerrying(f)) return "it is on a ferry flight";
            if (IsListed(f)) return "it is already listed for sale";
            if (MaintenanceService.IsGrounded(f)) return "it is grounded";
            if (MaintenanceService.IsCovering(f) || MaintenanceService.IsCovered(f)) return "it is tied up in a maintenance cover";
            return null;
        }

        public static double RentFor(FleetAirliner f, int months)
        {
            double factor = months >= 60 ? 0.9 : months >= 36 ? 0.95 : 1.0;
            return Math.Round(f.Airliner.GetLeasingPrice()*factor);
        }

        public static double EarlyEndFee(FleetAirliner f)
        {
            return f.LeaseOutRent*3;
        }

        private static Airline PickLessee(Airline owner, FleetAirliner f)
        {
            var candidates = Airlines.GetAllAirlines().Where(a => a != owner && !a.IsHuman && a.Profile.Country != null && a.Money > 0).ToList();
            if (candidates.Count == 0) return null;
            return candidates[Rnd.Next(candidates.Count)];
        }

        public static bool LeaseOut(Airline airline, FleetAirliner f, int months, out string message)
        {
            GameObject game = GameObject.GetInstance();
            string blocker = GetBlocker(f);
            if (blocker != null)
            {
                message = f.Name + ": " + blocker + ".";
                return false;
            }
            Airline lessee = PickLessee(airline, f);
            if (lessee == null)
            {
                message = "No airline wants it right now.";
                return false;
            }
            foreach (var pilot in f.Pilots.ToList()) f.RemovePilot(pilot);
            f.LeaseOutTo = lessee;
            f.LeaseOutStart = game.GameTime.Date;
            f.LeaseOutUntil = game.GameTime.Date.AddMonths(months);
            f.LeaseOutRent = RentFor(f, months);
            message = f.Name + " is leased to " + lessee.Profile.Name + " for " + months + " months at " + f.LeaseOutRent.ToString("N0") + " a month. The first rent comes on the 1st.";
            return true;
        }

        /// <summary>Ends the lease: early (pay a fee) or when it runs out. The aircraft comes back a little worn.</summary>
        public static string EndLease(Airline airline, FleetAirliner f, bool early)
        {
            GameObject game = GameObject.GetInstance();
            if (f.LeaseOutTo == null) return null;
            string lessee = f.LeaseOutTo.Profile.Name;
            double months = Math.Max(0, (game.GameTime.Date - f.LeaseOutStart).TotalDays/30.0);
            f.Airliner.Condition = Math.Max(40, f.Airliner.Condition - months*0.12);
            f.Airliner.Flown += months*25000;
            double fee = 0;
            if (early)
            {
                fee = EarlyEndFee(f);
                AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Rents, -fee);
            }
            f.LeaseOutTo = null;
            f.LeaseOutUntil = DateTime.MinValue;
            f.LeaseOutRent = 0;
            return f.Name + " is back from " + lessee + (early ? ", the early end cost " + fee.ToString("N0") + "." : ".");
        }

        public static bool List(FleetAirliner f, double ratio, out string message)
        {
            string blocker = GetBlocker(f);
            if (blocker != null)
            {
                message = f.Name + ": " + blocker + ".";
                return false;
            }
            f.SaleAsk = Math.Round(f.Airliner.GetPrice()*ratio);
            f.SaleSince = GameObject.GetInstance().GameTime.Date;
            message = f.Name + " is for sale at " + f.SaleAsk.ToString("N0") + ". A buyer may come any day.";
            return true;
        }

        public static void Withdraw(FleetAirliner f)
        {
            f.SaleAsk = 0;
        }

        /// <summary>Chance per day that a buyer takes it at this price.</summary>
        public static double BuyerChance(FleetAirliner f)
        {
            double value = Math.Max(1, f.Airliner.GetPrice());
            double ratio = f.SaleAsk/value;
            return Math.Max(0.005, Math.Min(0.12, 0.04 + (1.0 - ratio)*0.4));
        }

        public static List<string> ProcessDaily(Airline airline)
        {
            GameObject game = GameObject.GetInstance();
            var news = new List<string>();
            foreach (FleetAirliner f in airline.Fleet.ToList())
            {
                if (f.LeaseOutTo != null)
                {
                    if (game.GameTime.Day == 1 && f.LeaseOutStart < game.GameTime.Date)
                    {
                        AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Rents, f.LeaseOutRent);
                    }
                    if (f.LeaseOutUntil <= game.GameTime)
                    {
                        news.Add(EndLease(airline, f, false));
                    }
                }
                else if (f.SaleAsk > 0)
                {
                    if (GetBlocker0(f) != null)
                    {
                        f.SaleAsk = 0;
                        continue;
                    }
                    if (Rnd.NextDouble() < BuyerChance(f))
                    {
                        double price = f.SaleAsk;
                        airline.RemoveAirliner(f);
                        AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Purchases, price);
                        foreach (var pilot in f.Pilots.ToList()) f.RemovePilot(pilot);
                        news.Add(f.Name + " (" + f.Airliner.Type.Name + ") sold for " + price.ToString("N0") + " after " + (int) (game.GameTime.Date - f.SaleSince).TotalDays + " days.");
                        f.SaleAsk = 0;
                    }
                }
            }
            if (news.Count > 0 && airline == game.HumanAirline)
            {
                game.NewsBox.AddNews(new News(News.NewsType.AirlineNews, game.GameTime, "Aircraft deals", string.Join("\n", news)));
            }
            return news;
        }

        //a listed aircraft that got routes, grounded or sent away is taken off the market
        private static string GetBlocker0(FleetAirliner f)
        {
            if (f.Routes.Count > 0) return "routes";
            if (f.Status != FleetAirliner.AirlinerStatus.Stopped) return "status";
            if (CharterService.IsOnCharter(f)) return "charter";
            return null;
        }
    }
}
