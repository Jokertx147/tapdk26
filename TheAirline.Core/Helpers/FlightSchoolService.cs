using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using TheAirline.Models.General.Countries.Towns;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Pilots;

namespace TheAirline.Helpers
{
    /// <summary>Keeps the world supplied with pilots and instructors, so there is always somebody to hire.</summary>
    public static class PilotSupply
    {
        /// <summary>Pilots for hire that can fly one aircraft family, at least.</summary>
        public const int MinPerFamily = 40;

        public const int MinPilots = 120;
        public const int MinInstructors = 40;

        public static int CountFor(string family)
        {
            return Pilots.GetPilots().Count(p => p.Airline == null && p.Aircrafts != null && p.Aircrafts.Contains(family));
        }

        /// <summary>Makes sure there are enough pilots for hire who can fly the family. Returns how many were created.</summary>
        public static int EnsureFamily(string family, int minimum = MinPerFamily)
        {
            if (string.IsNullOrEmpty(family)) return 0;
            int have = CountFor(family);
            if (have >= minimum) return 0;
            GeneralHelpers.CreatePilots(minimum - have, family);
            return minimum - have;
        }

        public static void EnsureAnyPilots(int minimum = MinPilots)
        {
            int have = Pilots.GetPilots().Count(p => p.Airline == null);
            if (have < minimum)
            {
                GeneralHelpers.CreatePilots(minimum - have);
            }
        }

        public static void EnsureInstructors(int minimum = MinInstructors)
        {
            int have = Instructors.GetUnassignedInstructors().Count;
            if (have < minimum)
            {
                GeneralHelpers.CreateInstructors(minimum - have);
            }
        }

        /// <summary>Monthly top up for the player: pilots for every family the fleet flies, and instructors.</summary>
        public static void TopUp(Airline airline)
        {
            EnsureAnyPilots();
            EnsureInstructors();
            if (airline == null) return;
            foreach (string family in airline.Fleet.Select(f => f.Airliner.Type.AirlinerFamily).Distinct().ToList())
            {
                EnsureFamily(family);
            }
        }
    }

    /// <summary>Flight schools: building them, instructors, training aircraft and students.</summary>
    public static class FlightSchoolService
    {
        private static readonly Random Rnd = new Random();

        public static double BuildPrice => GeneralHelpers.GetInflationPrice(267050);
        public static double StudentPrice => GeneralHelpers.GetInflationPrice(PilotStudent.StudentCost);

        private static string Money(double v)
        {
            return v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
        }

        private static GameObject Game => GameObject.GetInstance();

        /// <summary>Airports where a school can be built: hubs and airports with a service facility, one school each.</summary>
        public static List<Airport> PossibleAirports(Airline airline)
        {
            var list = airline.Airports.Where(a => a.GetCurrentAirportFacility(airline, AirportFacility.FacilityType.Service).TypeLevel > 0).ToList();
            list.AddRange(airline.Airports.Where(a => a.IsHub));
            return list.Distinct().Where(a => airline.FlightSchools.All(f => f.Airport != a)).OrderBy(a => a.Profile.Town.Name).ToList();
        }

        public static bool Build(Airline airline, Airport airport, out string message)
        {
            if (airport == null || !PossibleAirports(airline).Contains(airport))
            {
                message = "A flight school can only be built at one of your hubs or at an airport with a service facility, and one school per airport.";
                return false;
            }
            double price = BuildPrice;
            if (airline.Money < price)
            {
                message = "Not enough money: a flight school costs " + Money(price) + ".";
                return false;
            }
            var school = new FlightSchool(airport);
            airline.AddFlightSchool(school);
            AirlineHelpers.AddAirlineInvoice(airline, Game.GameTime, Invoice.InvoiceType.AirlineExpenses, -price);
            message = school.Name + " is built for " + Money(price) + ". Hire instructors and buy a training aircraft before taking in students.";
            return true;
        }

        /// <summary>Closes a school: the instructors go back to the market, the training aircraft are sold for 75%, students are sent home.</summary>
        public static void Close(Airline airline, FlightSchool school, out string message)
        {
            double refund = 0;
            foreach (var a in school.TrainingAircrafts.ToList())
            {
                refund += a.Type.Price*0.75;
            }
            if (refund > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, Game.GameTime, Invoice.InvoiceType.AirlineExpenses, refund);
            }
            int students = school.Students.Count;
            foreach (var i in school.Instructors)
            {
                i.Students.Clear();
            }
            school.Students.Clear();
            school.TrainingAircrafts.Clear();
            airline.RemoveFlightSchool(school);
            message = school.Name + " is closed. " + (refund > 0 ? "The training aircraft were sold for " + Money(refund) + ". " : "")
                      + (students > 0 ? students + " student(s) were sent home." : "");
        }

        public static int Seats(FlightSchool school)
        {
            return school.TrainingAircrafts.Sum(a => a.Type.MaxNumberOfStudents);
        }

        /// <summary>How many students the school can take in all: two per instructor, limited by the seats in the training aircraft.</summary>
        public static int Capacity(FlightSchool school)
        {
            return Math.Min(school.Instructors.Count*FlightSchool.MaxNumberOfStudentsPerInstructor, Seats(school));
        }

        public static int FreeSeats(FlightSchool school, TrainingAircraft aircraft)
        {
            return aircraft.Type.MaxNumberOfStudents - school.Students.Count(s => s.Aircraft == aircraft);
        }

        public static List<Instructor> InstructorsWithRoom(FlightSchool school)
        {
            return school.Instructors.Where(i => i.Students.Count < FlightSchool.MaxNumberOfStudentsPerInstructor).ToList();
        }

        /// <summary>The ratings a new student can get: those who can train in an aircraft with a free seat.</summary>
        public static List<PilotRating> PossibleRatings(FlightSchool school)
        {
            return PilotRatings.GetRatings()
                               .Where(r => r.Aircrafts.Any(t => school.TrainingAircrafts.Any(a => a.Type == t && FreeSeats(school, a) > 0)))
                               .ToList();
        }

        /// <summary>Why no student can be taken in now, or null.</summary>
        public static string GetStudentBlocker(Airline airline, FlightSchool school)
        {
            if (school.Instructors.Count == 0) return "The school has no instructors yet.";
            if (school.TrainingAircrafts.Count == 0) return "The school has no training aircraft yet.";
            if (InstructorsWithRoom(school).Count == 0) return "All instructors have their " + FlightSchool.MaxNumberOfStudentsPerInstructor + " students. Hire another instructor.";
            if (PossibleRatings(school).Count == 0) return "All the seats in the training aircraft are taken. Buy another training aircraft.";
            if (airline.Money < StudentPrice) return "Not enough money: a student costs " + Money(StudentPrice) + ".";
            return null;
        }

        /// <summary>The aircraft families a student can be trained for: what is flying now, and what flew in the last 30 years.</summary>
        public static List<string> Families()
        {
            return AirlinerTypes.GetTypes(t => t.Produced.From.Year <= Game.GameTime.Year && t.Produced.To > Game.GameTime.AddYears(-30))
                                .Select(t => t.AirlinerFamily).Distinct().OrderBy(f => f).ToList();
        }

        public static bool StartStudent(Airline airline, FlightSchool school, Instructor instructor, string family, out string message)
        {
            string blocker = GetStudentBlocker(airline, school);
            if (blocker != null)
            {
                message = blocker;
                return false;
            }
            if (instructor == null || !InstructorsWithRoom(school).Contains(instructor))
            {
                message = "Choose an instructor who has room for another student.";
                return false;
            }
            if (string.IsNullOrEmpty(family))
            {
                message = "Choose the aircraft family the student is trained for.";
                return false;
            }

            var possible = PossibleRatings(school);
            var rating = GeneralHelpers.GetPilotStudentRating(instructor, possible);
            if (!possible.Contains(rating)) rating = possible[Rnd.Next(possible.Count)];

            var free = school.TrainingAircrafts.Where(a => FreeSeats(school, a) > 0).ToList();
            var aircraft = free.Where(a => rating.Aircrafts.Contains(a.Type)).OrderBy(a => a.Type.TypeLevel).FirstOrDefault() ?? free.OrderBy(a => a.Type.TypeLevel).First();

            List<Town> towns = Towns.GetTowns(school.Airport.Profile.Country);
            if (towns == null || towns.Count == 0) towns = Towns.GetTowns();
            Town town = towns[Rnd.Next(towns.Count)];
            DateTime birthdate = MathHelpers.GetRandomDate(Game.GameTime.AddYears(-35), Game.GameTime.AddYears(-23));
            var profile = new PilotProfile(Names.GetInstance().GetRandomFirstName(town.Country), Names.GetInstance().GetRandomLastName(town.Country), birthdate, town);

            var student = new PilotStudent(profile, Game.GameTime, instructor, rating, family) {Aircraft = aircraft};
            school.AddStudent(student);
            instructor.AddStudent(student);
            double price = StudentPrice;
            AirlineHelpers.AddAirlineInvoice(airline, Game.GameTime, Invoice.InvoiceType.AirlineExpenses, -price);

            message = profile.Name + " starts training for the " + family + " with " + instructor.Profile.Name + " (rating " + rating.Name + ", about "
                      + rating.TrainingDays + " days, ready " + DateDisplay.Format(student.EndDate) + "). It cost " + Money(price) + ".";
            return true;
        }

        public static void CancelStudent(FlightSchool school, PilotStudent student)
        {
            school.RemoveStudent(student);
            student.Instructor?.RemoveStudent(student);
            student.Instructor = null;
        }

        public static bool HireInstructor(Airline airline, FlightSchool school, Instructor instructor, out string message)
        {
            if (instructor == null || instructor.FlightSchool != null)
            {
                message = "That instructor is not available.";
                return false;
            }
            if (school.NumberOfInstructors >= FlightSchool.MaxNumberOfInstructors)
            {
                message = school.Name + " has " + FlightSchool.MaxNumberOfInstructors + " instructors already, which is the most it can have.";
                return false;
            }
            school.AddInstructor(instructor);
            instructor.FlightSchool = school;
            message = instructor.Profile.Name + " is hired as an instructor at " + school.Name + ".";
            return true;
        }

        public static bool DismissInstructor(FlightSchool school, Instructor instructor, out string message)
        {
            if (instructor.Students.Count > 0)
            {
                message = instructor.Profile.Name + " still has students. Wait until they graduate, or move or cancel them first.";
                return false;
            }
            school.RemoveInstructor(instructor);
            instructor.FlightSchool = null;
            message = instructor.Profile.Name + " leaves " + school.Name + ".";
            return true;
        }

        public static bool BuyAircraft(Airline airline, FlightSchool school, TrainingAircraftType type, out string message)
        {
            if (type == null)
            {
                message = "Choose a training aircraft.";
                return false;
            }
            if (airline.Money < type.Price)
            {
                message = "Not enough money: a " + type.Name + " costs " + Money(type.Price) + ".";
                return false;
            }
            school.AddTrainingAircraft(new TrainingAircraft(type, Game.GameTime, school));
            AirlineHelpers.AddAirlineInvoice(airline, Game.GameTime, Invoice.InvoiceType.AirlineExpenses, -type.Price);
            message = "Bought a " + type.Name + " for " + Money(type.Price) + ".";
            return true;
        }

        public static bool SellAircraft(Airline airline, FlightSchool school, TrainingAircraft aircraft, out string message)
        {
            if (school.Students.Any(s => s.Aircraft == aircraft))
            {
                message = "Students are training in this " + aircraft.Type.Name + ". Sell it after they graduate.";
                return false;
            }
            school.RemoveTrainingAircraft(aircraft);
            double price = aircraft.Type.Price*0.75;
            AirlineHelpers.AddAirlineInvoice(airline, Game.GameTime, Invoice.InvoiceType.AirlineExpenses, price);
            message = "Sold the " + aircraft.Type.Name + " for " + Money(price) + " (75% of the price).";
            return true;
        }

        // ---- type training of pilots you have hired ----

        /// <summary>A day of a substitute pilot, while a pilot who flies an aircraft is away on training.</summary>
        public const double SubstituteDayPrice = 500;

        public static List<string> TrainingFamilies(Pilot pilot)
        {
            return Families().Where(f => pilot.Aircrafts == null || !pilot.Aircrafts.Contains(f)).ToList();
        }

        public static double GetSubstitutePrice(Pilot pilot, string family)
        {
            return pilot.Airliner == null ? 0 : GeneralHelpers.GetInflationPrice(AirlineHelpers.GetTrainingDays(pilot, family)*SubstituteDayPrice);
        }

        public static double GetTrainingCost(Pilot pilot, string family)
        {
            return AirlineHelpers.GetTrainingPrice(pilot, family) + GetSubstitutePrice(pilot, family);
        }

        /// <summary>Sends a pilot of the airline to training for another aircraft family. The pilot can fly it when the training ends.</summary>
        public static bool TrainPilot(Airline airline, Pilot pilot, string family, out string message)
        {
            if (pilot == null || pilot.Airline != airline)
            {
                message = "Choose one of your pilots.";
                return false;
            }
            if (pilot.Training != null)
            {
                message = pilot.Profile.Name + " is already training for the " + pilot.Training.AirlinerFamily + ".";
                return false;
            }
            if (string.IsNullOrEmpty(family) || (pilot.Aircrafts != null && pilot.Aircrafts.Contains(family)))
            {
                message = "Choose an aircraft family " + pilot.Profile.Name + " cannot fly yet.";
                return false;
            }

            double training = AirlineHelpers.GetTrainingPrice(pilot, family);
            double substitute = GetSubstitutePrice(pilot, family);
            int days = AirlineHelpers.GetTrainingDays(pilot, family);
            if (airline.Money < training + substitute)
            {
                message = "Not enough money: the training costs " + Money(training + substitute) + ".";
                return false;
            }

            if (substitute > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, Game.GameTime, Invoice.InvoiceType.AirlineExpenses, -substitute);
            }
            AirlineHelpers.SendForTraining(airline, pilot, family, days, training);
            message = pilot.Profile.Name + " is training for the " + family + " for " + days + " days, until " + DateDisplay.Format(pilot.Training.EndDate) + ". It costs "
                      + Money(training) + (substitute > 0 ? " plus " + Money(substitute) + " for a substitute pilot on " + pilot.Airliner.Name : "") + ".";
            return true;
        }

        /// <summary>Puts pilots who have no aircraft (new graduates, for example) on aircraft that need pilots and match their type.</summary>
        public static int AssignIdlePilots(Airline airline)
        {
            int n = 0;
            foreach (var pilot in airline.Pilots.Where(p => p.Airliner == null).ToList())
            {
                var target = airline.Fleet.FirstOrDefault(
                    f => f.Pilots.Count < f.Airliner.Type.CockpitCrew && pilot.Aircrafts != null && pilot.Aircrafts.Contains(f.Airliner.Type.AirlinerFamily));
                if (target == null) continue;
                pilot.Airliner = target;
                target.AddPilot(pilot);
                n++;
            }
            return n;
        }
    }
}
