using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Statistics;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>One scheduled arrival or departure at an airport.</summary>
    public class AirportMovement
    {
        public Airline Airline { get; set; }
        public Route Route { get; set; }
        public Airport Other { get; set; }
        public bool IsDeparture { get; set; }
        public DayOfWeek Day { get; set; }
        public TimeSpan Time { get; set; }
        public string FlightCode { get; set; }
        public string Aircraft { get; set; }
    }

    /// <summary>One airline's numbers at an airport.</summary>
    public class AirportAirlineStats
    {
        public Airline Airline { get; set; }
        public int Routes { get; set; }
        public int DeparturesPerWeek { get; set; }
        public double PassengersThisYear { get; set; }
        public double PassengersLastYear { get; set; }
        public double ArrivalsThisYear { get; set; }
        public int PassengerGates { get; set; }
        public int CargoGates { get; set; }
    }

    /// <summary>Traffic, airline shares and the weekly schedule of an airport.</summary>
    public static class AirportStatsService
    {
        private static StatisticsType Type(string name)
        {
            return StatisticsTypes.GetStatisticsType(name);
        }

        public static double Passengers(Airport airport, int year)
        {
            var t = Type("Passengers");
            return t == null ? 0 : airport.Statistics.GetTotalValue(year, t);
        }

        public static double Arrivals(Airport airport, int year)
        {
            var t = Type("Arrivals");
            return t == null ? 0 : airport.Statistics.GetTotalValue(year, t);
        }

        /// <summary>Every airline that flies here or leases gates here, busiest first.</summary>
        public static List<AirportAirlineStats> Airlines(Airport airport)
        {
            int year = GameObject.GetInstance().GameTime.Year;
            var pax = Type("Passengers");
            var arr = Type("Arrivals");
            var routes = AirportHelpers.GetAirportRoutes(airport);
            var airlines = routes.Select(r => r.Airline)
                                 .Concat(airport.AirlineContracts.Select(c => c.Airline))
                                 .Where(a => a != null)
                                 .Distinct()
                                 .ToList();
            var list = new List<AirportAirlineStats>();
            foreach (var a in airlines)
            {
                var mine = routes.Where(r => r.Airline == a).ToList();
                list.Add(new AirportAirlineStats
                {
                    Airline = a,
                    Routes = mine.Count,
                    DeparturesPerWeek = mine.Sum(r => r.TimeTable.Entries.Count(e => e.DepartureAirport == airport)),
                    PassengersThisYear = pax == null ? 0 : airport.Statistics.GetStatisticsValue(year, a, pax),
                    PassengersLastYear = pax == null ? 0 : airport.Statistics.GetStatisticsValue(year - 1, a, pax),
                    ArrivalsThisYear = arr == null ? 0 : airport.Statistics.GetStatisticsValue(year, a, arr),
                    PassengerGates = airport.AirlineContracts.Where(c => c.Airline == a && c.TerminalType == Terminal.TerminalType.Passenger).Sum(c => c.NumberOfGates),
                    CargoGates = airport.AirlineContracts.Where(c => c.Airline == a && c.TerminalType == Terminal.TerminalType.Cargo).Sum(c => c.NumberOfGates)
                });
            }
            return list.OrderByDescending(s => s.PassengersThisYear).ThenByDescending(s => s.DeparturesPerWeek).ToList();
        }

        /// <summary>The destinations people from here travelled to most (this game's passenger statistics).</summary>
        public static List<KeyValuePair<Airport, long>> TopDestinations(Airport airport, int count)
        {
            return Airports.GetAllActiveAirports()
                           .Select(a => new KeyValuePair<Airport, long>(a, airport.GetDestinationPassengerStatistics(a)))
                           .Where(p => p.Value > 0)
                           .OrderByDescending(p => p.Value)
                           .Take(count)
                           .ToList();
        }

        private static TimeSpan FlightTime(Route route, RouteTimeTableEntry entry)
        {
            var type = entry.Airliner?.Airliner?.Type ?? route.GetAirliners().FirstOrDefault()?.Airliner?.Type;
            if (type != null) return route.GetFlightTime(type);
            double km = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            return TimeSpan.FromHours(km/750 + 0.5);
        }

        /// <summary>All the scheduled arrivals and departures here in a week.</summary>
        public static List<AirportMovement> Movements(Airport airport)
        {
            var list = new List<AirportMovement>();
            foreach (var route in AirportHelpers.GetAirportRoutes(airport))
            {
                foreach (var e in route.TimeTable.Entries.ToList())
                {
                    if (e?.Destination == null) continue;
                    string aircraft = e.Airliner?.Airliner?.Type?.Name ?? "";
                    if (e.DepartureAirport == airport)
                    {
                        list.Add(new AirportMovement
                        {
                            Airline = route.Airline, Route = route, Other = e.Destination.Airport, IsDeparture = true,
                            Day = e.Day, Time = e.Time, FlightCode = e.Destination.FlightCode, Aircraft = aircraft
                        });
                    }
                    else if (e.Destination.Airport == airport)
                    {
                        var arrival = e.Time + FlightTime(route, e);
                        int extraDays = (int) Math.Floor(arrival.TotalDays);
                        list.Add(new AirportMovement
                        {
                            Airline = route.Airline, Route = route, Other = e.DepartureAirport, IsDeparture = false,
                            Day = (DayOfWeek) (((int) e.Day + extraDays)%7), Time = TimeSpan.FromMinutes(Math.Floor(arrival.TotalMinutes%1440)),
                            FlightCode = e.Destination.FlightCode, Aircraft = aircraft
                        });
                    }
                }
            }
            return list.OrderBy(m => ((int) m.Day + 6)%7).ThenBy(m => m.Time).ToList();
        }

        /// <summary>Movements per hour of one day (24 numbers).</summary>
        public static int[] PerHour(IEnumerable<AirportMovement> movements, DayOfWeek day)
        {
            var hours = new int[24];
            foreach (var m in movements.Where(m => m.Day == day))
                hours[Math.Max(0, Math.Min(23, m.Time.Hours))]++;
            return hours;
        }

        /// <summary>The gates of each built terminal and who uses them.</summary>
        public static List<string> GateLines(Airport airport)
        {
            var lines = new List<string>();
            var now = GameObject.GetInstance().GameTime;
            foreach (var t in airport.Terminals.AirportTerminals.OrderBy(t => t.DeliveryDate))
            {
                string kind = t.Type == Terminal.TerminalType.Cargo ? "cargo" : "passenger";
                if (t.DeliveryDate > now)
                {
                    lines.Add(string.Format("{0} ({1}): {2} {3} gates, opens {4}", t.Name, kind, t.Gates.NumberOfGates, kind, t.DeliveryDate.ToShortDateString()));
                    continue;
                }
                string owner = t.Airline != null ? "owned by " + t.Airline.Profile.Name : "airport terminal";
                lines.Add(string.Format("{0} ({1}, {2}): {3} gates", t.Name, kind, owner, t.Gates.NumberOfDeliveredGates));
            }
            foreach (Terminal.TerminalType type in new[] { Terminal.TerminalType.Passenger, Terminal.TerminalType.Cargo })
            {
                int total = airport.Terminals.GetNumberOfGates(type);
                if (total == 0) continue;
                var users = airport.AirlineContracts.Where(c => c.TerminalType == type)
                                   .GroupBy(c => c.Airline)
                                   .Select(g => new { Airline = g.Key, Gates = g.Sum(c => c.NumberOfGates) })
                                   .OrderByDescending(x => x.Gates)
                                   .ToList();
                int free = airport.Terminals.GetFreeGates(type);
                lines.Add(string.Format(
                    "{0} gates: {1} in all, {2} free{3}",
                    type == Terminal.TerminalType.Cargo ? "Cargo" : "Passenger", total, free,
                    users.Count == 0 ? "" : "  |  " + string.Join(", ", users.Select(u => u.Airline.Profile.IATACode + " " + u.Gates))));
            }
            return lines;
        }
    }
}
