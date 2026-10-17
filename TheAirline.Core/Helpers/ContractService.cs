using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>Special contracts: accepting an offer, which routes count for it and how it stands.</summary>
    public static class ContractService
    {
        public const int DaysToFulfil = 90;

        /// <summary>When the contract is judged: the end of the period for fixed contracts, otherwise some days after accepting.</summary>
        public static DateTime GetDeadline(SpecialContract sc)
        {
            return sc.Type.IsFixedDate ? sc.Type.Period.To : sc.Date;
        }

        /// <summary>Accepts an offered contract. The game judges it from the date stored in the contract, so a contract
        /// without a fixed period starts being judged after the days to fulfil it, not on the day it is accepted.</summary>
        public static SpecialContract Accept(Airline airline, SpecialContractType type)
        {
            DateTime date = type.IsFixedDate ? type.Period.From : GameObject.GetInstance().GameTime.AddDays(DaysToFulfil);
            var sc = new SpecialContract(type, date, airline);
            airline.SpecialContracts.Add(sc);
            GameObject.GetInstance().Contracts.Remove(type);

            foreach (Route r in airline.Routes.ToList())
            {
                LinkRoute(airline, r);
            }

            return sc;
        }

        public static void Decline(SpecialContractType type)
        {
            GameObject.GetInstance().Contracts.Remove(type);
        }

        public static bool Matches(SpecialContractRoute scr, Route route)
        {
            if (route.Type != scr.RouteType && !(scr.RouteType == Route.RouteType.Passenger && route.Type == Route.RouteType.Mixed))
            {
                return false;
            }

            return (route.Destination1 == scr.Departure && route.Destination2 == scr.Destination)
                   || (scr.BothWays && route.Destination2 == scr.Departure && route.Destination1 == scr.Destination);
        }

        /// <summary>Counts a route for the contracts of the airline that ask for it. Returns the names of those contracts.</summary>
        public static List<string> LinkRoute(Airline airline, Route route)
        {
            var linked = new List<string>();
            foreach (SpecialContract sc in airline.SpecialContracts)
            {
                if (sc.Routes.Contains(route) || !sc.Type.Routes.Any(r => Matches(r, route)))
                {
                    continue;
                }

                sc.Routes.Add(route);
                linked.Add(sc.Type.Name);
            }

            return linked;
        }

        public static string DescribeRoute(SpecialContractRoute r)
        {
            return string.Format(
                CultureInfo.InvariantCulture, "{0} {1} {2}  |  {3:N0} extra passengers a day",
                r.Departure.Profile.IATACode, r.BothWays ? "<->" : "->", r.Destination.Profile.IATACode, r.PassengersPerDay);
        }

        public static string DescribeRequirement(ContractRequirement c)
        {
            if (c.Type == ContractRequirement.RequirementType.ClassType)
            {
                return c.ClassType + " seats on the aircraft (at least " + c.MinSeats + ")";
            }

            return "Fly " + c.Departure?.Profile.IATACode + " to " + c.Destination?.Profile.IATACode;
        }

        /// <summary>Is the contract route flown today by a route that counts for the contract?</summary>
        public static bool IsServed(SpecialContract sc, SpecialContractRoute scr)
        {
            return sc.Routes.Any(r => r.HasAirliner && Matches(scr, r));
        }

        /// <summary>The airline's own routes that fit a contract route, whether or not they count yet.</summary>
        public static List<Route> FindOwnRoutes(Airline airline, SpecialContractRoute scr)
        {
            return airline.Routes.Where(r => Matches(scr, r)).ToList();
        }
    }
}
