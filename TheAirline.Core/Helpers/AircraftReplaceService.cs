using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>An aircraft that could take over another aircraft's flights on a route, or why it can't.</summary>
    public class ReplacementOption
    {
        public FleetAirliner Airliner { get; set; }
        public bool CanReplace { get; set; }

        /// <summary>"free", "also flies ATL-MIA", or why it can't.</summary>
        public string Note { get; set; }
    }

    /// <summary>
    ///     Puts another aircraft of the player's fleet on a route in place of one that flies it (N101AA to N103AA): the
    ///     new aircraft takes over exactly the flights the old one had on the route, at the same times, and the old one
    ///     keeps its other routes (or stands idle). The new aircraft may fly other routes too, when the flights fit
    ///     between its own.
    /// </summary>
    public static class AircraftReplaceService
    {
        private static bool RightKind(FleetAirliner f, Route route)
        {
            AirlinerType.TypeOfAirliner kind = f.Airliner.Type.TypeAirliner;
            if (route.Type == Route.RouteType.Cargo) return kind == AirlinerType.TypeOfAirliner.Cargo;
            if (route.Type == Route.RouteType.Helicopter) return kind == AirlinerType.TypeOfAirliner.Helicopter;
            return kind == AirlinerType.TypeOfAirliner.Passenger || kind == AirlinerType.TypeOfAirliner.Mixed;
        }

        private static string Code(Route r)
        {
            return r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode;
        }

        private static int WeekMinute(RouteTimeTableEntry e)
        {
            return (int) e.Day*24*60 + (int) e.Time.TotalMinutes;
        }

        /// <summary>
        ///     Whether the airliner can fly all the given flights: each one, with the flight time of this aircraft type
        ///     and its turnaround, must end before the next one of the week starts. Returns the first clash, or null.
        /// </summary>
        public static string FirstClash(FleetAirliner airliner, List<RouteTimeTableEntry> entries)
        {
            var list = entries.OrderBy(WeekMinute).ToList();
            if (list.Count < 2) return null;
            TimeSpan turnaround = FleetAirlinerHelpers.GetMinTimeBetweenFlights(airliner);
            const int week = 7*24*60;
            for (int i = 0; i < list.Count; i++)
            {
                RouteTimeTableEntry e = list[i];
                RouteTimeTableEntry next = list[(i + 1)%list.Count];
                int start = WeekMinute(e);
                int nextStart = WeekMinute(next) + (i + 1 == list.Count ? week : 0);
                double needs = e.TimeTable.Route.GetFlightTime(airliner.Airliner.Type).TotalMinutes + turnaround.TotalMinutes;
                if (start + needs > nextStart)
                {
                    return e.Day + " " + e.Time.ToString(@"hh\:mm") + " from " + e.DepartureAirport.Profile.IATACode + " (" + Code(e.TimeTable.Route)
                           + ") needs " + Math.Ceiling(needs) + " min with the turnaround, and the next flight leaves after " + (nextStart - start) + " min";
                }
            }
            return null;
        }

        /// <summary>The player's aircraft that could take over the flights of `from` on the route, the ones that fit first.</summary>
        public static List<ReplacementOption> Options(Route route, FleetAirliner from)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            var moving = route.TimeTable.Entries.Where(e => e.Airliner == from).ToList();
            double distance = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            long runway = Math.Min(route.Destination1.GetMaxRunwayLength(), route.Destination2.GetMaxRunwayLength());
            var onRoute = route.GetAirliners();

            var list = new List<ReplacementOption>();
            foreach (FleetAirliner f in airline.DeliveredFleet.Where(f => f != from && !onRoute.Contains(f) && RightKind(f, route)).OrderBy(f => f.Name))
            {
                var o = new ReplacementOption {Airliner = f};
                if (AircraftDealService.IsLeasedOut(f)) o.Note = "leased out";
                else if (AircraftDealService.IsListed(f)) o.Note = "for sale";
                else if (CharterService.IsOnCharter(f)) o.Note = "on a charter";
                else if (f.Airliner.Range < distance) o.Note = "range too short (" + f.Airliner.Range.ToString("N0") + " km)";
                else if (f.Airliner.Type.MinRunwaylength > runway) o.Note = "runway too short";
                else
                {
                    var own = f.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == f)).ToList();
                    string clash = FirstClash(f, own.Concat(moving).ToList());
                    if (clash != null)
                    {
                        o.Note = f.Routes.Count == 0 ? "too slow for this timetable" : "the flights don't fit between its own";
                    }
                    else
                    {
                        o.CanReplace = true;
                        o.Note = f.Routes.Count == 0 ? "free" : "also flies " + string.Join(", ", f.Routes.Select(Code));
                    }
                }
                list.Add(o);
            }
            return list.OrderByDescending(o => o.CanReplace).ThenBy(o => o.Airliner.Name).ToList();
        }

        /// <summary>Puts `to` on the route in place of `from`, on the same flights. False with the reason when it can't.</summary>
        public static bool Replace(Route route, FleetAirliner from, FleetAirliner to, out string message)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            message = "";
            if (route == null || !airline.Routes.Contains(route))
            {
                message = "That isn't one of your routes.";
                return false;
            }
            if (from == null || !route.GetAirliners().Contains(from))
            {
                message = "Pick an aircraft that flies this route.";
                return false;
            }
            if (to == null)
            {
                message = "Pick the aircraft to put on the route.";
                return false;
            }

            ReplacementOption option = Options(route, from).FirstOrDefault(o => o.Airliner == to);
            if (option == null)
            {
                message = to.Name + " can't take over: it already flies this route, isn't delivered yet, or is the wrong kind of aircraft.";
                return false;
            }
            if (!option.CanReplace)
            {
                var own = to.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == to)).ToList();
                string clash = FirstClash(to, own.Concat(route.TimeTable.Entries.Where(e => e.Airliner == from)).ToList());
                bool timing = option.Note.Contains("slow") || option.Note.Contains("fit");
                message = to.Name + " can't take over from " + from.Name + ": " + option.Note + (timing && clash != null ? " (" + clash + ")" : "") + ".";
                return false;
            }

            int flights = 0;
            foreach (RouteTimeTableEntry e in route.TimeTable.Entries.Where(e => e.Airliner == from).ToList())
            {
                e.Airliner = to;
                flights++;
            }
            from.Routes.Remove(route);
            if (!to.Routes.Contains(route)) to.AddRoute(route);

            string fromNow;
            if (from.Routes.Count == 0)
            {
                from.Status = FleetAirliner.AirlinerStatus.Stopped;
                fromNow = from.Name + " has no route now.";
            }
            else
            {
                fromNow = from.Name + " keeps flying " + string.Join(", ", from.Routes.Select(Code)) + ".";
            }

            string toNow;
            if (to.Status != FleetAirliner.AirlinerStatus.Stopped)
            {
                toNow = to.Name + " is flying it.";
            }
            else if (to.Pilots.Count >= to.Airliner.Type.CockpitCrew)
            {
                to.Status = GameObject.GetInstance().DayRoundEnabled ? FleetAirliner.AirlinerStatus.OnRoute : FleetAirliner.AirlinerStatus.ToRouteStart;
                toNow = to.Name + " is flying it.";
            }
            else
            {
                toNow = to.Name + " has no full pilot crew yet, so it stays stopped until it has one.";
            }

            RouteIndex.Invalidate();
            message = to.Name + " takes over " + from.Name + "'s " + flights + " weekly flights on " + Code(route) + ", at the same times. " + toNow + " " + fromNow;
            return true;
        }
    }
}
