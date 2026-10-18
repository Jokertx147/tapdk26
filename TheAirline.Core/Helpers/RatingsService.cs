using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.General.Statistics;
using TheAirline.Models.Pilots;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>One reason a rating is higher or lower than the neutral 50.</summary>
    public class RatingFactor
    {
        public RatingFactor(string text, double points)
        {
            Text = text;
            Points = points;
        }

        public string Text { get; }
        public double Points { get; }
    }

    /// <summary>Where one rating is heading this month, and why.</summary>
    public class RatingTarget
    {
        public RatingTarget(string name)
        {
            Name = name;
            Factors = new List<RatingFactor>();
        }

        public string Name { get; }
        public double Base { get; set; } = 50;
        public List<RatingFactor> Factors { get; }
        public string Tip { get; set; }

        public int Value => (int) Math.Round(Math.Max(0, Math.Min(100, Base + Factors.Sum(f => f.Points))));

        public void Add(string text, double points)
        {
            if (Math.Abs(points) >= 0.5) Factors.Add(new RatingFactor(text, Math.Round(points, 1)));
        }
    }

    /// <summary>
    ///     The five airline ratings (customer happiness, employee happiness, maintenance, safety, security).
    ///     The original game filled them only from random events, which were switched off, so every airline
    ///     stayed at 50. They are now worked out from what the airline does, once a month, and move a third
    ///     of the way to the new value each month so one bad month does not wipe out a good record.
    /// </summary>
    public static class RatingsService
    {
        public const string Customer = "Customer happiness";
        public const string Employee = "Employee happiness";
        public const string Maintenance = "Maintenance";
        public const string Safety = "Safety";
        public const string Security = "Security";

        public static readonly string[] Names = { Customer, Employee, Maintenance, Safety, Security };

        private static double Clamp(double v, double min, double max)
        {
            return Math.Max(min, Math.Min(max, v));
        }

        private static bool HasFacility(Airline airline, string uid)
        {
            return airline.Facilities != null && airline.Facilities.Any(f => f != null && f.Uid == uid);
        }

        public static int Get(Airline airline, string name)
        {
            var r = airline.Ratings;
            switch (name)
            {
                case Customer: return r.CustomerHappinessRating;
                case Employee: return r.EmployeeHappinessRating;
                case Maintenance: return r.MaintenanceRating;
                case Safety: return r.SafetyRating;
                default: return r.SecurityRating;
            }
        }

        public static int GetLastMonth(Airline airline, string name)
        {
            var r = airline.Ratings;
            switch (name)
            {
                case Customer: return r.LastCustomerHappiness;
                case Employee: return r.LastEmployeeHappiness;
                case Maintenance: return r.LastMaintenance;
                case Safety: return r.LastSafety;
                default: return r.LastSecurity;
            }
        }

        /// <summary>The average of the five ratings.</summary>
        public static int Overall(Airline airline)
        {
            return (int) Math.Round(Names.Average(n => (double) Get(airline, n)));
        }

        public static string Grade(int value)
        {
            if (value >= 85) return "Excellent";
            if (value >= 70) return "Good";
            if (value >= 50) return "Fair";
            if (value >= 35) return "Weak";
            return "Poor";
        }

        private static List<FleetAirliner> Fleet(Airline airline)
        {
            return airline.Fleet.ToList().Where(f => f?.Airliner != null).ToList();
        }

        private static double OverdueShare(Airline airline, List<FleetAirliner> fleet)
        {
            // only the player's airline uses the maintenance check system
            if (!airline.IsHuman || fleet.Count == 0) return 0;
            int overdue = 0;
            foreach (var f in fleet)
            {
                try
                {
                    if (Enum.GetValues(typeof (MaintenanceCheck)).Cast<MaintenanceCheck>().Any(c => MaintenanceService.IsOverdue(f, c)))
                        overdue++;
                }
                catch
                {
                    // a check that cannot be worked out counts as fine
                }
            }
            return (double) overdue/fleet.Count;
        }

        public static RatingTarget CustomerTarget(Airline airline)
        {
            var t = new RatingTarget(Customer);
            var year = GameObject.GetInstance().GameTime.Year;

            double flights = airline.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("Arrivals"));
            if (flights > 0)
            {
                double otp = airline.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("On-Time%"));
                t.Add(string.Format("On-time flights {0:0}% this year", otp), Clamp((otp - 80)*0.6, -15, 12));
            }

            var routes = airline.Routes.ToList().OfType<PassengerRoute>().ToList();
            if (routes.Count > 0)
            {
                double shares = 0, priceRatios = 0;
                int priced = 0;
                foreach (var r in routes)
                {
                    var cls = r.GetRouteAirlinerClass(AirlinerClass.ClassType.EconomyClass);
                    if (cls == null) continue;
                    var facilities = cls.GetFacilities();
                    double max = facilities.Select(f => f.Type).Distinct()
                                           .Sum(type => RouteFacilities.GetFacilities(type).Select(x => x.ServiceLevel).DefaultIfEmpty(0).Max());
                    double have = facilities.Sum(f => f.ServiceLevel);
                    shares += max > 0 ? Clamp(have/max, 0, 1) : 0.5;

                    double suggested = PassengerHelpers.GetPassengerPrice(r.Destination1, r.Destination2);
                    if (suggested > 0 && cls.FarePrice > 0)
                    {
                        priceRatios += cls.FarePrice/suggested;
                        priced++;
                    }
                }
                double service = shares/routes.Count;
                t.Add(string.Format("On-board service is {0:0}% of the best available", service*100), Clamp((service - 0.4)*30, -12, 18));
                if (priced > 0)
                {
                    double ratio = priceRatios/priced;
                    t.Add(string.Format("Economy fares are {0:0}% of the usual price", ratio*100), Clamp((1 - ratio)*25, -15, 10));
                }
            }

            double fac = 0;
            if (HasFacility(airline, "100")) fac += 4; // frequent flyer
            if (HasFacility(airline, "105")) fac += 2; // online booking
            if (HasFacility(airline, "106")) fac += 2; // online check-in
            if (HasFacility(airline, "107")) fac += 3; // wi-fi
            if (HasFacility(airline, "108")) fac += 3; // VIP
            if (HasFacility(airline, "101")) fac += 1; // magazine
            if (HasFacility(airline, "102")) fac += 1; // newspapers
            t.Add("Airline services (frequent flyer, online booking, Wi-Fi, ...)", fac);

            var fleet = Fleet(airline).Where(f => f.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger).ToList();
            if (fleet.Count > 0)
            {
                double age = fleet.Average(f => f.Airliner.Age);
                t.Add(string.Format("Passenger fleet is {0:0.#} years old on average", age), Clamp((10 - age)*0.6, -8, 4));
            }

            t.Tip = "Fly on time, add food, drinks and entertainment on your routes (Routes > service), keep fares near the usual price and add airline services such as a frequent flyer program.";
            return t;
        }

        public static RatingTarget EmployeeTarget(Airline airline)
        {
            var t = new RatingTarget(Employee);

            if (airline.Pilots.Count > 0)
            {
                double morale = PilotMoraleService.Average(airline);
                t.Add(string.Format("Pilot morale {0:0} ({1})", morale, PilotMoraleService.MoodText(morale)), Clamp((morale - 65)*0.5, -20, 15));
            }

            var wages = FeeTypes.GetTypes(FeeType.EFeeType.Wage).Where(w => !w.Name.StartsWith("Cockpit")).ToList();
            if (wages.Count > 0 && airline.Fees != null)
            {
                double ratio = wages.Average(w => w.DefaultValue > 0 ? airline.Fees.GetValue(w)/w.DefaultValue : 1);
                t.Add(string.Format("Crew and staff wages are {0:0}% of normal", ratio*100), Clamp((ratio - 1)*40, -20, 15));
            }

            if (airline.Money < 0) t.Add("The airline is in debt", -8);

            t.Tip = "Pay pilots well (Staff > Pilots > pay), keep cabin, maintenance and support wages at or above normal (Finances > fees), and keep pilots flying rather than sitting unassigned.";
            return t;
        }

        public static RatingTarget MaintenanceTarget(Airline airline)
        {
            var t = new RatingTarget(Maintenance);
            var fleet = Fleet(airline);
            if (fleet.Count == 0)
            {
                t.Add("No aircraft yet", 0);
                t.Tip = "Buy aircraft first.";
                return t;
            }

            double condition = fleet.Average(f => f.Airliner.Condition);
            t.Base = 0;
            t.Add(string.Format("Fleet condition {0:0}% on average", condition), condition);

            double overdue = OverdueShare(airline, fleet);
            t.Add(string.Format("{0:0}% of aircraft have an overdue check", overdue*100), -overdue*30);

            if (HasFacility(airline, "110")) t.Add("Large maintenance center", 6);
            else if (HasFacility(airline, "109")) t.Add("Basic maintenance center", 3);

            t.Tip = "Do the A, B, C and D checks on time (My Airline > Maintenance, or turn on automatic checks) and build a maintenance center.";
            return t;
        }

        public static RatingTarget SafetyTarget(Airline airline)
        {
            var t = new RatingTarget(Safety);
            var fleet = Fleet(airline);
            t.Base = 55;
            if (fleet.Count > 0)
            {
                double condition = fleet.Average(f => f.Airliner.Condition);
                t.Add(string.Format("Fleet condition {0:0}%", condition), Clamp((condition - 70)*0.5, -20, 15));

                double age = fleet.Average(f => f.Airliner.Age);
                t.Add(string.Format("Fleet age {0:0.#} years", age), Clamp((15 - age)*0.7, -12, 6));

                t.Add("Aircraft with overdue checks", -OverdueShare(airline, fleet)*20);
            }

            var ratings = PilotRatings.GetRatings().OrderBy(r => r.CostIndex).ToList();
            var pilots = airline.Pilots.ToList().Where(p => p.Rating != null).ToList();
            if (ratings.Count > 1 && pilots.Count > 0)
            {
                double level = pilots.Average(p => (double) ratings.IndexOf(p.Rating))/(ratings.Count - 1);
                t.Add(string.Format("Pilot ratings ({0:0}% of the top)", level*100), Clamp((level - 0.4)*20, -8, 12));
            }

            t.Tip = "Keep aircraft in good condition, replace old aircraft and hire or train higher rated pilots.";
            return t;
        }

        public static RatingTarget SecurityTarget(Airline airline)
        {
            var t = new RatingTarget(Security);
            t.Base = 50;

            var airports = airline.Airports.ToList();
            if (airports.Count > 0)
            {
                int own = airports.Count(a => a.GetCurrentAirportFacility(airline, AirportFacility.FacilityType.CheckIn)?.TypeLevel > 0);
                double share = (double) own/airports.Count;
                t.Add(string.Format("Own check-in at {0} of {1} airports", own, airports.Count), share*15);
            }

            if (HasFacility(airline, "103") || HasFacility(airline, "104") || HasFacility(airline, "105"))
                t.Add("Booking system (passenger lists are checked)", 4);
            if (HasFacility(airline, "106")) t.Add("Online check-in", 3);

            if (airline.InsurancePolicies != null && airline.InsurancePolicies.Count > 0)
                t.Add("Insured (insurers ask for security checks)", 5);

            int employee = airline.Ratings.EmployeeHappinessRating;
            t.Add("Staff happiness", Clamp((employee - 50)*0.25, -10, 10));

            t.Tip = "Run your own check-in at your airports (Airports > facilities), use a booking and online check-in system, take out insurance and keep staff happy.";
            return t;
        }

        public static List<RatingTarget> Targets(Airline airline)
        {
            return new List<RatingTarget>
            {
                CustomerTarget(airline), EmployeeTarget(airline), MaintenanceTarget(airline), SafetyTarget(airline), SecurityTarget(airline)
            };
        }

        private static int Step(int current, int target, bool first)
        {
            if (first) return target;
            double next = current + (target - current)/3.0;
            if (next > current && next - current < 1) next = current + 1;
            if (next < current && current - next < 1) next = current - 1;
            return (int) Math.Round(Clamp(next, 0, 100));
        }

        /// <summary>Moves the airline's ratings toward this month's targets. Called once a month for every airline.</summary>
        public static void ProcessMonth(Airline airline)
        {
            try
            {
                var r = airline.Ratings;
                var t = Targets(airline);
                bool first = !r.IsCalculated;

                r.LastCustomerHappiness = r.CustomerHappinessRating;
                r.LastEmployeeHappiness = r.EmployeeHappinessRating;
                r.LastMaintenance = r.MaintenanceRating;
                r.LastSafety = r.SafetyRating;
                r.LastSecurity = r.SecurityRating;

                r.CustomerHappinessRating = Step(r.CustomerHappinessRating, t[0].Value, first);
                r.EmployeeHappinessRating = Step(r.EmployeeHappinessRating, t[1].Value, first);
                r.MaintenanceRating = Step(r.MaintenanceRating, t[2].Value, first);
                r.SafetyRating = Step(r.SafetyRating, t[3].Value, first);
                r.SecurityRating = Step(r.SecurityRating, t[4].Value, first);
                if (first)
                {
                    r.LastCustomerHappiness = r.CustomerHappinessRating;
                    r.LastEmployeeHappiness = r.EmployeeHappinessRating;
                    r.LastMaintenance = r.MaintenanceRating;
                    r.LastSafety = r.SafetyRating;
                    r.LastSecurity = r.SecurityRating;
                }
                r.IsCalculated = true;
            }
            catch (Exception)
            {
                // a rating that cannot be worked out this month keeps its value
            }
        }

        /// <summary>How much passengers prefer the airline: 0.75 at a customer rating of 0, 1 at 50 and 1.25 at 100.</summary>
        public static double CustomerFactor(Airline airline)
        {
            if (airline?.Ratings == null) return 1;
            return 0.75 + Clamp(airline.Ratings.CustomerHappinessRating, 0, 100)/200.0;
        }
    }
}
