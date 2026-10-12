using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Environment;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    public enum OpsType
    {
        Regular,

        Business,

        Whole_Day
    }

    public enum ServiceTier
    {
        Basic,

        Standard,

        Premium
    }

    /// <summary>
    ///     What the player chose when creating a route from the advisory tab.
    /// </summary>
    public class NewRouteRequest
    {
        public Airport Origin { get; set; }

        public Airport Destination { get; set; }

        public ServiceTier Tier { get; set; }

        //null when the aircraft is assigned later
        public FleetAirliner Airliner { get; set; }

        public OpsType Operation { get; set; }

        public int TurnaroundMinutes { get; set; }

        public TimeSpan StartTime { get; set; }

        public int RoundTripsPerDay { get; set; }

        public bool StartAirliner { get; set; }
    }

    /// <summary>
    ///     Creates a passenger route, and optionally its daily schedule, in one go.
    /// </summary>
    public static class RouteCreator
    {
        public static readonly List<int> TurnaroundOptions = new List<int> { 30, 45, 60, 75, 90, 105, 120 };

        //the human airline's passenger aircraft that can fly between the two airports
        public static List<FleetAirliner> GetCandidateAirliners(Airport origin, Airport destination)
        {
            double distance = MathHelpers.GetDistance(origin, destination);
            long runway = Math.Min(origin.GetMaxRunwayLength(), destination.GetMaxRunwayLength());

            return GameObject.GetInstance()
                             .HumanAirline.Fleet.Where(
                                 f =>
                                 f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger
                                 || f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed)
                             .Where(f => f.Routes.Count == 0)
                             .Where(f => f.Airliner.Type.Range >= distance && f.Airliner.Type.MinRunwaylength <= runway)
                             .OrderBy(f => f.Name)
                             .ToList();
        }

        //true when a round trip takes so long that the schedule is made weekly instead of daily
        public static bool IsLongRoute(Airport origin, Airport destination, AirlinerType type, int turnaroundMinutes)
        {
            TimeSpan minFlightTime = MathHelpers.GetFlightTime(origin, destination, type).Add(new TimeSpan(0, turnaroundMinutes, 0));

            return minFlightTime.Hours >= 12 || minFlightTime.Days >= 1;
        }

        //how many round trips a day fit: 06:00-22:00 for a regular schedule, the whole day otherwise
        public static int GetMaxRoundTrips(Airport origin, Airport destination, AirlinerType type, int turnaroundMinutes, OpsType operation)
        {
            TimeSpan minFlightTime = MathHelpers.GetFlightTime(origin, destination, type).Add(new TimeSpan(0, turnaroundMinutes, 0));

            int maxHours = operation == OpsType.Whole_Day ? 24 : 16;

            return Math.Max(1, (int) Math.Floor(maxHours * 60 / (2 * minFlightTime.TotalMinutes)));
        }

        //the possible first departures for a number of round trips
        public static List<TimeSpan> GetStartTimes(Airport origin, Airport destination, AirlinerType type, int turnaroundMinutes, int roundTrips, OpsType operation)
        {
            TimeSpan minFlightTime = MathHelpers.GetFlightTime(origin, destination, type).Add(new TimeSpan(0, turnaroundMinutes, 0));

            int firstHour = operation == OpsType.Whole_Day ? 0 : 6;
            int lastHour = Math.Max(firstHour, (int) (22 - minFlightTime.TotalHours * roundTrips * 2));

            var times = new List<TimeSpan>();

            for (int hour = firstHour; hour <= lastHour; hour++)
            {
                times.Add(new TimeSpan(hour, 0, 0));
                times.Add(new TimeSpan(hour, 30, 0));
            }

            return times;
        }

        //creates the route (and its schedule); returns the route, or null with a message that says why not
        public static Route Create(NewRouteRequest request, out string message)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            Airport origin = request.Origin;
            Airport destination = request.Destination;

            if (airline.Routes.Exists(
                r =>
                (r.Destination1 == origin && r.Destination2 == destination)
                || (r.Destination1 == destination && r.Destination2 == origin)))
            {
                message = "You already have a route between these airports.";
                return null;
            }

            try
            {
                AirlineHelpers.IsRouteDestinationsOk(airline, origin, destination, Route.RouteType.Passenger, null, null);
            }
            catch (Exception ex)
            {
                message = "The route could not be created: " + DescribeError(ex.Message);
                return null;
            }

            int year = GameObject.GetInstance().GameTime.Year;
            double baseFare = PassengerHelpers.GetPassengerPrice(origin, destination);

            var route = new PassengerRoute(Guid.NewGuid().ToString(), origin, destination, GameObject.GetInstance().GameTime, 0);

            foreach (AirlinerClass.ClassType classType in AirlinerClass.GetAirlinerTypes())
            {
                if ((int) classType > year)
                {
                    continue;
                }

                RouteAirlinerClass routeClass = route.GetRouteAirlinerClass(classType);

                if (routeClass == null)
                {
                    continue;
                }

                routeClass.FarePrice = baseFare * GeneralHelpers.ClassToPriceFactor(classType);

                foreach (RouteFacility.FacilityType facilityType in Enum.GetValues(typeof (RouteFacility.FacilityType)))
                {
                    if (year < (int) facilityType || RouteFacilities.IsDiscontinued(facilityType, year))
                    {
                        continue;
                    }

                    RouteFacility facility = PickFacility(airline, facilityType, request.Tier);

                    if (facility != null)
                    {
                        routeClass.AddFacility(facility);
                    }
                }
            }

            FleetAirlinerHelpers.CreateStopoverRoute(route, null, null);
            route.Season = Weather.Season.AllYear;
            airline.AddRoute(route);

            message = string.Format("Route {0} - {1} created.", origin.Profile.IATACode, destination.Profile.IATACode);

            FleetAirliner airliner = request.Airliner;

            if (airliner == null)
            {
                message += " Assign an aircraft to it in the route planner to start flying.";
                return route;
            }

            List<string> codes = airline.GetFlightCodes();
            string flightCode1 = codes[0];
            string flightCode2 = codes[1];

            RouteTimeTable timeTable = AIHelpers.CreateAirlinerRouteTimeTable(
                route,
                airliner,
                request.RoundTripsPerDay,
                true,
                request.TurnaroundMinutes,
                request.StartTime,
                flightCode1,
                flightCode2);

            var existing = new List<RouteTimeTableEntry>(
                airliner.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == airliner)));

            if (!TimeTableHelpers.IsTimeTableValid(timeTable, airliner, existing, false))
            {
                message += " The schedule clashes with the flights " + airliner.Name
                           + " already has, so no flights were scheduled. Set the timetable in the route planner.";
                return route;
            }

            foreach (RouteTimeTableEntry entry in timeTable.Entries)
            {
                if (!TimeTableHelpers.IsRouteEntryInOccupied(entry, airliner))
                {
                    route.TimeTable.AddEntry(entry);
                }
            }

            airliner.AddRoute(route);

            if (airliner.Status != FleetAirliner.AirlinerStatus.Stopped)
            {
                message += " " + airliner.Name + " is scheduled on it.";
            }
            else if (request.StartAirliner)
            {
                if (airliner.Pilots.Count == airliner.Airliner.Type.CockpitCrew)
                {
                    airliner.Status = GameObject.GetInstance().DayRoundEnabled
                        ? FleetAirliner.AirlinerStatus.OnRoute
                        : FleetAirliner.AirlinerStatus.ToRouteStart;
                    message += " " + airliner.Name + " is flying it.";
                }
                else
                {
                    message += " " + airliner.Name + " has no full pilot crew yet, so it was not started.";
                }
            }
            else
            {
                message += " " + airliner.Name + " is scheduled but stopped.";
            }

            return route;
        }

        //true when the human airline has no passenger contract at the airport, so a contract can fix a missing gate
        public static bool NeedsContract(Airport airport)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;

            return !airport.GetAirlineContracts(airline).Any(c => c.TerminalType == Terminal.TerminalType.Passenger);
        }

        public static int GetFreePassengerGates(Airport airport)
        {
            return airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
        }

        //signs a passenger gate contract (up to two gates, two years) and takes the gates; same rules as the airport page
        public static void SignContract(Airport airport, AirportContract.ContractType contractType)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            DateTime now = GameObject.GetInstance().GameTime;

            int gates = Math.Min(2, GetFreePassengerGates(airport));

            if (contractType == AirportContract.ContractType.Full
                && airport.GetAirportFacility(airline, AirportFacility.FacilityType.CheckIn).TypeLevel == 0)
            {
                AirportFacility checkin =
                    AirportFacilities.GetFacilities(AirportFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);

                airport.AddAirportFacility(airline, checkin, now);
                AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -checkin.Price);
            }

            double yearlyPayment = AirportHelpers.GetYearlyContractPayment(airport, contractType, gates, 2);

            var contract = new AirportContract(
                airline, airport, contractType, Terminal.TerminalType.Passenger, now, gates, 2, yearlyPayment, true);

            AirportHelpers.AddAirlineContract(contract);

            for (int i = 0; i < gates; i++)
            {
                Gate gate = airport.Terminals.GetGates().First(g => g.Airline == null);
                gate.Airline = airline;
            }
        }

        public static string DescribeError(string code)
        {
            switch (code)
            {
                case "3000":
                    return "No free gates at one of the airports. You need an airport contract with free gates at both ends.";
                case "3001":
                    return "The distance is too short, or longer than any available aircraft can fly.";
                case "3002":
                    return "The route is not of an appropriate type for these airports.";
                case "3003":
                    return "Cargo is missing at the airports.";
                case "3004":
                    return "Your airline has no license for this route.";
                case "3005":
                    return "There are flight restrictions between these countries.";
                default:
                    return code;
            }
        }

        //the facility that fits a service tier: the cheapest for basic, the best for premium, one in the middle for standard
        private static RouteFacility PickFacility(Airline airline, RouteFacility.FacilityType type, ServiceTier tier)
        {
            List<RouteFacility> facilities =
                AirlineHelpers.GetRouteFacilities(airline, type).OrderBy(f => f.ServiceLevel).ToList();

            if (facilities.Count == 0)
            {
                return null;
            }

            switch (tier)
            {
                case ServiceTier.Basic:
                    return facilities.First();
                case ServiceTier.Premium:
                    return facilities.Last();
                default:
                    return facilities[(int) Math.Round((facilities.Count - 1) * 0.5)];
            }
        }
    }
}
