using System;
using System.Globalization;
using System.IO;
using TheAirline.Infrastructure;

namespace TheAirline.Presentation
{
    /// <summary>How dates are shown: US (MM-DD-YYYY) or international (DD-MM-YYYY). Remembered between runs.</summary>
    public static class DateFormatter
    {
        private static bool? _us;

        /// <summary>Raised when the player changes the format, so the screens can show their dates again.</summary>
        public static event Action Changed;

        private static string SettingPath()
        {
            return Path.Combine(AppSettings.GetCommonApplicationDataPath(), "date-format.txt");
        }

        public static bool UseUs
        {
            get
            {
                if (_us == null)
                {
                    try
                    {
                        string p = SettingPath();
                        _us = !File.Exists(p) || File.ReadAllText(p).Trim() != "international";
                    }
                    catch (Exception)
                    {
                        _us = true;
                    }
                }

                return _us.Value;
            }
            set
            {
                if (_us == value)
                {
                    return;
                }

                _us = value;
                try
                {
                    File.WriteAllText(SettingPath(), value ? "us" : "international");
                }
                catch (Exception ex)
                {
                    StartupLog.Write("Could not store the date format: " + ex.Message);
                }

                Changed?.Invoke();
            }
        }

        public static string Pattern => UseUs ? "MM-dd-yyyy" : "dd-MM-yyyy";

        public static string Date(DateTime d)
        {
            return d.ToString(Pattern, CultureInfo.InvariantCulture);
        }

        public static string DateTime(DateTime d)
        {
            return Date(d) + " " + d.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static string Example => Date(new System.DateTime(2007, 3, 25));
    }
}
