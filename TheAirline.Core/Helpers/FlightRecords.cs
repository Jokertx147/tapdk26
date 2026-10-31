using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TheAirline.Models.Airliners;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>One flight an aircraft flew (or that was cancelled): what it carried and what it earned.</summary>
    public class FlownFlight
    {
        public RouteTimeTableEntry Entry { get; set; }
        public string FlightCode { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public DateTime Takeoff { get; set; }
        public DateTime Landing { get; set; }

        /// <summary>The minutes it left late.</summary>
        public int DelayMinutes { get; set; }

        public bool Cancelled { get; set; }
        public string CancelReason { get; set; }
        public bool IsCargo { get; set; }
        public int Passengers { get; set; }
        public int Seats { get; set; }
        public double Cargo { get; set; }
        public double Income { get; set; }

        /// <summary>Income less the flight's own costs (fuel, fees, crew, handling, maintenance, sales), before ownership.</summary>
        public double Profit { get; set; }
    }

    /// <summary>
    ///     Each aircraft's last flights, for the world map's flight board. Kept while the game runs (not saved: after
    ///     loading a game the board shows the flights from then on).
    /// </summary>
    public static class FlightRecords
    {
        public const int Keep = 60;

        private static readonly ConditionalWeakTable<FleetAirliner, List<FlownFlight>> Flights = new ConditionalWeakTable<FleetAirliner, List<FlownFlight>>();

        private static void Add(FleetAirliner airliner, FlownFlight f)
        {
            var list = Flights.GetValue(airliner, x => new List<FlownFlight>());
            lock (list)
            {
                list.Add(f);
                if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
            }
        }

        /// <summary>A flight landed: its income less its own costs.</summary>
        public static void Landed(FleetAirliner airliner, DateTime landing, double income, double profit)
        {
            try
            {
                Flight flight = airliner?.CurrentFlight;
                if (flight?.Entry == null) return;
                var f = new FlownFlight
                {
                    Entry = flight.Entry, FlightCode = flight.Entry.Destination.FlightCode,
                    From = flight.GetDepartureAirport()?.Profile.IATACode, To = flight.Entry.Destination.Airport?.Profile.IATACode,
                    Takeoff = flight.FlightTime, Landing = landing, Income = income, Profit = profit, IsCargo = flight.IsCargoFlight() && !flight.IsPassengerFlight()
                };
                f.DelayMinutes = Math.Max(0, (int) Math.Round((flight.FlightTime - flight.ScheduledFlightTime).TotalMinutes));
                if (flight.ScheduledFlightTime == default(DateTime)) f.DelayMinutes = 0;
                if (flight.IsPassengerFlight())
                {
                    f.Passengers = flight.GetTotalPassengers();
                    f.Seats = airliner.Airliner.GetTotalSeatCapacity();
                }
                else
                {
                    f.Cargo = flight.Cargo;
                }
                Add(airliner, f);
            }
            catch (Exception)
            {
                // a record that can't be made leaves the flight as it is
            }
        }

        /// <summary>A flight was cancelled before it left.</summary>
        public static void Cancelled(FleetAirliner airliner, Flight flight, string reason)
        {
            try
            {
                if (flight?.Entry == null) return;
                Add(airliner, new FlownFlight
                {
                    Entry = flight.Entry, FlightCode = flight.Entry.Destination.FlightCode,
                    From = flight.Entry.DepartureAirport?.Profile.IATACode, To = flight.Entry.Destination.Airport?.Profile.IATACode,
                    Takeoff = flight.FlightTime, Landing = flight.FlightTime, Cancelled = true, CancelReason = reason
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>The aircraft's recorded flights, oldest first.</summary>
        public static List<FlownFlight> Of(FleetAirliner airliner)
        {
            List<FlownFlight> list;
            if (airliner == null || !Flights.TryGetValue(airliner, out list)) return new List<FlownFlight>();
            lock (list) return list.ToList();
        }

        public static void Clear()
        {
            Flights.Clear();
        }
    }
}
