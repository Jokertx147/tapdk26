using System;
using System.Globalization;
using System.Linq;
using System.Xml;

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

        /// <summary>
        ///     Enum.Parse that ignores case, underscores and spaces: the data files write "Verysmall" and "Long_Haul_International"
        ///     where the code has VerySmall and LongHaulInternational.
        /// </summary>
        public static object ParseEnum(Type enumType, string value, bool ignoreCase = true)
        {
            var key = NormalizeName(value);
            foreach (var name in Enum.GetNames(enumType))
            {
                if (NormalizeName(name) == key)
                {
                    return Enum.Parse(enumType, name);
                }
            }

            return Enum.Parse(enumType, value.Trim(), true);
        }

        private static string NormalizeName(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        /// <summary>Parses the digits of a value such as "024" or a value polluted with stray characters.</summary>
        public static int ParseInt(string value)
        {
            var digits = new string(value.Where(c => char.IsDigit(c) || c == '-').ToArray());
            return int.Parse(digits, CultureInfo.InvariantCulture);
        }

        /// <summary>Reads an attribute without caring about its case. Returns null if it is missing.</summary>
        public static string AttributeValue(XmlElement element, string name)
        {
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value;
                }
            }
            return null;
        }
    }
}
