using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Scenarios;
using TheAirline.Models.General.Statistics;

namespace TheAirline.Helpers
{
    /// <summary>One goal of a scenario as the player sees it.</summary>
    public class ScenarioGoal
    {
        public ScenarioFailure Failure;
        public string Goal, When, Now, Status;

        /// <summary>True when met right now, false when not, null when it can't be measured.</summary>
        public bool? Ok;
    }

    /// <summary>
    ///     Scenarios: a start (year, airline, fleet, routes, rivals, cash and sometimes debt), goals the airline must keep
    ///     meeting (every month, or once in a given year) and an end year. Missing a monthly goal for its allowed number of
    ///     months in a row, or a yearly goal when it is checked, ends the scenario as lost; reaching the end year wins it.
    ///     The airline and its subsidiaries count together. Safety, security and crime goals aren't measured by the game.
    /// </summary>
    public static class ScenarioService
    {
        private static Airline Main => GameObject.GetInstance().MainAirline ?? GameObject.GetInstance().HumanAirline;

        private static IEnumerable<Airline> Group(Airline a)
        {
            yield return a;
            foreach (var s in a.Subsidiaries.ToList()) yield return s;
        }

        public static bool IsMeasured(ScenarioFailure.FailureType t)
        {
            return t != ScenarioFailure.FailureType.Safety && t != ScenarioFailure.FailureType.Security && t != ScenarioFailure.FailureType.Crime;
        }

        private static double Value(ScenarioFailure f)
        {
            double v;
            return double.TryParse(Convert.ToString(f.Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static double Passengers(Airline a, int year)
        {
            return a.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("Passengers"));
        }

        /// <summary>The airline's current figure for a goal, or null when it can't be measured (yet).</summary>
        public static double? Measure(ScenarioObject so, ScenarioFailure f)
        {
            var main = Main;
            if (main == null) return null;
            var all = Group(main).ToList();
            int year = GameObject.GetInstance().GameTime.Year;
            switch (f.Type)
            {
                case ScenarioFailure.FailureType.Cash:
                    return all.Sum(a => a.Money);
                case ScenarioFailure.FailureType.Debt:
                    // what is still owed, without the future interest
                    return all.Sum(a => a.Loans.ToList().Where(l => l.IsActive).Sum(l => l.Balance));
                case ScenarioFailure.FailureType.Fleet:
                    return all.Sum(a => a.Fleet.Count);
                case ScenarioFailure.FailureType.Domestic:
                    return all.Sum(a => a.Airports.Count(x => x.Profile.Country == a.Profile.Country));
                case ScenarioFailure.FailureType.Intl:
                    return all.Sum(a => a.Airports.Count(x => x.Profile.Country != a.Profile.Country));
                case ScenarioFailure.FailureType.PaxGrowth:
                {
                    // last full year against the year before; not measurable until both are played
                    double before = all.Sum(a => Passengers(a, year - 2)), last = all.Sum(a => Passengers(a, year - 1));
                    if (before <= 0 || year - 2 < so.Scenario.StartYear) return null;
                    return (last - before)/before*100;
                }
                case ScenarioFailure.FailureType.FleetAge:
                {
                    var fleet = all.SelectMany(a => a.Fleet.ToList()).ToList();
                    return fleet.Count == 0 ? 0 : fleet.Average(x => x.Airliner.Age);
                }
                case ScenarioFailure.FailureType.Pax:
                    return all.Sum(a => a.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("Passengers")));
                case ScenarioFailure.FailureType.Bases:
                    return all.Sum(a => a.Airports.Count(x => x.GetCurrentAirportFacility(a, AirportFacility.FacilityType.Service).TypeLevel > 0));
                case ScenarioFailure.FailureType.JetRation:
                {
                    var fleet = all.SelectMany(a => a.Fleet.ToList()).ToList();
                    return fleet.Count == 0 ? 0 : (double) fleet.Count(x => x.Airliner.Type.Engine == AirlinerType.TypeOfEngine.Jet)/fleet.Count;
                }
                default:
                    return null;
            }
        }

        /// <summary>Whether a measured figure meets the goal.</summary>
        public static bool Passes(ScenarioObject so, ScenarioFailure f, double now)
        {
            double v = Value(f);
            switch (f.Type)
            {
                case ScenarioFailure.FailureType.Cash: return now > v;
                // debt may not grow above where the airline started
                case ScenarioFailure.FailureType.Debt: return now <= so.Scenario.StartDebt + v + 1;
                case ScenarioFailure.FailureType.Fleet:
                case ScenarioFailure.FailureType.Domestic:
                case ScenarioFailure.FailureType.Intl:
                case ScenarioFailure.FailureType.PaxGrowth:
                // "at least 3 bases" is written as more than 2 (the original check was the wrong way round)
                case ScenarioFailure.FailureType.Bases:
                    return now > v;
                case ScenarioFailure.FailureType.FleetAge: return now < v;
                case ScenarioFailure.FailureType.Pax: return now > v*1000;
                case ScenarioFailure.FailureType.JetRation: return now >= v;
                default: return true;
            }
        }

        /// <summary>The goal in plain words.</summary>
        public static string Describe(Scenario s, ScenarioFailure f)
        {
            double v = Value(f);
            int more = (int) Math.Floor(v) + 1;
            string home = s.Airline?.Profile.Country?.Name ?? "your home country";
            switch (f.Type)
            {
                case ScenarioFailure.FailureType.Cash: return "Cash above " + Money(v);
                case ScenarioFailure.FailureType.Debt: return "Debt no higher than " + Money(s.StartDebt + v) + (s.StartDebt > 0 ? " (what you start with)" : "");
                case ScenarioFailure.FailureType.Fleet: return "At least " + more + " aircraft";
                case ScenarioFailure.FailureType.Domestic: return "At least " + more + " airports in " + home;
                case ScenarioFailure.FailureType.Intl: return "At least " + more + " airports abroad";
                case ScenarioFailure.FailureType.PaxGrowth: return "Passengers growing more than " + v.ToString("0.#", CultureInfo.InvariantCulture) + "% a year";
                case ScenarioFailure.FailureType.FleetAge: return "Average fleet age under " + v.ToString("0.#", CultureInfo.InvariantCulture) + " years";
                case ScenarioFailure.FailureType.Pax: return "More than " + (v*1000).ToString("N0") + " passengers carried";
                case ScenarioFailure.FailureType.Bases: return "At least " + more + " bases (airports with a service centre)";
                case ScenarioFailure.FailureType.JetRation: return "At least " + Math.Round(v*100) + "% of the fleet jets";
                case ScenarioFailure.FailureType.Safety: return "Safety standards (not checked in this version)";
                case ScenarioFailure.FailureType.Security: return "Security standards (not checked in this version)";
                case ScenarioFailure.FailureType.Crime: return "No major crime (not checked in this version)";
                default: return f.Type.ToString();
            }
        }

        /// <summary>When the goal is checked.</summary>
        public static string Timing(Scenario s, ScenarioFailure f)
        {
            if (!IsMeasured(f.Type)) return "";
            if (f.CheckMonths <= 1)
                return f.MonthsOfFailure > 1 ? "every month; you lose after " + f.MonthsOfFailure + " months in a row of missing it" : "every month";
            return "checked in January " + (s.StartYear + f.CheckMonths/12);
        }

        private static string Format(ScenarioFailure f, double now)
        {
            switch (f.Type)
            {
                case ScenarioFailure.FailureType.Cash:
                case ScenarioFailure.FailureType.Debt: return Money(now);
                case ScenarioFailure.FailureType.PaxGrowth: return now.ToString("0.0") + "%";
                case ScenarioFailure.FailureType.FleetAge: return now.ToString("0.0") + " years";
                case ScenarioFailure.FailureType.JetRation: return (now*100).ToString("0") + "% jets";
                case ScenarioFailure.FailureType.Pax: return now.ToString("N0");
                default: return now.ToString("N0");
            }
        }

        /// <summary>All goals with where the airline stands now.</summary>
        public static List<ScenarioGoal> Goals(ScenarioObject so)
        {
            var list = new List<ScenarioGoal>();
            if (so?.Scenario == null) return list;
            foreach (var f in so.Scenario.Failures)
            {
                var g = new ScenarioGoal { Failure = f, Goal = Describe(so.Scenario, f), When = Timing(so.Scenario, f) };
                if (!IsMeasured(f.Type))
                {
                    g.Now = "";
                    g.Status = "not checked";
                }
                else
                {
                    double? now = Measure(so, f);
                    if (now == null)
                    {
                        g.Now = "not measured yet";
                        g.Status = "needs two full years";
                    }
                    else
                    {
                        g.Now = Format(f, now.Value);
                        g.Ok = Passes(so, f, now.Value);
                        var fo = so.GetScenarioFailure(f);
                        int streak = fo != null && MathHelpers.GetMonthsBetween(fo.LastFailureTime, GameObject.GetInstance().GameTime) <= 1 ? fo.Failures : 0;
                        if (g.Ok == true) g.Status = "on track";
                        else if (f.CheckMonths > 1) g.Status = "not met yet";
                        else if (f.MonthsOfFailure > 1) g.Status = "missed " + Math.Max(1, streak) + " of " + f.MonthsOfFailure + " months";
                        else g.Status = "not met";
                    }
                }
                list.Add(g);
            }
            return list;
        }

        /// <summary>Monthly: checks the goals that are due; ends the scenario when one is lost or the end year is reached.</summary>
        public static void Check(ScenarioObject so)
        {
            if (so?.Scenario == null || so.IsEnded) return;
            var game = GameObject.GetInstance();
            int months = MathHelpers.GetMonthsBetween(game.StartDate, game.GameTime);
            foreach (var f in so.Scenario.Failures.Where(x => IsMeasured(x.Type) && (x.CheckMonths <= 1 || x.CheckMonths == months)).ToList())
            {
                double? now = Measure(so, f);
                if (now == null || Passes(so, f, now.Value)) continue;
                bool lost;
                if (f.MonthsOfFailure <= 1 || f.CheckMonths > 1) lost = true;
                else
                {
                    var fo = so.GetScenarioFailure(f);
                    if (fo == null) { lost = true; }
                    else
                    {
                        fo.Failures = MathHelpers.GetMonthsBetween(fo.LastFailureTime, game.GameTime) == 1 ? fo.Failures + 1 : 1;
                        fo.LastFailureTime = game.GameTime;
                        lost = fo.Failures >= f.MonthsOfFailure;
                    }
                }
                if (lost)
                {
                    so.ScenarioFailed = f;
                    so.EndedOn = game.GameTime;
                    lock (game.NewsBox) game.NewsBox.AddNews(new News(News.NewsType.StandardNews, game.GameTime, "Scenario lost: " + so.Scenario.Name, f.FailureText));
                    return;
                }
            }
            if (game.GameTime.Year >= so.Scenario.EndYear)
            {
                so.IsSuccess = true;
                so.EndedOn = game.GameTime;
                lock (game.NewsBox) game.NewsBox.AddNews(new News(News.NewsType.StandardNews, game.GameTime, "Scenario won: " + so.Scenario.Name,
                    string.IsNullOrEmpty(so.Scenario.SuccessText) ? "You met every goal until " + so.Scenario.EndYear + "." : so.Scenario.SuccessText));
            }
        }

        /// <summary>Why a scenario can't be played with the loaded data, or null.</summary>
        public static string Problem(Scenario s)
        {
            if (s.Airline == null) return "its airline is missing from the game data";
            if (s.Homebase == null) return "its home airport is missing from the game data";
            return null;
        }

        /// <summary>Starts a scenario game (the game data must be loaded fresh). The clock starts paused once the demand exists.</summary>
        public static void Start(Scenario s)
        {
            ScenarioHelpers.SetupScenario(s);
        }
    }
}
