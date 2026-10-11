using System;
using System.Globalization;

namespace TheAirline.Infrastructure
{
    /// <summary>
    ///     Reads the dates in the game's XML data files, which are written as US month first dates such as
    ///     "05.22.1911 00:00:00". The old code relied on the machine's culture, which only worked on US systems.
    /// </summary>
    public static class DataParsing
    {
        private static readonly string[] Formats =
        {
            "M.d.yyyy H:mm:ss", "M.d.yyyy", "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm:ss tt", "M/d/yyyy",
            "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss"
        };

        public static DateTime ParseDate(string value, IFormatProvider ignored = null)
        {
            DateTime result;
            if (DateTime.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out result))
            {
                return result;
            }

            return DateTime.Parse(value, new CultureInfo("en-US", false));
        }
    }
}
