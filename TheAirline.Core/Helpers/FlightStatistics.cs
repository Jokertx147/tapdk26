using System;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Statistics;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     The game's statistics of the flights: the departures (counted at take-off; the landing's figures are counted
    ///     in FleetAirlinerHelpers.SetFlightStats), the on-time share, and how long they are kept.
    /// </summary>
    public static class FlightStatistics
    {
        /// <summary>A flight that lands this late or less is on time (as the monthly report counts it).</summary>
        public const int OnTimeMinutes = 15;

        /// <summary>Years of airport statistics kept: this one and the two before (an airport has a line per airline and figure).</summary>
        public const int AirportYears = 3;

        /// <summary>A flight takes off: a departure for the airline, the aircraft, the airport it leaves and its route.</summary>
        public static void TakeOff(FleetAirliner airliner)
        {
            Flight flight = airliner?.CurrentFlight;
            Airline airline = airliner?.Airliner?.Airline;
            if (flight?.Entry == null || airline == null) return;
            int year = GameObject.GetInstance().GameTime.Year;
            StatisticsType departures = StatisticsTypes.GetStatisticsType("Departures");
            airline.Statistics.AddStatisticsValue(year, departures, 1);
            airliner.Statistics.AddStatisticsValue(year, departures, 1);
            flight.Entry.DepartureAirport?.Statistics.AddStatisticsValue(year, airline, departures, 1);
            Route route = flight.Entry.TimeTable?.Route;
            if (route == null) return;
            if (flight.IsPassengerFlight() && route is PassengerRoute pr)
            {
                foreach (FlightAirlinerClass fac in flight.Classes.ToList())
                {
                    RouteAirlinerClass rac = fac?.AirlinerClass == null ? null : pr.GetRouteAirlinerClass(fac.AirlinerClass.Type);
                    if (rac != null) route.Statistics.AddStatisticsValue(rac, departures, 1);
                }
            }
            if (flight.IsCargoFlight()) route.Statistics.AddStatisticsValue(departures, 1);
        }

        /// <summary>
        ///     The airline's share of flights on time over all the years it has flown (adding the years' percentages, as the
        ///     ratings did, gave 180% after two years).
        /// </summary>
        public static double OnTimePercent(Airline airline)
        {
            double arrivals = airline.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("Arrivals"));
            if (arrivals <= 0) return 0;
            return 100*airline.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("On-Time"))/arrivals;
        }

        /// <summary>Drops the airport statistics older than the years kept.</summary>
        public static void PruneAirportStatistics()
        {
            int oldest = GameObject.GetInstance().GameTime.Year - (AirportYears - 1);
            foreach (Airport airport in Airports.GetAllAirports())
            {
                var stats = airport.Statistics?.Stats;
                if (stats == null) continue;
                lock (stats) stats.RemoveAll(s => s == null || s.Year < oldest);
            }
        }
    }
}
