using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>How the game shows a date in its messages; the screens can replace it with their own format.</summary>
    public static class DateDisplay
    {
        public static Func<DateTime, string> Formatter = d => d.ToShortDateString();

        public static string Format(DateTime d)
        {
            return Formatter(d);
        }
    }

    public enum MaintenanceCheck
    {
        A,
        B,
        C,
        D
    }

    /// <summary>Maintenance checks, wear and breakdowns for an airline's fleet.</summary>
    public static class MaintenanceService
    {
        public static double GetCost(FleetAirliner f, MaintenanceCheck c)
        {
            double v = f.Airliner.GetValue();
            switch (c)
            {
                case MaintenanceCheck.A: return v*0.01 + 2000;
                case MaintenanceCheck.B: return v*0.02 + 4500;
                case MaintenanceCheck.C: return v*0.025 + 156000;
                default: return v*0.03 + 1200000;
            }
        }

        public static int MinGain(MaintenanceCheck c)
        {
            return c == MaintenanceCheck.A ? 3 : c == MaintenanceCheck.B ? 12 : c == MaintenanceCheck.C ? 20 : 35;
        }

        public static int MaxGain(MaintenanceCheck c)
        {
            return c == MaintenanceCheck.A ? 10 : c == MaintenanceCheck.B ? 20 : c == MaintenanceCheck.C ? 30 : 50;
        }

        /// <summary>Days the aircraft is grounded while the check is done.</summary>
        public static int GetDowntimeDays(FleetAirliner f, MaintenanceCheck c)
        {
            double worn = 100 - f.Airliner.Condition;
            if (worn < 0) worn = 0;
            switch (c)
            {
                case MaintenanceCheck.A: return 0;
                case MaintenanceCheck.B: return 1;
                case MaintenanceCheck.C: return 7 + (int) (worn/10);
                default: return 21 + (int) (worn/4);
            }
        }

        /// <summary>Days between two checks. A and B are stored in days, C and D in months; unset uses defaults.</summary>
        public static int GetIntervalDays(FleetAirliner f, MaintenanceCheck c)
        {
            switch (c)
            {
                case MaintenanceCheck.A: return f.AMaintenanceInterval > 0 ? f.AMaintenanceInterval : 90;
                case MaintenanceCheck.B: return f.BMaintenanceInterval > 0 ? f.BMaintenanceInterval : 270;
                case MaintenanceCheck.C: return f.CMaintenanceInterval > 0 ? f.CMaintenanceInterval*30 : 540;
                default: return f.DMaintenanceInterval > 0 ? f.DMaintenanceInterval*30 : 1800;
            }
        }

        public static void SetIntervals(FleetAirliner f, int aDays, int bDays, int cMonths, int dMonths)
        {
            f.AMaintenanceInterval = Math.Max(30, Math.Min(365, aDays));
            f.BMaintenanceInterval = Math.Max(90, Math.Min(730, bDays));
            f.CMaintenanceInterval = Math.Max(6, Math.Min(48, cMonths));
            f.DMaintenanceInterval = Math.Max(24, Math.Min(120, dMonths));
        }

        public static DateTime GetLastDate(FleetAirliner f, MaintenanceCheck c)
        {
            DateTime last;
            switch (c)
            {
                case MaintenanceCheck.A: last = f.LastAMaintenance; break;
                case MaintenanceCheck.B: last = f.LastBMaintenance; break;
                case MaintenanceCheck.C: last = f.LastCMaintenance; break;
                default: last = f.LastDMaintenance; break;
            }
            //a check done before we bought it does not count
            return last < f.PurchasedDate ? f.PurchasedDate : last;
        }

        public static DateTime GetDueDate(FleetAirliner f, MaintenanceCheck c)
        {
            return GetLastDate(f, c).AddDays(GetIntervalDays(f, c));
        }

        public static bool IsOverdue(FleetAirliner f, MaintenanceCheck c)
        {
            return GetDueDate(f, c).Date < GameObject.GetInstance().GameTime.Date;
        }

        public static bool IsGrounded(FleetAirliner f)
        {
            return f.GroundedToDate > GameObject.GetInstance().GameTime;
        }

        public static string GetConditionText(double condition)
        {
            if (condition >= 90) return "Excellent";
            if (condition >= 75) return "Good";
            if (condition >= 60) return "Fair";
            if (condition >= 40) return "Poor";
            return "Critical";
        }

        /// <summary>Does a check now. Returns false with a message if it is not possible.</summary>
        public static bool PerformCheck(FleetAirliner f, MaintenanceCheck c, out string message)
        {
            GameObject game = GameObject.GetInstance();
            if (f.Airliner.BuiltDate > game.GameTime)
            {
                message = "The aircraft has not been delivered yet.";
                return false;
            }
            if (IsGrounded(f))
            {
                message = "The aircraft is already grounded until " + DateDisplay.Format(f.GroundedToDate) + ".";
                return false;
            }
            double cost = GetCost(f, c);
            if (f.Airliner.Airline == game.HumanAirline && game.HumanMoney < cost)
            {
                message = "Not enough money: the " + c + " check costs " + cost.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + ".";
                return false;
            }

            AirlineHelpers.AddAirlineInvoice(f.Airliner.Airline, game.GameTime, Invoice.InvoiceType.Maintenances, -cost);
            var rnd = new Random();
            double before = f.Airliner.Condition;
            f.Airliner.Condition = Math.Min(100, f.Airliner.Condition + rnd.Next(MinGain(c), MaxGain(c) + 1));
            int days = GetDowntimeDays(f, c);
            switch (c)
            {
                case MaintenanceCheck.A: f.LastAMaintenance = game.GameTime; break;
                case MaintenanceCheck.B: f.LastBMaintenance = game.GameTime; break;
                case MaintenanceCheck.C: f.LastCMaintenance = game.GameTime; break;
                default: f.LastDMaintenance = game.GameTime; break;
            }
            if (days > 0)
            {
                f.GroundedToDate = game.GameTime.AddDays(days);
            }
            f.MaintenanceHistory.Add(new Invoice(game.GameTime, Invoice.InvoiceType.Maintenances, -cost), c.ToString());

            message = c + " check done for " + f.Name + ": condition " + before.ToString("0") + "% -> " + f.Airliner.Condition.ToString("0") + "%, "
                      + cost.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + (days > 0 ? ", grounded for " + days + " days" : "") + ".";
            return true;
        }

        /// <summary>
        ///     Daily: does the checks that are due on aircraft set to automatic maintenance. A and B first, and only one check that
        ///     grounds the aircraft at a time. It keeps a cash reserve, so the checks never take the airline to its last dollar.
        /// </summary>
        public static List<string> ProcessAuto(Airline airline)
        {
            GameObject game = GameObject.GetInstance();
            var done = new List<string>();
            var skipped = new List<string>();
            foreach (FleetAirliner f in airline.Fleet.ToList())
            {
                if (!f.AutoMaintenance || f.Airliner.BuiltDate > game.GameTime) continue;

                foreach (MaintenanceCheck c in new[] {MaintenanceCheck.A, MaintenanceCheck.B, MaintenanceCheck.C, MaintenanceCheck.D})
                {
                    if (IsGrounded(f)) break;
                    if (GetDueDate(f, c).Date > game.GameTime.Date) continue;

                    double cost = GetCost(f, c);
                    if (airline.Money < cost*2)
                    {
                        if (!skipped.Contains(f.Name)) skipped.Add(f.Name);
                        break;
                    }

                    string message;
                    if (PerformCheck(f, c, out message))
                    {
                        done.Add(message);
                    }
                }
            }

            if (airline == game.HumanAirline)
            {
                if (done.Count > 0)
                {
                    game.NewsBox.AddNews(new News(News.NewsType.AirlineNews, game.GameTime, "Automatic maintenance", string.Join("\n", done)));
                }
                if (skipped.Count > 0)
                {
                    game.NewsBox.AddNews(
                        new News(
                            News.NewsType.AirlineNews, game.GameTime, "Automatic maintenance waiting for cash",
                            "There is not enough money for the checks that are due on: " + string.Join(", ", skipped) + "."));
                }
            }
            return done;
        }

        /// <summary>Monthly wear and chance of breakdowns for one airline. Returns messages for what happened.</summary>
        public static List<string> ProcessMonth(Airline airline, Random rnd = null)
        {
            rnd = rnd ?? new Random();
            GameObject game = GameObject.GetInstance();
            var messages = new List<string>();
            foreach (FleetAirliner f in airline.Fleet.ToList())
            {
                if (f.Airliner.BuiltDate > game.GameTime) continue;

                double wear = f.Routes.Count > 0 ? 0.4 + rnd.NextDouble()*0.5 : 0.1;
                foreach (MaintenanceCheck c in new[] {MaintenanceCheck.A, MaintenanceCheck.B, MaintenanceCheck.C})
                {
                    if (IsOverdue(f, c)) wear += 0.2;
                }
                f.Airliner.Condition = Math.Max(20, f.Airliner.Condition - wear);

                double worn = (100 - f.Airliner.Condition)/100;
                if (f.Routes.Count > 0 && !IsGrounded(f) && rnd.NextDouble() < worn*worn*0.2)
                {
                    messages.Add(Breakdown(airline, f, rnd));
                }
            }
            return messages;
        }

        private static string Breakdown(Airline airline, FleetAirliner f, Random rnd)
        {
            GameObject game = GameObject.GetInstance();
            double cost = f.Airliner.GetValue()*(0.01 + rnd.NextDouble()*0.04);
            f.Airliner.Condition = Math.Max(20, f.Airliner.Condition - rnd.Next(3, 9));
            f.GroundedToDate = game.GameTime.AddDays(7);
            AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Maintenances, -cost);

            Airport where = PickLocation(f, rnd);
            double payout = InsuranceService.Payout(airline, f, where, cost);
            if (payout > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Maintenances, payout);
            }

            string text = f.Name + " broke down at " + (where != null ? where.Profile.IATACode : "its base") + ". Repairs cost " + cost.ToString("C0", CultureInfo.GetCultureInfo("en-US"))
                          + " and it is grounded for 7 days."
                          + (payout > 0 ? " Insurance paid " + payout.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + "." : " It was not covered by insurance.");
            if (airline == game.HumanAirline)
            {
                game.NewsBox.AddNews(new News(News.NewsType.AirlineNews, game.GameTime, "Breakdown: " + f.Name, text));
            }
            return text;
        }

        private static Airport PickLocation(FleetAirliner f, Random rnd)
        {
            var places = new List<Airport>();
            foreach (var r in f.Routes)
            {
                places.Add(r.Destination1);
                places.Add(r.Destination2);
            }
            if (places.Count == 0) return f.Homebase;
            return places[rnd.Next(places.Count)];
        }
    }

    /// <summary>Airline insurance: quotes, payments, cancellation and payouts for breakdowns.</summary>
    public static class InsuranceService
    {
        public static int PaymentsPerYear(AirlineInsurance.PaymentTerms t)
        {
            switch (t)
            {
                case AirlineInsurance.PaymentTerms.Monthly: return 12;
                case AirlineInsurance.PaymentTerms.Quarterly: return 4;
                case AirlineInsurance.PaymentTerms.Biannual: return 2;
                default: return 1;
            }
        }

        public static int MonthsPerPayment(AirlineInsurance.PaymentTerms t)
        {
            return 12/PaymentsPerYear(t);
        }

        public static string Describe(AirlineInsurance.InsuranceType t)
        {
            switch (t)
            {
                case AirlineInsurance.InsuranceType.PublicLiability: return "Third-party damage on the ground. Does not cover your aircraft.";
                case AirlineInsurance.InsuranceType.PassengerLiability: return "Injury to passengers. Does not cover your aircraft.";
                case AirlineInsurance.InsuranceType.CombinedSingleLimit: return "Liability plus damage to your aircraft in one limit.";
                default: return "Everything above, with the highest premiums.";
            }
        }

        public static string Describe(AirlineInsurance.InsuranceScope s)
        {
            switch (s)
            {
                case AirlineInsurance.InsuranceScope.Airport: return "Only airports where you have facilities.";
                case AirlineInsurance.InsuranceScope.Domestic: return "Airports in your home country.";
                case AirlineInsurance.InsuranceScope.Hub: return "Your hubs and all domestic airports.";
                default: return "Anywhere in the world.";
            }
        }

        public static bool CoversAircraft(AirlineInsurance.InsuranceType t)
        {
            return t == AirlineInsurance.InsuranceType.CombinedSingleLimit || t == AirlineInsurance.InsuranceType.FullCoverage;
        }

        private static double TypeFactor(AirlineInsurance.InsuranceType t)
        {
            switch (t)
            {
                case AirlineInsurance.InsuranceType.PublicLiability: return 1;
                case AirlineInsurance.InsuranceType.PassengerLiability: return 1.4;
                case AirlineInsurance.InsuranceType.CombinedSingleLimit: return 2;
                default: return 3;
            }
        }

        private static double ScopeFactor(AirlineInsurance.InsuranceScope s)
        {
            switch (s)
            {
                case AirlineInsurance.InsuranceScope.Airport: return 0.8;
                case AirlineInsurance.InsuranceScope.Domestic: return 1;
                case AirlineInsurance.InsuranceScope.Hub: return 1.4;
                default: return 2;
            }
        }

        /// <summary>Builds a priced policy covering the whole fleet; the amount is the limit per aircraft. Nothing is charged.</summary>
        public static AirlineInsurance Quote(Airline airline, AirlineInsurance.InsuranceType type, AirlineInsurance.InsuranceScope scope, AirlineInsurance.PaymentTerms terms, int years, int amount)
        {
            years = Math.Max(1, Math.Min(20, years));
            var policy = AirlineInsuranceHelpers.CreatePolicy(airline, type, scope, terms, true, years, amount);
            policy.AllFleetAirliners = true;
            policy.Deductible = amount*(scope == AirlineInsurance.InsuranceScope.Airport ? 0.005 : 0.001);

            double fleet = Math.Max(1, airline.Fleet.Count)*0.95;
            double annual = amount*0.0015*TypeFactor(type)*ScopeFactor(scope)*AirlineInsuranceHelpers.GetRatingModifier(airline)*fleet;
            policy.PaymentAmount = annual/PaymentsPerYear(terms);
            policy.TermLength = years;
            policy.RemainingPayments = years*PaymentsPerYear(terms);
            return policy;
        }

        /// <summary>Buys a quoted policy: the first payment is charged now.</summary>
        public static bool Purchase(Airline airline, AirlineInsurance policy, out string message)
        {
            GameObject game = GameObject.GetInstance();
            if (airline == game.HumanAirline && game.HumanMoney < policy.PaymentAmount)
            {
                message = "Not enough money for the first payment of " + policy.PaymentAmount.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + ".";
                return false;
            }
            AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Maintenances, -policy.PaymentAmount);
            policy.RemainingPayments--;
            policy.NextPaymentDue = game.GameTime.AddMonths(MonthsPerPayment(policy.InsTerms));
            policy.InsuranceEffective = game.GameTime;
            policy.InsuranceExpires = game.GameTime.AddYears((int) policy.TermLength);
            policy.CancellationFee = (int) (policy.PaymentAmount*2);
            airline.AddInsurance(policy);
            message = "Policy bought. First payment of " + policy.PaymentAmount.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + " charged.";
            return true;
        }

        public static bool Cancel(Airline airline, AirlineInsurance policy, out string message)
        {
            GameObject game = GameObject.GetInstance();
            double fee = policy.CancellationFee;
            if (airline == game.HumanAirline && game.HumanMoney < fee)
            {
                message = "Not enough money for the cancellation fee of " + fee.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + ".";
                return false;
            }
            if (fee > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Maintenances, -fee);
            }
            airline.RemoveInsurance(policy);
            message = "Policy cancelled" + (fee > 0 ? " for a fee of " + fee.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + "." : ".");
            return true;
        }

        public static bool IsCovered(Airline airline, AirlineInsurance policy, Airport where)
        {
            switch (policy.InsScope)
            {
                case AirlineInsurance.InsuranceScope.Global:
                    return true;
                case AirlineInsurance.InsuranceScope.Hub:
                    return where != null && (airline.GetHubs().Contains(where) || where.Profile.Country == airline.Profile.Country);
                case AirlineInsurance.InsuranceScope.Domestic:
                    return where != null && where.Profile.Country == airline.Profile.Country;
                default:
                    return where != null && airline.Airports.Contains(where);
            }
        }

        /// <summary>Amount paid by the best policy for damage to an aircraft (0 if not covered).</summary>
        public static double Payout(Airline airline, FleetAirliner f, Airport where, double damage)
        {
            double best = 0;
            foreach (AirlineInsurance p in airline.InsurancePolicies)
            {
                if (!CoversAircraft(p.InsType) || p.InsuranceExpires < GameObject.GetInstance().GameTime || !IsCovered(airline, p, where)) continue;
                double pay = Math.Min(damage, p.InsuredAmount) - p.Deductible;
                if (pay > best) best = pay;
            }
            return best;
        }

        /// <summary>Monthly: charge payments that are due and remove expired policies.</summary>
        public static void ProcessMonth(Airline airline)
        {
            GameObject game = GameObject.GetInstance();
            foreach (AirlineInsurance p in airline.InsurancePolicies.ToList())
            {
                if (p.InsuranceExpires < game.GameTime || p.RemainingPayments <= 0 && p.NextPaymentDue < game.GameTime)
                {
                    airline.RemoveInsurance(p);
                    continue;
                }
                if (p.RemainingPayments > 0 && p.NextPaymentDue.Date <= game.GameTime.Date)
                {
                    AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Maintenances, -p.PaymentAmount);
                    p.RemainingPayments--;
                    p.NextPaymentDue = p.NextPaymentDue.AddMonths(MonthsPerPayment(p.InsTerms));
                }
            }
        }
    }
}
