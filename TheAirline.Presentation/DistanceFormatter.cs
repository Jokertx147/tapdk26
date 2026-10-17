using System;
using System.Globalization;
using System.IO;
using TheAirline.Infrastructure;

namespace TheAirline.Presentation
{
    /// <summary>How distances and speeds are shown: kilometres or miles. The game itself always works in kilometres.</summary>
    public static class DistanceFormatter
    {
        private const double MilesPerKm = 0.621371192;
        private const double FeetPerMetre = 3.280839895;

        private static bool? _miles;

        /// <summary>Raised when the player changes the unit, so the screens can show their texts again.</summary>
        public static event Action Changed;

        private static string SettingPath()
        {
            return Path.Combine(AppSettings.GetCommonApplicationDataPath(), "distance-unit.txt");
        }

        public static bool UseMiles
        {
            get
            {
                if (_miles == null)
                {
                    try
                    {
                        string p = SettingPath();
                        _miles = File.Exists(p) && File.ReadAllText(p).Trim() == "miles";
                    }
                    catch (Exception)
                    {
                        _miles = false;
                    }
                }

                return _miles.Value;
            }
            set
            {
                if (_miles == value)
                {
                    return;
                }

                _miles = value;
                try
                {
                    File.WriteAllText(SettingPath(), value ? "miles" : "kilometres");
                }
                catch (Exception ex)
                {
                    StartupLog.Write("Could not store the distance unit: " + ex.Message);
                }

                Changed?.Invoke();
            }
        }

        /// <summary>A distance given in kilometres, e.g. "1,234 km" or "767 mi".</summary>
        public static string Km(double km)
        {
            return UseMiles
                ? (km * MilesPerKm).ToString("N0", CultureInfo.InvariantCulture) + " mi"
                : km.ToString("N0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>A speed given in km/h, e.g. "850 km/h" or "528 mph".</summary>
        public static string Speed(double kmh)
        {
            return UseMiles
                ? (kmh * MilesPerKm).ToString("N0", CultureInfo.InvariantCulture) + " mph"
                : kmh.ToString("N0", CultureInfo.InvariantCulture) + " km/h";
        }

        /// <summary>A length given in metres (runways), e.g. "2,500 m" or "8,202 ft".</summary>
        public static string Metres(double m)
        {
            return UseMiles
                ? (m * FeetPerMetre).ToString("N0", CultureInfo.InvariantCulture) + " ft"
                : m.ToString("N0", CultureInfo.InvariantCulture) + " m";
        }
    }
}
