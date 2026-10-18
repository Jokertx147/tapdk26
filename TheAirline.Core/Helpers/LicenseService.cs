using System;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>The airline's flying license: Regional, Domestic, then Long haul. A higher one is bought for money.</summary>
    public static class LicenseService
    {
        public static string Name(Airline.AirlineLicense license)
        {
            switch (AirlineHelpers.GetLicenseRank(license))
            {
                case 0: return "Regional";
                case 1: return "Domestic";
                default: return "Long haul";
            }
        }

        public static string Allows(Airline.AirlineLicense license)
        {
            switch (AirlineHelpers.GetLicenseRank(license))
            {
                case 0: return "Flights inside one continent or one union of countries.";
                case 1: return "The same, plus flights inside a country and flights shorter than 2,000 km between continents.";
                default: return "Flights anywhere, including long distance between continents.";
            }
        }

        public static bool IsHighest(Airline airline)
        {
            return AirlineHelpers.GetLicenseRank(airline.License) >= 2;
        }

        public static Airline.AirlineLicense Next(Airline airline)
        {
            return AirlineHelpers.GetNextLicense(airline.License);
        }

        public static double Price(Airline airline)
        {
            return GeneralHelpers.GetInflationPrice(AirlineHelpers.GetLicenseRank(airline.License) == 0 ? 3000000 : 15000000);
        }

        /// <summary>Why the next license cannot be bought now, or null.</summary>
        public static string GetBlocker(Airline airline)
        {
            if (IsHighest(airline)) return "You already have the highest license.";
            if (airline.Money < Price(airline)) return "Not enough money: it costs " + Price(airline).ToString("N0") + ".";
            return null;
        }

        public static bool Upgrade(Airline airline, out string message)
        {
            string blocker = GetBlocker(airline);
            if (blocker != null)
            {
                message = blocker;
                return false;
            }
            double price = Price(airline);
            var next = Next(airline);
            AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.AirlineExpenses, -price);
            airline.License = next;
            message = "Your airline now has a " + Name(next) + " license. " + Allows(next);
            return true;
        }
    }
}
