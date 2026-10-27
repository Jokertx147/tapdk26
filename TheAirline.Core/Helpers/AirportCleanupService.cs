using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>An area a game can be set in: a whole continent or one region of it.</summary>
    public class GameArea
    {
        public string Name { get; set; }
        public HashSet<string> RegionUids { get; set; }
        public bool IsContinent { get; set; }
        public int Airports { get; set; }

        public bool Contains(Airport airport)
        {
            string uid = airport?.Profile?.Country?.Region?.Uid;
            return uid != null && RegionUids.Contains(uid);
        }
    }

    /// <summary>
    ///     Takes airports out of a game that is set in one continent or region. An earlier version added every airport of
    ///     the data files that a save didn't name when loading it, so a North America game came back as the whole world,
    ///     and saving it again kept the extra airports. A save doesn't say which area the game was started in (and the
    ///     airlines don't tell either: the opponents are the airlines nearest to the player, so a world game looks like
    ///     a regional one), so the player picks the area. Airports outside it that something uses are always kept.
    /// </summary>
    public static class AirportCleanupService
    {
        private static Continent ContinentOf(Region region)
        {
            if (region?.Uid == null) return null;
            return Continents.GetContinents().FirstOrDefault(c => c.Regions.Exists(r => r.Uid == region.Uid));
        }

        private static string RegionName(Region region)
        {
            return Regions.GetDisplayName(region) ?? region.Name ?? region.Uid;
        }

        /// <summary>The continents the game's airports are on (when there are several).</summary>
        public static int ContinentCount()
        {
            return Airports.GetAllAirports().Select(a => ContinentOf(a.Profile?.Country?.Region)).Where(c => c != null).Distinct().Count();
        }

        /// <summary>
        ///     The areas to choose from: each continent the game has airports on and, where a continent has airports in
        ///     several regions, each of those regions. The continent where the player's airline is comes first.
        /// </summary>
        public static List<GameArea> Areas()
        {
            var airports = Airports.GetAllAirports().ToList();
            var human = GameObject.GetInstance()?.HumanAirline;
            Continent home = ContinentOf(human?.Airports.FirstOrDefault()?.Profile.Country.Region ?? human?.Profile?.Country?.Region);

            HashSet<Airport> used = InUse();
            var list = new List<GameArea>();
            var byContinent = airports.GroupBy(a => ContinentOf(a.Profile?.Country?.Region)).Where(g => g.Key != null)
                                      .OrderByDescending(g => g.Key == home).ThenByDescending(g => g.Count());
            foreach (var continent in byContinent)
            {
                list.Add(new GameArea
                {
                    Name = (continent.Key.Name ?? continent.Key.Uid) + " (the whole continent)",
                    RegionUids = new HashSet<string>(continent.Key.Regions.Select(r => r.Uid)),
                    IsContinent = true,
                    Airports = continent.Count()
                });
                // regions only where the airlines are (the list would be long otherwise)
                if (!continent.Any(used.Contains)) continue;
                var regions = continent.GroupBy(a => a.Profile.Country.Region.Uid).ToList();
                if (regions.Count < 2) continue;
                foreach (var region in regions.OrderByDescending(r => r.Count()))
                {
                    list.Add(new GameArea
                    {
                        Name = RegionName(region.First().Profile.Country.Region) + " (region only)",
                        RegionUids = new HashSet<string> {region.Key},
                        Airports = region.Count()
                    });
                }
            }
            return list;
        }

        /// <summary>The airports something in the game refers to: airlines' airports, routes, aircraft, gates, hubs, facilities.</summary>
        public static HashSet<Airport> InUse()
        {
            var used = new HashSet<Airport>();
            foreach (Airline airline in Airlines.GetAllAirlines())
            {
                foreach (Airport a in airline.Airports.ToList()) used.Add(a);
                foreach (Route r in airline.Routes)
                {
                    used.Add(r.Destination1);
                    used.Add(r.Destination2);
                    foreach (var stop in r.Stopovers.ToList())
                    {
                        used.Add(stop.Stopover);
                        foreach (var leg in stop.Legs.ToList())
                        {
                            used.Add(leg.Destination1);
                            used.Add(leg.Destination2);
                        }
                    }
                }
                foreach (var f in airline.Fleet.ToList())
                {
                    used.Add(f.Homebase);
                    used.Add(f.CurrentPosition);
                    used.Add(f.FerryTo);
                    used.Add(f.CharterDestination);
                }
            }
            foreach (Airport a in Airports.GetAllAirports())
            {
                if (a.PlayerEdited || a.HasAirlineFacility() || a.AirlineContracts.Count > 0 || a.GetHubs().Count > 0) used.Add(a);
            }
            used.Remove(null);
            return used;
        }

        /// <summary>The airports outside the area that would be taken out, and how many outside it are kept because they are used.</summary>
        public static List<Airport> Outside(GameArea area, out int keptInUse)
        {
            HashSet<Airport> used = InUse();
            var outside = Airports.GetAllAirports().Where(a => !area.Contains(a)).ToList();
            keptInUse = outside.Count(used.Contains);
            return outside.Where(a => !used.Contains(a)).ToList();
        }

        /// <summary>
        ///     Takes out the unused airports outside the area and builds the passenger and cargo demand again for the
        ///     airports that are left. Returns how many were taken out. Run it with the clock stopped.
        /// </summary>
        public static int RemoveOutside(GameArea area)
        {
            int kept;
            var remove = new HashSet<Airport>(Outside(area, out kept));
            if (remove.Count == 0) return 0;

            Airports.RemoveAirports(remove.Contains);

            // the demand of the airports that are left still names the removed ones: build it again
            foreach (Airport airport in Airports.GetAllAirports())
            {
                airport.Statics = new AirportStatics(airport);
            }
            PassengerHelpers.CreateDestinationDemand();
            DemandOverrides.Apply();

            RouteIndex.Invalidate();
            ConnectionService.Invalidate();
            AIPricingService.Invalidate();
            Infrastructure.StartupLog.Write("Game area: " + remove.Count + " airports outside " + area.Name + " taken out, " + Airports.GetAllAirports().Count + " left");
            return remove.Count;
        }
    }
}
