using System;
using System.Collections.Generic;
using System.Linq;

namespace TheAirline.Models.Airports
{
    /// <summary>
    ///     Demand values the player has edited. The normal demand is generated again every time a game is loaded,
    ///     so the edits are kept here, written into the save and applied again after loading.
    /// </summary>
    public static class DemandOverrides
    {
        private static readonly object Lock = new object();

        // key: origin|destination|P or C
        private static readonly Dictionary<string, int> Values = new Dictionary<string, int>();

        public static void Clear()
        {
            lock (Lock)
            {
                Values.Clear();
            }
        }

        public static void Set(Airport origin, Airport destination, bool cargo, int value)
        {
            if (origin?.Profile == null || destination?.Profile == null)
            {
                return;
            }

            lock (Lock)
            {
                Values[Key(origin.Profile.IATACode, destination.Profile.IATACode, cargo)] = value;
            }
        }

        // the edits as text lines, one per edit, for the save file
        public static List<string> ToStrings()
        {
            lock (Lock)
            {
                return Values.Select(v => v.Key + "|" + v.Value).ToList();
            }
        }

        public static void Restore(IEnumerable<string> lines)
        {
            lock (Lock)
            {
                Values.Clear();

                if (lines == null)
                {
                    return;
                }

                foreach (string line in lines)
                {
                    int split = line?.LastIndexOf('|') ?? -1;
                    int value;

                    if (split > 0 && int.TryParse(line.Substring(split + 1), out value))
                    {
                        Values[line.Substring(0, split)] = value;
                    }
                }
            }
        }

        // puts the edited values back on the airports, after the demand has been generated
        public static void Apply()
        {
            List<KeyValuePair<string, int>> edits;

            lock (Lock)
            {
                edits = Values.ToList();
            }

            foreach (KeyValuePair<string, int> edit in edits)
            {
                string[] parts = edit.Key.Split('|');

                if (parts.Length != 3)
                {
                    continue;
                }

                Airport origin = Airports.GetAllAirports().FirstOrDefault(a => a.Profile.IATACode == parts[0]);
                Airport destination = Airports.GetAllAirports().FirstOrDefault(a => a.Profile.IATACode == parts[1]);

                if (origin?.Statics == null || destination == null)
                {
                    continue;
                }

                ushort rate = (ushort) Math.Max(0, Math.Min(ushort.MaxValue, edit.Value));

                if (parts[2] == "C")
                {
                    origin.Statics.SetCargoDemand(destination, rate);
                }
                else
                {
                    origin.Statics.SetPassengerDemand(destination, rate);
                }
            }
        }

        private static string Key(string origin, string destination, bool cargo)
        {
            return origin + "|" + destination + "|" + (cargo ? "C" : "P");
        }
    }
}
