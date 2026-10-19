using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Runways the player pays for: a new runway of a chosen length (takes about a year), or a longer existing
    ///     runway (takes weeks, and the runway stays in use at its old length meanwhile).
    /// </summary>
    public static class RunwayService
    {
        public const long MinLength = 800;
        public const long MaxLength = 5500;
        public const long MinExtension = 100;
        public const int MaxRunways = 6;

        public static readonly Runway.SurfaceType[] Surfaces = { Runway.SurfaceType.Concrete, Runway.SurfaceType.Asphalt };

        private static double SizeFactor(Airport airport)
        {
            return 1 + 0.1*(int) airport.Profile.Size;
        }

        /// <summary>The price of a new runway (in base-year money, before inflation).</summary>
        public static double NewPrice(Airport airport, long length, Runway.SurfaceType surface)
        {
            double perMeter = surface == Runway.SurfaceType.Concrete ? 900 : 750;
            return GeneralHelpers.GetInflationPrice((300000 + perMeter*length)*SizeFactor(airport));
        }

        public static int NewDays(long length)
        {
            return 180 + (int) (length/15);
        }

        public static double ExtensionPrice(Airport airport, Runway runway, long newLength)
        {
            long added = Math.Max(0, newLength - runway.Length);
            double perMeter = runway.Surface == Runway.SurfaceType.Concrete ? 900 : 750;
            return GeneralHelpers.GetInflationPrice((80000 + perMeter*added)*SizeFactor(airport));
        }

        public static int ExtensionDays(long added)
        {
            return 20 + (int) (added/40);
        }

        public static string Status(Runway r)
        {
            var now = GameObject.GetInstance().GameTime;
            if (r.BuiltDate > now) return "under construction, ready " + r.BuiltDate.ToShortDateString();
            if (r.IsExtending) return "being extended to " + r.ExtendTo.ToString("N0") + " m, ready " + r.ExtendDate.ToShortDateString();
            return "in use";
        }

        public static string NextName(Airport airport)
        {
            int n = airport.Runways.Count + 1;
            string name;
            do
            {
                name = "New runway " + n++;
            } while (airport.Runways.Any(r => r.Name == name));
            return name;
        }

        public static string GetBuildBlocker(Airline airline, Airport airport, long length, string name)
        {
            if (length < MinLength || length > MaxLength) return "The length must be between " + MinLength.ToString("N0") + " and " + MaxLength.ToString("N0") + " m.";
            if (airport.Runways.Count(r => r.Type == Runway.RunwayType.Regular) >= MaxRunways) return airport.Profile.IATACode + " already has " + MaxRunways + " runways.";
            if (!string.IsNullOrWhiteSpace(name) && airport.Runways.Any(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return "There is already a runway called " + name.Trim() + ".";
            return null;
        }

        public static bool Build(Airline airline, Airport airport, long length, Runway.SurfaceType surface, string name, out string message)
        {
            message = GetBuildBlocker(airline, airport, length, name);
            if (message != null) return false;
            double price = NewPrice(airport, length, surface);
            var now = GameObject.GetInstance().GameTime;
            var ready = now.AddDays(NewDays(length));
            string n = string.IsNullOrWhiteSpace(name) ? NextName(airport) : name.Trim();
            airport.Runways.Add(new Runway(n, length, Runway.RunwayType.Regular, surface, ready, false) { BuiltBy = airline });
            airport.PlayerEdited = true;
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -price);
            message = string.Format(
                "{0} ({1:N0} m, {2}) ordered at {3} for {4}. Ready {5}.",
                n, length, surface.ToString().ToLower(), airport.Profile.IATACode, Money(price), ready.ToShortDateString());
            return true;
        }

        public static string GetExtendBlocker(Airport airport, Runway runway, long newLength)
        {
            if (runway == null) return "Pick a runway.";
            if (runway.Type != Runway.RunwayType.Regular) return "Helipads can't be extended.";
            if (runway.BuiltDate > GameObject.GetInstance().GameTime) return "The runway is still being built.";
            if (runway.IsExtending) return "The runway is already being extended.";
            if (newLength - runway.Length < MinExtension) return "Extend by at least " + MinExtension + " m (to " + (runway.Length + MinExtension).ToString("N0") + " m or more).";
            if (newLength > MaxLength) return "The longest runway allowed is " + MaxLength.ToString("N0") + " m.";
            return null;
        }

        public static bool Extend(Airline airline, Airport airport, Runway runway, long newLength, out string message)
        {
            message = GetExtendBlocker(airport, runway, newLength);
            if (message != null) return false;
            double price = ExtensionPrice(airport, runway, newLength);
            var now = GameObject.GetInstance().GameTime;
            runway.ExtendTo = newLength;
            runway.ExtendDate = now.AddDays(ExtensionDays(newLength - runway.Length));
            runway.BuiltBy = airline;
            airport.PlayerEdited = true;
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -price);
            message = string.Format(
                "{0} at {1} is being extended from {2:N0} m to {3:N0} m for {4}. Ready {5}; it stays in use until then.",
                runway.Name, airport.Profile.IATACode, runway.Length, newLength, Money(price), runway.ExtendDate.ToShortDateString());
            return true;
        }

        /// <summary>Finishes extensions and announces finished runways. Called once a day.</summary>
        public static void ProcessDaily()
        {
            var game = GameObject.GetInstance();
            var today = game.GameTime.Date;
            foreach (var airport in Airports.GetAllAirports().Where(a => a.PlayerEdited))
            {
                foreach (var r in airport.Runways.ToList())
                {
                    if (r.IsExtending && r.ExtendDate.Date <= today)
                    {
                        long old = r.Length;
                        r.Length = r.ExtendTo;
                        r.ExtendTo = 0;
                        if (r.BuiltBy != null && r.BuiltBy.IsHuman)
                            Post(airport, "Runway extended", string.Format("{0} at {1} is now {2:N0} m long (was {3:N0} m).", r.Name, airport.Profile.IATACode, r.Length, old));
                    }
                    if (r.BuiltBy != null && r.BuiltBy.IsHuman && r.BuiltDate.Date == today)
                        Post(airport, "New runway open", string.Format("{0} ({1:N0} m) at {2} is open.", r.Name, r.Length, airport.Profile.IATACode));
                }
            }
        }

        private static void Post(Airport airport, string subject, string body)
        {
            GameObject.GetInstance().NewsBox.AddNews(new News(News.NewsType.AirportNews, GameObject.GetInstance().GameTime, subject, body));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }
    }
}
