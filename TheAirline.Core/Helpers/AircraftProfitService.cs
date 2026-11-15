using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>What one aircraft earned in a month, and what it cost to have.</summary>
    public class AircraftFigures
    {
        public FleetAirliner Airliner { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }

        /// <summary>Its share of the profit of the routes it flies, before the aircraft itself is paid for.</summary>
        public double RouteShare { get; set; }

        /// <summary>What owning or leasing it costs for the month.</summary>
        public double Ownership { get; set; }

        /// <summary>The share of the routes' profit less what the aircraft costs: what it really made.</summary>
        public double Profit => RouteShare - Ownership;

        /// <summary>Flights a week it is rostered for.</summary>
        public int WeeklyFlights { get; set; }

        /// <summary>How full those flights were, averaged over its routes by the flights it flies on each.</summary>
        public double Load { get; set; }

        /// <summary>The routes it flies, as "ATL-MCO, ATL-LGA".</summary>
        public string Routes { get; set; }

        public bool Idle => WeeklyFlights == 0;
    }

    /// <summary>
    ///     Profit per aircraft. The game invoices a route, not an aircraft, so an aircraft is credited with the share of
    ///     each of its routes' profit that matches the flights it operates on that route: two aircraft splitting a route
    ///     evenly take half each. What the aircraft costs to own or lease is then charged to the aircraft itself, which
    ///     the route figures leave out, so the two together account for the money once.
    ///
    ///     An aircraft with no flights earns nothing and still costs its ownership, so it shows as a loss of exactly
    ///     what it costs to keep.
    /// </summary>
    public static class AircraftProfitService
    {
        public static AircraftFigures Figures(FleetAirliner f, int year, int month)
        {
            var figures = new AircraftFigures
            {
                Airliner = f,
                Name = f.Name,
                TypeName = f.Airliner.Type.Name,
                Ownership = OwnershipService.MonthlyCost(f)
            };

            var routes = f.Routes.ToList();
            double share = 0, loadWeight = 0, loaded = 0;
            int flights = 0;
            foreach (Route route in routes)
            {
                var entries = route.TimeTable.Entries.ToList();
                int mine = entries.Count(e => e.Airliner == f);
                if (mine == 0) continue;

                int all = Math.Max(mine, entries.Count);
                double profit = RouteProfitService.Month(route, year, month);
                share += profit*mine/all;
                flights += mine;

                double load = ConnectionService.MonthLoad(route);
                if (!double.IsNaN(load) && load > 0)
                {
                    loaded += load*mine;
                    loadWeight += mine;
                }
            }

            figures.RouteShare = share;
            figures.WeeklyFlights = flights;
            figures.Load = loadWeight > 0 ? loaded/loadWeight : 0;
            figures.Routes = string.Join(
                ", ",
                routes.Where(r => r.TimeTable.Entries.Any(e => e.Airliner == f))
                      .Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode));
            return figures;
        }

        /// <summary>Every delivered aircraft of the airline, for the month given.</summary>
        public static List<AircraftFigures> All(Airline airline, int year, int month)
        {
            return airline.DeliveredFleet.ToList().Select(f => Figures(f, year, month)).ToList();
        }

        /// <summary>Every delivered aircraft, for the last full month.</summary>
        public static List<AircraftFigures> LastMonth(Airline airline)
        {
            DateTime last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return All(airline, last.Year, last.Month);
        }
    }
}
