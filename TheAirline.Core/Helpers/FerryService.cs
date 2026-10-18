using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Automatic ferry flights. An aircraft that has been idle (it did not fly yesterday) and is scheduled to fly from an airport
    ///     other than where it stands gets an empty ferry flight there first. It costs fuel and takes time; a long ferry costs the
    ///     flights of that day.
    /// </summary>
    public static class FerryService
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        public static bool IsFerrying(FleetAirliner f)
        {
            return f.FerryTo != null && f.FerryUntil > GameObject.GetInstance().GameTime;
        }

        /// <summary>Hours a ferry of this length takes: flying time plus taxi, take-off and landing.</summary>
        public static double Hours(FleetAirliner f, double km)
        {
            return km/Math.Max(300, f.Airliner.Type.CruisingSpeed) + 1.5;
        }

        public static double Cost(FleetAirliner f, Airport from, Airport to)
        {
            double km = MathHelpers.GetDistance(from, to);
            int seats = CharterService.CapacityOf(f);
            double fuel = AirportHelpers.GetFuelPrice(from)*km*f.Airliner.FuelConsumption*Math.Max(1, seats)*0.55;
            return Math.Round(fuel*1.1 + GeneralHelpers.GetInflationPrice(800));
        }

        /// <summary>Why the aircraft cannot ferry between two airports, or null when it can.</summary>
        public static string GetBlocker(FleetAirliner f, Airport from, Airport to)
        {
            double km = MathHelpers.GetDistance(from, to);
            if (f.Airliner.Type.Range < km) return "the ferry is " + DistanceFormatter(km) + " but its range is " + DistanceFormatter(f.Airliner.Type.Range);
            if (f.Airliner.Type.MinRunwaylength > to.GetMaxRunwayLength()) return "the runway at " + to.Profile.IATACode + " is too short";
            if (f.Airliner.Type.MinRunwaylength > from.GetMaxRunwayLength()) return "the runway at " + from.Profile.IATACode + " is too short";
            return null;
        }

        private static string DistanceFormatter(double km)
        {
            return MaintenanceService.DistanceText(km);
        }

        private static void Post(string title, string text)
        {
            GameObject game = GameObject.GetInstance();
            game.NewsBox.AddNews(new News(News.NewsType.AirlineNews, game.GameTime, title, text));
        }

        /// <summary>
        ///     Called for each aircraft before its flights of the day. Returns false when it must not fly today (ferrying, or no ferry possible).
        /// </summary>
        public static bool BeforeFlights(FleetAirliner f, List<RouteTimeTableEntry> entries)
        {
            GameObject game = GameObject.GetInstance();
            DateTime today = game.GameTime.Date;
            Airline airline = f.Airliner.Airline;

            if (f.FerryTo != null)
            {
                if (f.FerryUntil > game.GameTime) return false;
                f.CurrentPosition = f.FerryTo;
                f.FerryTo = null;
                f.LastFlightDate = today;
            }

            if (entries.Count == 0 || f.CurrentPosition == null) return true;
            RouteTimeTableEntry first = entries.OrderBy(e => e.Time).First();
            Airport dep = first.DepartureAirport;
            if (dep == null || dep == f.CurrentPosition) return true;
            //an aircraft that flew yesterday just carries on, as before
            if (f.LastFlightDate >= today.AddDays(-1) && f.LastFlightDate != DateTime.MinValue) return true;

            double km = MathHelpers.GetDistance(f.CurrentPosition, dep);
            if (km < 50) return true;

            string key = f.Name + ">" + dep.Profile.IATACode;
            string blocker = GetBlocker(f, f.CurrentPosition, dep);
            double cost = Cost(f, f.CurrentPosition, dep);
            if (blocker == null && airline.Money < cost) blocker = "there is not enough money for the fuel (" + cost.ToString("N0") + ")";
            if (blocker != null)
            {
                if (Warned.Add(key))
                {
                    Post(
                        "No ferry flight possible",
                        f.Name + " stands at " + f.CurrentPosition.Profile.IATACode + " but is scheduled from " + dep.Profile.IATACode + ", and cannot ferry: " + blocker + ". It stays on the ground until that is fixed.");
                }
                return false;
            }
            Warned.Remove(key);

            double hours = Hours(f, km);
            AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.FlightExpenses, -cost);
            f.Airliner.Flown += km;
            Airport from = f.CurrentPosition;
            if (hours <= first.Time.TotalHours - 0.5)
            {
                f.CurrentPosition = dep;
                f.LastFlightDate = today;
                Post("Ferry flight", f.Name + " ferried " + from.Profile.IATACode + " - " + dep.Profile.IATACode + " (" + DistanceFormatter(km) + ", " + hours.ToString("0.0") + " h, " + cost.ToString("N0") + ") before its first flight today.");
                return true;
            }

            int days = Math.Max(1, (int) Math.Ceiling(hours/24.0));
            f.FerryTo = dep;
            f.FerryUntil = today.AddDays(days);
            Post(
                "Ferry flight", f.Name + " is ferrying " + from.Profile.IATACode + " - " + dep.Profile.IATACode + " (" + DistanceFormatter(km) + ", " + hours.ToString("0.0") + " h, " + cost.ToString("N0")
                                 + "). It is too late for today's flight and starts its schedule on " + f.FerryUntil.ToShortDateString() + ".");
            return false;
        }
    }
}
