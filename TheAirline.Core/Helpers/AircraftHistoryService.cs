using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Statistics;

namespace TheAirline.Helpers
{
    /// <summary>One month of an aircraft's money.</summary>
    public class AircraftMonth
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double Income { get; set; }
        public double Costs { get; set; }
        public double Profit => Income + Costs;
        public Dictionary<string, double> Parts { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>One year of an aircraft's flying.</summary>
    public class AircraftYear
    {
        public int Year { get; set; }
        public double Flights { get; set; }
        public double Passengers { get; set; }
        public double Cargo { get; set; }
        public double LoadFactor { get; set; }
    }

    /// <summary>Per-aircraft history (money per month, flying per year) and comparison of aircraft types.</summary>
    public static class AircraftHistoryService
    {
        public static readonly string[] IncomeTypes = { "Tickets", "In-flight Services" };

        /// <summary>The last months of money, newest first (months without any value are left out).</summary>
        public static List<AircraftMonth> Months(FleetAirliner f, int count)
        {
            var values = f.Data?.Values?.ToList() ?? new List<OperatingValue>();
            return values.GroupBy(v => v.Year*100 + v.Month)
                         .OrderByDescending(g => g.Key)
                         .Take(count)
                         .Select(g => new AircraftMonth
                         {
                             Year = g.Key/100,
                             Month = g.Key%100,
                             Income = g.Where(v => IncomeTypes.Contains(v.Type)).Sum(v => v.Value),
                             Costs = g.Where(v => !IncomeTypes.Contains(v.Type)).Sum(v => v.Value),
                             Parts = g.GroupBy(v => v.Type).ToDictionary(x => x.Key, x => x.Sum(v => v.Value))
                         })
                         .ToList();
        }

        /// <summary>The profit of the last 12 months of records.</summary>
        public static double ProfitLast12(FleetAirliner f)
        {
            var now = GameObject.GetInstance().GameTime;
            int from = now.AddMonths(-11).Year*100 + now.AddMonths(-11).Month;
            return (f.Data?.Values?.ToList() ?? new List<OperatingValue>()).Where(v => v.Year*100 + v.Month >= from).Sum(v => v.Value);
        }

        private static double Stat(FleetAirliner f, int year, string type)
        {
            var t = StatisticsTypes.GetStatisticsType(type);
            return t == null || f.Statistics == null ? 0 : f.Statistics.GetStatisticsValue(year, t);
        }

        /// <summary>Flights, passengers and load factor for each year the aircraft has flown.</summary>
        public static List<AircraftYear> Years(FleetAirliner f)
        {
            var arrivals = StatisticsTypes.GetStatisticsType("Arrivals");
            var years = f.Statistics?.StatValues?.Where(s => s.Stat != null && s.Stat.Shortname == arrivals?.Shortname).Select(s => s.Year).Distinct().ToList()
                        ?? new List<int>();
            int now = GameObject.GetInstance().GameTime.Year;
            if (!years.Contains(now)) years.Add(now);
            int seats = f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo ? 0 : f.Airliner.GetTotalSeatCapacity();
            return years.OrderByDescending(y => y).Select(y =>
                {
                    double flights = Stat(f, y, "Arrivals");
                    double pax = Stat(f, y, "Passengers");
                    return new AircraftYear
                    {
                        Year = y,
                        Flights = flights,
                        Passengers = pax,
                        Cargo = Stat(f, y, "Cargo"),
                        LoadFactor = flights > 0 && seats > 0 ? pax/flights/seats : 0
                    };
                }).ToList();
        }

        public static string MonthName(int year, int month)
        {
            return new DateTime(year, Math.Max(1, Math.Min(12, month)), 1).ToString("MMM yyyy");
        }

        // ---- comparing aircraft types ----

        public static List<AirlinerType> AvailableTypes()
        {
            var now = GameObject.GetInstance().GameTime;
            return AirlinerTypes.GetTypes(t => t.Produced.From <= now && t.Produced.To >= now.AddYears(-30))
                                .OrderBy(t => t.Manufacturer?.Name).ThenBy(t => t.Name)
                                .ToList();
        }

        public static int Capacity(AirlinerType t)
        {
            if (t is AirlinerPassengerType p) return p.MaxSeatingCapacity;
            if (t is AirlinerCargoType c) return (int) c.CargoSize;
            return t.GetCapacity();
        }

        public static TimeSpan FlightTime(AirlinerType t, double km)
        {
            return TimeSpan.FromHours(km/Math.Max(100, t.CruisingSpeed) + 0.5);
        }

        /// <summary>The fuel cost of one flight with every seat (or all cargo space) filled, at today's average fuel price.</summary>
        public static double FuelPerFlight(AirlinerType t, double km)
        {
            double price = GameObject.GetInstance().FuelPrice;
            return price*km*Capacity(t)*t.FuelConsumption;
        }

        /// <summary>The fuel cost per seat (or per ton) and 100 km.</summary>
        public static double FuelPerSeat100(AirlinerType t)
        {
            return GameObject.GetInstance().FuelPrice*100*t.FuelConsumption;
        }

        /// <summary>Why the type can't fly the distance or use the airports, or null.</summary>
        public static string CanFly(AirlinerType t, double km, Airport a = null, Airport b = null)
        {
            if (km > t.Range) return "too far (range " + t.Range.ToString("N0") + " km)";
            foreach (var ap in new[] { a, b })
            {
                if (ap != null && ap.GetMaxRunwayLength() < t.MinRunwaylength)
                    return "runway at " + ap.Profile.IATACode + " too short (" + ap.GetMaxRunwayLength().ToString("N0") + " m, needs " + t.MinRunwaylength.ToString("N0") + " m)";
            }
            return null;
        }
    }
}
