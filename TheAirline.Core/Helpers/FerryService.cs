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

        public static double LegCost(FleetAirliner f, Airport from, Airport to)
        {
            double km = MathHelpers.GetDistance(from, to);
            int seats = CharterService.CapacityOf(f);
            double fuel = AirportHelpers.GetFuelPrice(from)*km*f.Airliner.FuelConsumption*Math.Max(1, seats)*0.55;
            return Math.Round(fuel*1.1 + GeneralHelpers.GetInflationPrice(800));
        }

        public static double Cost(FleetAirliner f, Airport from, Airport to)
        {
            return LegCost(f, from, to);
        }

        /// <summary>
        ///     Plans a ferry as a list of landings (the last one is the destination). A single hop when the aircraft can fly it,
        ///     otherwise stops at airports on the way that its range and runway allow. Returns null when there is no way.
        /// </summary>
        public static List<Airport> Plan(FleetAirliner f, Airport from, Airport to)
        {
            double range = f.Airliner.Type.Range*0.92;
            long runway = f.Airliner.Type.MinRunwaylength;
            if (to.GetMaxRunwayLength() < runway || from.GetMaxRunwayLength() < runway) return null;
            var path = new List<Airport>();
            Airport at = from;
            List<Airport> pool = null;
            for (int hop = 0; hop < 10; hop++)
            {
                double left = MathHelpers.GetDistance(at, to);
                if (left <= range)
                {
                    path.Add(to);
                    return path;
                }
                if (pool == null) pool = Airports.GetAllActiveAirports().Where(a => a.GetMaxRunwayLength() >= runway && a.GetMaxRunwayLength() >= 1000).ToList();
                Airport best = null;
                double bestLeft = left*0.97;
                foreach (Airport c in pool)
                {
                    if (c == at || path.Contains(c)) continue;
                    if (MathHelpers.GetDistance(at, c) > range) continue;
                    double l = MathHelpers.GetDistance(c, to);
                    if (l < bestLeft)
                    {
                        bestLeft = l;
                        best = c;
                    }
                }
                if (best == null) return null;
                path.Add(best);
                at = best;
            }
            return null;
        }

        public static double PlanKm(Airport from, List<Airport> plan)
        {
            double km = 0;
            Airport at = from;
            foreach (Airport a in plan)
            {
                km += MathHelpers.GetDistance(at, a);
                at = a;
            }
            return km;
        }

        public static double PlanHours(FleetAirliner f, Airport from, List<Airport> plan)
        {
            double h = 0;
            Airport at = from;
            foreach (Airport a in plan)
            {
                h += Hours(f, MathHelpers.GetDistance(at, a));
                at = a;
            }
            return h;
        }

        public static double PlanCost(FleetAirliner f, Airport from, List<Airport> plan)
        {
            double c = 0;
            Airport at = from;
            foreach (Airport a in plan)
            {
                c += LegCost(f, at, a);
                at = a;
            }
            return c;
        }

        /// <summary>Why the aircraft cannot ferry between two airports, or null when it can.</summary>
        public static string GetBlocker(FleetAirliner f, Airport from, Airport to)
        {
            if (f.Airliner.Type.MinRunwaylength > to.GetMaxRunwayLength()) return "the runway at " + to.Profile.IATACode + " is too short for it";
            if (f.Airliner.Type.MinRunwaylength > from.GetMaxRunwayLength()) return "the runway at " + from.Profile.IATACode + " is too short for it";
            if (Plan(f, from, to) == null) return "no way found with its range of " + DistanceFormatter(f.Airliner.Type.Range) + " and runway needs (" + DistanceFormatter(MathHelpers.GetDistance(from, to)) + " away)";
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

            if (MathHelpers.GetDistance(f.CurrentPosition, dep) < 50) return true;

            string key = f.Name + ">" + dep.Profile.IATACode;
            string message;
            if (!Start(f, dep, out message, first.Time.TotalHours - 0.5))
            {
                if (Warned.Add(key))
                {
                    Post("No ferry flight possible", f.Name + " stands at " + f.CurrentPosition.Profile.IATACode + " but is scheduled from " + dep.Profile.IATACode + ". " + message + " It stays on the ground until that is fixed.");
                }
                return false;
            }
            Warned.Remove(key);
            //true when the ferry was short enough to be done before the first flight of the day
            return f.FerryTo == null;
        }

        /// <summary>
        ///     Starts a ferry flight to an airport (planned over stops when it is far). When <paramref name="hoursBeforeFlight" /> is
        ///     given and the ferry fits in them, the aircraft is there at once; otherwise it is away for whole days.
        /// </summary>
        public static bool Start(FleetAirliner f, Airport to, out string message, double hoursBeforeFlight = -1)
        {
            GameObject game = GameObject.GetInstance();
            Airline airline = f.Airliner.Airline;
            Airport from = f.CurrentPosition;
            if (from == null || to == null)
            {
                message = "Its position is unknown.";
                return false;
            }
            if (from == to)
            {
                message = f.Name + " is already at " + to.Profile.IATACode + ".";
                return false;
            }
            var plan = Plan(f, from, to);
            if (plan == null)
            {
                message = "It cannot ferry: " + GetBlocker(f, from, to) + ".";
                return false;
            }
            double cost = PlanCost(f, from, plan);
            if (airline.Money < cost)
            {
                message = "It cannot ferry: there is not enough money for the fuel (" + cost.ToString("N0") + ").";
                return false;
            }
            double km = PlanKm(from, plan);
            double hours = PlanHours(f, from, plan);
            AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.FlightExpenses, -cost);
            f.Airliner.Flown += km;
            string via = plan.Count > 1 ? " via " + string.Join(", ", plan.Take(plan.Count - 1).Select(a => a.Profile.IATACode)) : "";
            string leg = from.Profile.IATACode + " - " + to.Profile.IATACode + via + " (" + DistanceFormatter(km) + ", " + hours.ToString("0.0") + " h, " + cost.ToString("N0") + ")";
            if (hoursBeforeFlight >= 0 && hours <= hoursBeforeFlight)
            {
                f.CurrentPosition = to;
                f.LastFlightDate = game.GameTime.Date;
                Post("Ferry flight", f.Name + " ferried " + leg + " before its first flight today.");
                message = "Ferried.";
                return true;
            }
            int days = Math.Max(1, (int) Math.Ceiling(hours/24.0));
            f.FerryTo = to;
            f.FerryUntil = game.GameTime.Date.AddDays(days);
            Post("Ferry flight", f.Name + " is ferrying " + leg + ". It is there on " + f.FerryUntil.ToShortDateString() + ".");
            message = f.Name + " is ferrying " + leg + ". It arrives on " + f.FerryUntil.ToShortDateString() + ".";
            return true;
        }

        /// <summary>Why the aircraft cannot be ferried by hand now, or null when it can.</summary>
        public static string GetManualBlocker(FleetAirliner f)
        {
            GameObject game = GameObject.GetInstance();
            if (f.Airliner.BuiltDate > game.GameTime) return "it has not been delivered yet";
            if (f.Status != FleetAirliner.AirlinerStatus.Stopped) return "stop it first";
            if (IsFerrying(f)) return "it is already on a ferry flight";
            if (CharterService.IsOnCharter(f)) return "it is out on a charter";
            if (AircraftDealService.IsLeasedOut(f)) return "it is leased out";
            if (MaintenanceService.IsGrounded(f)) return "it is grounded";
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) return "it needs a full pilot crew (" + f.Pilots.Count + "/" + f.Airliner.Type.CockpitCrew + ")";
            return null;
        }

        /// <summary>Daily: ferries that have arrived put the aircraft at its destination (also for stopped aircraft).</summary>
        public static void ProcessDaily(Airline airline)
        {
            GameObject game = GameObject.GetInstance();
            foreach (FleetAirliner f in airline.Fleet.ToList())
            {
                if (f.FerryTo != null && f.FerryUntil <= game.GameTime)
                {
                    f.CurrentPosition = f.FerryTo;
                    f.FerryTo = null;
                    f.LastFlightDate = game.GameTime.Date;
                    if (airline == game.HumanAirline)
                    {
                        Post("Ferry flight", f.Name + " has landed at " + f.CurrentPosition.Profile.IATACode + ".");
                    }
                }
            }
        }

        /// <summary>A sentence on where the aircraft is and, when it is not flying, why.</summary>
        public static string Diagnose(FleetAirliner f)
        {
            GameObject game = GameObject.GetInstance();
            string at = f.CurrentPosition == null ? "unknown" : f.CurrentPosition.Profile.IATACode + " " + f.CurrentPosition.Profile.Name;
            if (f.Airliner.BuiltDate > game.GameTime) return "Not delivered yet.";
            if (IsFerrying(f)) return "At " + at + ", on a ferry flight to " + f.FerryTo.Profile.IATACode + " until " + f.FerryUntil.ToShortDateString() + ".";
            if (CharterService.IsOnCharter(f)) return "At " + at + ", out on a charter.";
            if (AircraftDealService.IsLeasedOut(f)) return "At " + at + ", leased out.";
            if (f.Routes.Count == 0) return "At " + at + ". No routes: schedule it from here, or use Ferry to bring it to one of your airports.";
            if (f.Status == FleetAirliner.AirlinerStatus.Stopped) return "At " + at + ". It is stopped: press Start for it to fly its schedule" + (f.Pilots.Count < f.Airliner.Type.CockpitCrew ? " (it first needs a full pilot crew)" : "") + ".";
            if (MaintenanceService.IsGrounded(f)) return "At " + at + ", grounded until " + f.GroundedToDate.ToShortDateString() + ".";
            var entries = f.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == f)).ToList();
            if (entries.Count == 0) return "At " + at + ". It has routes but no flights in a timetable: add flights under its route's timetable.";
            var first = entries.OrderBy(e => e.Day).ThenBy(e => e.Time).First().DepartureAirport;
            if (first != null && f.CurrentPosition != null && first != f.CurrentPosition && MathHelpers.GetDistance(first, f.CurrentPosition) >= 50)
            {
                string why = GetBlocker(f, f.CurrentPosition, first);
                return "At " + at + ", its schedule starts at " + first.Profile.IATACode + ". " + (why != null ? "No ferry is possible: " + why + "." : f.Airliner.Airline.AutoFerry ? "It ferries there on its first flying day." : "Auto ferry is off, so it starts from there.");
            }
            return "At " + at + ", flying its schedule.";
        }
    }
}
