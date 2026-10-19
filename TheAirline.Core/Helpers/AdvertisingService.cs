using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Advertising. Each channel (newspaper, radio, TV, internet) is set to none, local, national or global and
    ///     costs money every month. Together they set the reputation the airline moves toward (50 with no
    ///     advertising), and a better reputation makes passengers choose the airline more often. Campaigns aim
    ///     extra advertising at one country or one airport for a few months.
    /// </summary>
    public static class AdvertisingService
    {
        public static readonly string[] CampaignSizes = { "Small", "Medium", "Large" };

        public static string ChannelName(AdvertisementType.AirlineAdvertisementType type)
        {
            switch (type)
            {
                case AdvertisementType.AirlineAdvertisementType.Newspaper: return "Newspapers";
                case AdvertisementType.AirlineAdvertisementType.Radio: return "Radio";
                case AdvertisementType.AirlineAdvertisementType.Tv: return "TV";
                default: return "Internet";
            }
        }

        /// <summary>The channels that exist in the current year (the internet from 1995, TV from 1950, ...).</summary>
        public static List<AdvertisementType.AirlineAdvertisementType> Channels()
        {
            int year = GameObject.GetInstance().GameTime.Year;
            return Enum.GetValues(typeof (AdvertisementType.AirlineAdvertisementType))
                       .Cast<AdvertisementType.AirlineAdvertisementType>()
                       .Where(t => year >= (int) t)
                       .ToList();
        }

        public static List<AdvertisementType> Levels(AdvertisementType.AirlineAdvertisementType channel)
        {
            return AdvertisementTypes.GetTypes(channel).OrderBy(t => t.ReputationLevel).ToList();
        }

        public static AdvertisementType Current(Airline airline, AdvertisementType.AirlineAdvertisementType channel)
        {
            return airline.Advertisements.ContainsKey(channel) ? airline.Advertisements[channel] : AdvertisementTypes.GetBasicType(channel);
        }

        public static void Set(Airline airline, AdvertisementType level)
        {
            airline.SetAirlineAdvertisement(level);
        }

        /// <summary>The sum of the levels of the channels in use (0 to 12).</summary>
        public static int TotalLevel(Airline airline)
        {
            return Channels().Sum(c => Current(airline, c).ReputationLevel);
        }

        public static double ChannelsMonthlyCost(Airline airline)
        {
            return Channels().Sum(c => Current(airline, c).Price);
        }

        public static double CampaignsMonthlyCost(Airline airline)
        {
            var now = GameObject.GetInstance().GameTime;
            return Campaigns(airline).Where(c => c.End > now).Sum(c => c.MonthlyCost);
        }

        /// <summary>The reputation the airline moves toward with its current advertising.</summary>
        public static int TargetReputation(Airline airline)
        {
            return Math.Min(100, 50 + 4*TotalLevel(airline));
        }

        /// <summary>How much more often passengers choose the airline because of its reputation: 0.85 at 0, 1 at 50, 1.15 at 100.</summary>
        public static double ReputationFactor(Airline airline)
        {
            return 0.85 + Math.Max(0, Math.Min(100, airline.Reputation))*0.003;
        }

        public static List<AdCampaign> Campaigns(Airline airline)
        {
            return airline.AdCampaigns ?? (airline.AdCampaigns = new List<AdCampaign>());
        }

        public static double CampaignBoost(int level)
        {
            return level >= 3 ? 0.35 : level == 2 ? 0.2 : 0.1;
        }

        /// <summary>The extra passenger share at an airport from running campaigns (1 when none applies).</summary>
        public static double CampaignFactor(Airline airline, Airport airport)
        {
            var list = airline.AdCampaigns;
            if (list == null || list.Count == 0) return 1;
            var now = GameObject.GetInstance().GameTime;
            double best = 0;
            foreach (var c in list)
            {
                if (c.End <= now || c.Start > now) continue;
                bool hits = c.IsAirport
                    ? c.Target == airport.Profile.IATACode
                    : airport.Profile.Country != null && c.Target == airport.Profile.Country.Uid;
                if (hits) best = Math.Max(best, CampaignBoost(c.Level));
            }
            return 1 + best;
        }

        /// <summary>The whole advertising effect on passengers choosing the airline at an airport.</summary>
        public static double AirportFactor(Airline airline, Airport airport)
        {
            return ReputationFactor(airline)*CampaignFactor(airline, airport);
        }

        /// <summary>The monthly price of a campaign: bigger targets and bigger campaigns cost more.</summary>
        public static double CampaignMonthlyCost(bool isAirport, string target, int level)
        {
            double size = 1;
            if (isAirport)
            {
                var a = Airports.GetAirport(target);
                if (a != null) size = 1 + 0.5*(int) a.Profile.Size;
            }
            else
            {
                var c = Countries.GetCountry(target);
                int airports = c == null ? 10 : Airports.GetAllAirports().Count(a => a.Profile.Country == c);
                size = 2 + Math.Min(8, airports/25.0);
            }
            double perLevel = level >= 3 ? 30000 : level == 2 ? 15000 : 6000;
            return GeneralHelpers.GetInflationPrice(perLevel*size);
        }

        public static string TargetName(AdCampaign c)
        {
            if (c.IsAirport)
            {
                var a = Airports.GetAirport(c.Target);
                return a == null ? c.Target : a.Profile.IATACode + " " + a.Profile.Name;
            }
            var country = Countries.GetCountry(c.Target);
            return country == null ? c.Target : country.Name;
        }

        public static bool StartCampaign(Airline airline, bool isAirport, string target, int level, int months, out string message)
        {
            if (string.IsNullOrEmpty(target))
            {
                message = "Pick a country or an airport.";
                return false;
            }
            if (months < 1 || months > 24 || level < 1 || level > 3)
            {
                message = "Pick a size and 1 to 24 months.";
                return false;
            }
            var now = GameObject.GetInstance().GameTime;
            if (Campaigns(airline).Any(c => c.End > now && c.IsAirport == isAirport && c.Target == target))
            {
                message = "A campaign there is already running. End it first or wait until it is over.";
                return false;
            }
            double monthly = CampaignMonthlyCost(isAirport, target, level);
            var campaign = new AdCampaign(target, isAirport, level, now, now.AddMonths(months), monthly);
            Campaigns(airline).Add(campaign);
            // the first month is paid now, the rest on the 1st of each month
            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.AirlineExpenses, -monthly);
            message = string.Format(
                "{0} campaign in {1} until {2}: +{3:0}% passenger share there, {4} a month.",
                CampaignSizes[level - 1], TargetName(campaign), campaign.End.ToShortDateString(), CampaignBoost(level)*100,
                new ValueCurrencyFormatter().Convert(monthly));
            return true;
        }

        public static void EndCampaign(Airline airline, AdCampaign campaign)
        {
            Campaigns(airline).Remove(campaign);
        }

        /// <summary>Monthly: pays the campaigns, removes finished ones and moves the reputation toward its target.</summary>
        public static void ProcessMonth(Airline airline)
        {
            var now = GameObject.GetInstance().GameTime;
            var list = Campaigns(airline);
            foreach (var c in list.ToList())
            {
                if (c.End <= now)
                {
                    list.Remove(c);
                    if (airline.IsHuman)
                    {
                        GameObject.GetInstance().NewsBox.AddNews(
                            new News(News.NewsType.AirlineNews, now, "Campaign over", "Your advertising campaign in " + TargetName(c) + " has ended."));
                    }
                    continue;
                }
                if (c.Start.Month != now.Month || c.Start.Year != now.Year)
                    AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.AirlineExpenses, -c.MonthlyCost);
            }

            int target = TargetReputation(airline);
            int rep = airline.Reputation;
            if (rep != target)
            {
                int step = Math.Max(1, Math.Abs(target - rep)/4);
                airline.Reputation = rep < target ? Math.Min(target, rep + step) : Math.Max(target, rep - step);
            }
        }
    }
}
