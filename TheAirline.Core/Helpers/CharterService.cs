using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>A one-off flight somebody wants to pay for: a group of passengers, or a load of cargo.</summary>
    public class CharterOffer
    {
        public int Id { get; set; }
        public bool IsCargo { get; set; }
        public int Amount { get; set; }
        public Airport From { get; set; }
        public Airport To { get; set; }
        public double Distance { get; set; }
        public double Fee { get; set; }
        public DateTime Deadline { get; set; }
        public string Customer { get; set; }

        public string Title => (IsCargo ? "Cargo charter: " : "Passenger charter: ") + From.Profile.IATACode + " - " + To.Profile.IATACode;
    }

    /// <summary>What it would take to fly an offer with one aircraft.</summary>
    public class CharterQuote
    {
        public FleetAirliner Aircraft { get; set; }
        public double FerryDistance { get; set; }
        public int Days { get; set; }
        public double Cost { get; set; }
        public double Profit { get; set; }
    }

    /// <summary>Charter flights: offers appear now and then, an idle aircraft can take one and is away until it is done.</summary>
    public static class CharterService
    {
        private const double PaxRate = 0.13;
        private const double CargoRate = 0.30;
        private static readonly List<CharterOffer> OffersList = new List<CharterOffer>();
        private static DateTime _lastRefresh = DateTime.MinValue;
        private static int _nextId = 1;
        // Random.Shared is safe to use from the parallel airline threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        private static readonly string[] Customers =
        {
            "a football club", "a tour operator", "a film crew", "a mining company", "a wedding party", "a school group", "a pharmaceutical firm",
            "a trade delegation", "an oil company", "a relief organisation", "a fashion house", "a car maker", "a choir on tour", "a flower exporter"
        };

        public static IReadOnlyList<CharterOffer> Offers => OffersList;

        public static bool IsOnCharter(FleetAirliner f)
        {
            return f.CharterUntil > GameObject.GetInstance().GameTime && f.CharterDestination != null;
        }

        /// <summary>Makes new offers about every week and drops the ones that ran out.</summary>
        public static void Refresh(bool force = false)
        {
            GameObject game = GameObject.GetInstance();
            Airline airline = game.HumanAirline;
            if (airline == null) return;

            OffersList.RemoveAll(o => o.Deadline < game.GameTime);
            if (!force && (game.GameTime - _lastRefresh).TotalDays < 7 && _lastRefresh != DateTime.MinValue) return;
            _lastRefresh = game.GameTime;

            var origins = new List<Airport>();
            foreach (FleetAirliner f in airline.Fleet)
            {
                if (f.CurrentPosition != null && !origins.Contains(f.CurrentPosition)) origins.Add(f.CurrentPosition);
            }
            foreach (Airport a in airline.Airports)
            {
                if (!origins.Contains(a)) origins.Add(a);
            }
            if (origins.Count == 0) return;

            List<Airport> all = Airports.GetAllActiveAirports().Where(a => a.GetMaxRunwayLength() >= 1500).ToList();
            if (all.Count < 2) return;

            int wanted = Rnd.Next(2, 6);
            for (int i = 0; i < wanted && OffersList.Count < 12; i++)
            {
                Airport from = origins[Rnd.Next(origins.Count)];
                Airport to = null;
                double km = 0;
                for (int tries = 0; tries < 40; tries++)
                {
                    Airport c = all[Rnd.Next(all.Count)];
                    if (c == from) continue;
                    km = MathHelpers.GetDistance(from, c);
                    if (km >= 200 && km <= 8000)
                    {
                        to = c;
                        break;
                    }
                }
                if (to == null) continue;

                bool cargo = Rnd.Next(4) == 0;
                int amount = cargo ? Rnd.Next(5, 90) : Rnd.Next(15, 380);
                double rate = (cargo ? CargoRate : PaxRate)*(0.9 + Rnd.NextDouble()*0.3);
                double fee = GeneralHelpers.GetInflationPrice(Math.Round(amount*km*rate/100)*100);
                OffersList.Add(
                    new CharterOffer
                    {
                        Id = _nextId++, IsCargo = cargo, Amount = amount, From = from, To = to, Distance = km, Fee = fee,
                        Deadline = game.GameTime.AddDays(Rnd.Next(7, 22)).Date, Customer = Customers[Rnd.Next(Customers.Length)]
                    });
            }
        }

        /// <summary>Adds an offer by hand (used by tests).</summary>
        public static void AddOffer(CharterOffer o)
        {
            o.Id = _nextId++;
            OffersList.Add(o);
        }

        public static void Decline(CharterOffer o)
        {
            OffersList.Remove(o);
        }

        public static void Clear()
        {
            OffersList.Clear();
            _lastRefresh = DateTime.MinValue;
        }

        public static int CapacityOf(FleetAirliner f)
        {
            var pax = f.Airliner.Type as AirlinerPassengerType;
            if (pax != null) return pax.MaxSeatingCapacity;
            var cargo = f.Airliner.Type as AirlinerCargoType;
            return cargo != null ? (int) cargo.CargoSize : 0;
        }

        /// <summary>Why this aircraft cannot take the offer, or null when it can.</summary>
        public static string GetBlocker(FleetAirliner f, CharterOffer o)
        {
            GameObject game = GameObject.GetInstance();
            if (f.Airliner.BuiltDate > game.GameTime) return "not delivered yet";
            if (f.Routes.Count > 0) return "has routes";
            if (IsOnCharter(f)) return "already on a charter";
            if (AircraftDealService.IsLeasedOut(f)) return "leased out";
            if (FerryService.IsFerrying(f)) return "on a ferry flight";
            if (MaintenanceService.IsGrounded(f)) return "grounded";
            if (MaintenanceService.IsCovering(f) || MaintenanceService.IsCovered(f)) return "tied up in a maintenance cover";
            if ((f.Airliner.Type is AirlinerCargoType) != o.IsCargo) return o.IsCargo ? "not a cargo aircraft" : "not a passenger aircraft";
            if (CapacityOf(f) < o.Amount) return "too small (" + CapacityOf(f) + ")";
            if (f.Airliner.Type.Range < o.Distance) return "range too short";
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) return "no full crew";
            if (f.CurrentPosition == null) return "position unknown";
            if (f.Airliner.Type.Range < MathHelpers.GetDistance(f.CurrentPosition, o.From)) return "too far from the pick-up";
            if (f.Airliner.Type.MinRunwaylength > o.From.GetMaxRunwayLength() || f.Airliner.Type.MinRunwaylength > o.To.GetMaxRunwayLength())
                return "runway too short";
            return null;
        }

        public static CharterQuote Quote(FleetAirliner f, CharterOffer o)
        {
            double ferry = MathHelpers.GetDistance(f.CurrentPosition, o.From);
            double speed = Math.Max(300, f.Airliner.Type.CruisingSpeed);
            double hours = (ferry + o.Distance)/speed + (ferry > 1 ? 4 : 2);
            int days = Math.Max(1, (int) Math.Ceiling(hours/20.0));
            double price = AirportHelpers.GetFuelPrice(o.From);
            double fuel = price*(ferry + o.Distance)*f.Airliner.FuelConsumption*CapacityOf(f)*0.55 + price*o.Distance*f.Airliner.FuelConsumption*o.Amount*0.45;
            double cost = Math.Round(fuel*1.15 + days*GeneralHelpers.GetInflationPrice(1500));
            return new CharterQuote {Aircraft = f, FerryDistance = ferry, Days = days, Cost = cost, Profit = o.Fee - cost};
        }

        public static List<CharterQuote> Candidates(Airline airline, CharterOffer o)
        {
            return airline.Fleet.Where(f => GetBlocker(f, o) == null).Select(f => Quote(f, o)).OrderByDescending(q => q.Profit).ToList();
        }

        public static bool Accept(Airline airline, CharterOffer o, FleetAirliner f, out string message)
        {
            GameObject game = GameObject.GetInstance();
            if (!OffersList.Contains(o))
            {
                message = "That offer is no longer open.";
                return false;
            }
            string blocker = GetBlocker(f, o);
            if (blocker != null)
            {
                message = f.Name + " cannot do it: " + blocker + ".";
                return false;
            }
            CharterQuote q = Quote(f, o);
            f.CharterUntil = game.GameTime.Date.AddDays(q.Days);
            f.CharterPay = o.Fee;
            f.CharterCost = q.Cost;
            f.CharterDestination = o.To;
            f.CharterText = o.Title + " for " + o.Customer;
            f.Status = FleetAirliner.AirlinerStatus.OnCharter;
            OffersList.Remove(o);
            message = f.Name + " is off on the charter and is back in " + q.Days + " days, landing at " + o.To.Profile.IATACode + ".";
            return true;
        }

        /// <summary>Daily: aircraft that finished their charter get paid and land at the destination.</summary>
        public static List<string> ProcessDaily(Airline airline)
        {
            GameObject game = GameObject.GetInstance();
            var done = new List<string>();
            foreach (FleetAirliner f in airline.Fleet.ToList())
            {
                if (f.CharterDestination == null || f.CharterUntil > game.GameTime) continue;

                AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.Tickets, f.CharterPay);
                AirlineHelpers.AddAirlineInvoice(airline, game.GameTime, Invoice.InvoiceType.FlightExpenses, -f.CharterCost);
                double km = f.CurrentPosition != null ? MathHelpers.GetDistance(f.CurrentPosition, f.CharterDestination) : 0;
                f.Airliner.Flown += km;
                f.Airliner.Condition = Math.Max(1, f.Airliner.Condition - Math.Min(3, km/4000.0));
                f.CurrentPosition = f.CharterDestination;
                done.Add(f.Name + ": " + f.CharterText + ", earned " + f.CharterPay.ToString("N0") + ", costs " + f.CharterCost.ToString("N0"));
                f.CharterDestination = null;
                f.CharterText = null;
                f.CharterPay = 0;
                f.CharterCost = 0;
                f.Status = FleetAirliner.AirlinerStatus.Stopped;
            }
            if (done.Count > 0 && airline == game.HumanAirline)
            {
                game.NewsBox.AddNews(new News(News.NewsType.AirlineNews, game.GameTime, "Charter flights completed", string.Join("\n", done)));
            }
            return done;
        }
    }
}
