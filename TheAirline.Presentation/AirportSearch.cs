using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airports;
using TheAirline.Models.General.Countries.Towns;

namespace TheAirline.Presentation
{
    /// <summary>Finding an airport by what a player would type, shared by the screens that search airports.</summary>
    public static class AirportSearch
    {
        /// <summary>
        ///     Whether the text matches the airport: its IATA code, its name, its town, its state (by name or short
        ///     name, so "Hawaii" and "HI" both find Kona and Hilo) or its country. Empty text matches everything.
        /// </summary>
        public static bool Matches(Airport a, string text)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return true;
            if (a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            Town town = a.Profile.Town;
            if (town == null) return false;
            if (town.Name != null && town.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (town.State != null
                && ((town.State.Name != null && town.State.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    || string.Equals(town.State.ShortName, s, StringComparison.OrdinalIgnoreCase))) return true;
            return a.Profile.Country != null && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The one airport a typed code or name means: an exact IATA code wins, else the first match by name.</summary>
        public static Airport Resolve(string text, IEnumerable<Airport> among = null)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return null;
            List<Airport> all = (among ?? Airports.GetAllAirports()).ToList();
            return all.FirstOrDefault(a => string.Equals(a.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase))
                   ?? all.OrderBy(a => a.Profile.Name).FirstOrDefault(a => Matches(a, s));
        }
    }
}
