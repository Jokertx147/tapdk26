using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.General;
using TheAirline.Models.Pilots;
using TheAirline.Models.Airlines;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Pilot pay and morale. The airline sets a pay level (percent of the market salary). Every month each pilot's morale moves
    ///     toward a target that depends on pay, whether he has an aircraft, and his time with the airline. Unhappy pilots quit.
    ///     A one-off bonus lifts everybody's morale.
    /// </summary>
    public static class PilotMoraleService
    {
        public const int MinPay = 70;
        public const int MaxPay = 150;
        public const int BonusWaitDays = 120;
        // Random.Shared is safe to use from the parallel airline threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        public static double PayFactor(Airline airline)
        {
            return airline.PilotPayPercent <= 0 ? 1.0 : airline.PilotPayPercent/100.0;
        }

        public static string MoodText(double morale)
        {
            if (morale >= 85) return "Excellent";
            if (morale >= 65) return "Good";
            if (morale >= 45) return "Okay";
            if (morale >= 30) return "Low";
            return "About to quit";
        }

        /// <summary>The monthly pay of all the airline's pilots at a pay level.</summary>
        public static double Payroll(Airline airline, int percent)
        {
            double sum = 0;
            foreach (Pilot p in airline.Pilots.ToList())
            {
                sum += AirlineHelpers.GetPilotSalary(airline, p)/PayFactor(airline)*percent/100.0;
            }
            return sum;
        }

        public static bool SetPay(Airline airline, int percent, out string message)
        {
            percent = Math.Max(MinPay, Math.Min(MaxPay, percent));
            airline.PilotPayPercent = percent;
            message = "Pilots are now paid " + percent + "% of the market salary.";
            return true;
        }

        public static double BonusCost(Airline airline)
        {
            return Payroll(airline, airline.PilotPayPercent <= 0 ? 100 : (int) Math.Round(airline.PilotPayPercent));
        }

        public static int DaysToNextBonus(Airline airline)
        {
            double d = BonusWaitDays - (GameObject.GetInstance().GameTime.Date - airline.LastPilotBonus.Date).TotalDays;
            return airline.LastPilotBonus == DateTime.MinValue ? 0 : (int) Math.Max(0, Math.Ceiling(d));
        }

        public static bool PayBonus(Airline airline, out string message)
        {
            GameObject game = GameObject.GetInstance();
            if (airline.Pilots.Count == 0)
            {
                message = "You have no pilots.";
                return false;
            }
            int wait = DaysToNextBonus(airline);
            if (wait > 0)
            {
                message = "A bonus was paid recently. Wait " + wait + " more days or it stops meaning anything.";
                return false;
            }
            double cost = BonusCost(airline);
            if (airline.Money < cost)
            {
                message = "Not enough money: the bonus costs " + cost.ToString("N0") + ".";
                return false;
            }
            AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Wages, -cost);
            foreach (Pilot p in airline.Pilots.ToList()) p.Morale = Math.Min(100, p.Morale + 15);
            airline.LastPilotBonus = game.GameTime.Date;
            message = "Every pilot got a month's pay as a bonus (" + cost.ToString("N0") + "). Morale is up.";
            return true;
        }

        public static double Target(Airline airline, Pilot p)
        {
            GameObject game = GameObject.GetInstance();
            double years = Math.Max(0, (game.GameTime - p.AirlineSignedDate).TotalDays/365.0);
            double target = 55 + (PayFactor(airline)*100 - 100)*0.9;
            target += p.Airliner != null ? 10 : -12;
            target += Math.Min(6, years*1.5);
            if (p.Training != null) target += 4;
            return Math.Max(0, Math.Min(100, target));
        }

        public static double Average(Airline airline)
        {
            return airline.Pilots.Count == 0 ? 0 : airline.Pilots.Average(p => p.Morale);
        }

        public static List<string> ProcessMonth(Airline airline)
        {
            GameObject game = GameObject.GetInstance();
            var quit = new List<string>();
            foreach (Pilot p in airline.Pilots.ToList())
            {
                double target = Target(airline, p);
                p.Morale = Math.Max(0, Math.Min(100, p.Morale + (target - p.Morale)*0.3 + (Rnd.NextDouble()*4 - 2)));
                if (p.Morale < 30 && p.Training == null && Rnd.NextDouble() < Math.Min(0.5, (30 - p.Morale)/40.0))
                {
                    string where = p.Airliner != null ? " (he flew " + p.Airliner.Name + ")" : "";
                    airline.RemovePilot(p);
                    p.Morale = 60;
                    quit.Add(p.Profile.Name + where);
                }
            }

            if (airline == game.HumanAirline)
            {
                if (quit.Count > 0)
                {
                    game.NewsBox.AddNews(
                        new News(
                            News.NewsType.AirlineNews, game.GameTime, "Pilots have quit",
                            string.Join(", ", quit) + " left the airline because of low morale. Pay better, give a bonus, or keep pilots on aircraft."));
                }
                else if (airline.Pilots.Count > 0 && Average(airline) < 40)
                {
                    game.NewsBox.AddNews(
                        new News(
                            News.NewsType.AirlineNews, game.GameTime, "Pilots are unhappy",
                            "The average pilot morale is " + Average(airline).ToString("0") + ". Some are close to quitting. Raise the pay level, pay a bonus, or put idle pilots on aircraft."));
                }
            }
            return quit;
        }
    }
}
