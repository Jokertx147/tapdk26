using System.Collections.Generic;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;

namespace TheAirline.Helpers
{
    /// <summary>One landed flight's money, for checking the game's economy.</summary>
    public class FlightRecord
    {
        public Airline Airline;
        public string Type;
        public double Km, Hours;
        public int Seats, Passengers;
        public double Tickets, Fees, Meals, Fuel, Landing, Tax, CabinWages;
    }

    /// <summary>Keeps the last landed flights when switched on (used by the cost check, off in normal play).</summary>
    public static class FlightLog
    {
        public static bool Enabled;
        private static readonly List<FlightRecord> List = new List<FlightRecord>();

        public static void Add(FleetAirliner airliner, double km, double hours, double tickets, double fees, double meals, double fuel, double landing, double tax, double wages)
        {
            if (!Enabled) return;
            var r = new FlightRecord
            {
                Airline = airliner.Airliner.Airline, Type = airliner.Airliner.Type.Name, Km = km, Hours = hours,
                Seats = airliner.CurrentFlight.IsPassengerFlight() ? airliner.Airliner.GetTotalSeatCapacity() : 0, Passengers = airliner.CurrentFlight.IsPassengerFlight() ? airliner.CurrentFlight.GetTotalPassengers() : 0,
                Tickets = tickets, Fees = fees, Meals = meals, Fuel = fuel, Landing = landing, Tax = tax, CabinWages = wages
            };
            lock (List)
            {
                List.Add(r);
                if (List.Count > 200000) List.RemoveRange(0, 50000);
            }
        }

        public static List<FlightRecord> Records()
        {
            lock (List) return new List<FlightRecord>(List);
        }

        public static void Clear()
        {
            lock (List) List.Clear();
        }
    }
}
