using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Environment;
using TheAirline.Models.General.Holidays;
using TheAirline.Models.General.Statistics;
using TheAirline.Models.Passengers;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    //the helper class for the passengers
    public class PassengerHelpers
    {
        #region Static Fields

        private static readonly Dictionary<GeneralHelpers.Size, double> CargoFactors =
            new Dictionary<GeneralHelpers.Size, double>();

        private static readonly Dictionary<Airline, double> HappinessPercent = new Dictionary<Airline, double>();

        // Random.Shared is safe to use from the parallel airline threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        #endregion

        #region Public Methods and Operators

        public static void AddPassengerHappiness(Airline airline)
        {
            /*
            lock (HappinessPercent)
            {
                if (HappinessPercent.ContainsKey(airline))
                    HappinessPercent[airline] += 1;
                else
                    HappinessPercent.Add(airline, 1);
            }
             * */
        }

        public static void ChangePaxDemand(double factor)
        {
            ChangePaxDemand(Airports.GetAllActiveAirports(), factor);
        }

        //changes the demand for a list of airports with a factor
        public static void ChangePaxDemand(List<Airport> airports, double factor)
        {
            //increases the passenger demand between airports with 5%
            //Parallel.ForEach(airports, airport => { ChangePaxDemand(airport, factor); });

            foreach (Airport airport in airports)
            {
                ChangePaxDemand(airport, factor);
            }
        }

        //changes the demand for all airports belonging to an airline with a factor
        public static void ChangePaxDemand(Airline airline, double factor)
        {
            foreach (Airport a in airline.Airports)
            {
                ChangePaxDemand(a, factor);
            }
        }

        //changes the demand between two airports
        public static void ChangePaxDemand(Airport airport1, Airport airport2, int value)
        {
            DestinationDemand destPax = airport1.GetDestinationPassengersObject(airport2);

            if (destPax != null && value > 0)
            {
                destPax.Rate += Convert.ToUInt16(value);
            }
            else
            {
                if (value > 0)
                {
                    airport1.AddDestinationPassengersRate(airport2, Convert.ToUInt16(value));
                }
            }
        }

        //changes the demand for an airport with a factor
        public static void ChangePaxDemand(Airport airport, double factor)
        {
            double value = (100 + factor)/100;
            //factor 0.5 - 2;
            foreach (DestinationDemand destPax in airport.GetDestinationsPassengers())
            {
                if (destPax.Rate > 0)
                {
                    ushort oRate = destPax.Rate;
                    if ((oRate*value).Equals(destPax.Rate))
                    {
                        destPax.Rate =
                            Convert.ToUInt16(destPax.Rate + Convert.ToUInt16(Rnd.Next(0, Math.Max(1, (int) factor))));
                    }
                    else
                    {
                        destPax.Rate = (ushort) (destPax.Rate*value);
                    }
                }
                else
                {
                    destPax.Rate = (ushort) Rnd.Next(1, Math.Max(1, (int) factor));
                }
            }
        }

        public static void CreateAirlineDestinationDemand()
        {
            //sets up the factors if they are not already setted up

            if (CargoFactors.Count == 0)
            {
                CargoFactors.Add(GeneralHelpers.Size.Largest, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VeryLarge, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Large, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Medium, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Small, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VerySmall, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Smallest, 0.23);
            }

            IEnumerable<Airport> airports = Airlines.GetAllAirlines().SelectMany(a => a.Airports);

            foreach (Airport airport in airports)
            {
                //Parallel.ForEach(
                //    Airports.GetAllAirports(),
                //    dAirport =>
                foreach (Airport dAirport in Airports.GetAllAirports())
                {
                    airport.Statics.AddDistance(dAirport, MathHelpers.GetDistance(airport, dAirport));

                    if (airport != dAirport && airport.Profile.Town != dAirport.Profile.Town
                        && MathHelpers.GetDistance(airport, dAirport) > 50)
                    {
                        CreateDestinationPassengers(airport, dAirport);

                        CreateDestinationCargo(airport, dAirport);
                    }
                } //);
                if (airport.GetDestinationPassengersSum() == 0)
                {
                    Airport airport1 = airport;
                    List<Airport> subAirports =
                        Airports.GetAllAirports(a => a.Profile.Country == airport1.Profile.Country)
                                .DefaultIfEmpty()
                                .ToList();
                    subAirports.RemoveAll(a => a == null);

                    if (subAirports.Any())
                    {
                        CreateDestinationPassengers(airport, subAirports);
                    }
                    else
                    {
                        Airport airport2 = airport;
                        subAirports =
                            Airports.GetAllAirports(a => a.Profile.Country.Region == airport2.Profile.Country.Region)
                                    .ToList();
                        CreateDestinationPassengers(airport, subAirports);
                    }
                }
            }
        }

        public static void CreateDestinationCargo(Airport airport, Airport dAirport)
        {
            //origin airport out
            double originMassPerDay = GetAirportCargoMass(airport)/2;
            double originDemand = (originMassPerDay*CargoFactors[dAirport.Profile.Cargo])
                                  /Airports.CargoAirportsSizes[dAirport.Profile.Cargo];
            //destination airport in
            double destinationMassPerDay = GetAirportCargoMass(dAirport)/2;
            double destinationDemand = (destinationMassPerDay*CargoFactors[airport.Profile.Cargo])
                                       /Airports.CargoAirportsSizes[airport.Profile.Cargo];

            double distance = MathHelpers.GetDistance(airport, dAirport);

            double distanceFactor = distance > 5000 ? 1 : 0.5;
            double sameCountryFactor = airport.Profile.Country == dAirport.Profile.Country ? 0.75 : 1;
            double sameRegionFactor = sameCountryFactor.Equals(1)
                                      && airport.Profile.Country.Region == dAirport.Profile.Country.Region
                                          ? 0.5
                                          : 1;

            double minMass = Math.Min(originMassPerDay, destinationMassPerDay);

            double volume = minMass/distanceFactor;
            volume = volume/sameCountryFactor;
            volume = volume/sameRegionFactor;
            //converts to pounds of cargo
            volume *= 35.1;

            if (volume >= 1)
            {
                airport.AddDestinationCargoRate(new DestinationDemand(dAirport.Profile.IATACode, (ushort) volume));
            }
        }

        public static void CreateDestinationDemand()
        {
            if (CargoFactors.Count == 0)
            {
                CargoFactors.Add(GeneralHelpers.Size.Largest, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VeryLarge, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Large, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Medium, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Small, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VerySmall, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Smallest, 0.23);
            }

            List<Airport> airports = Airports.GetAllAirports(a => a.Statics.GetDestinationPassengersSum() == 0);
            int count = airports.Count;
            // each airport's position once, instead of converting it for every pair
            var points = airports.Select(a => a.Profile.Coordinates.ConvertToGeoCoordinate()).ToArray();

            //var airports = Airports.GetAirports(a => a != airport && a.Profile.Town != airport.Profile.Town && MathHelpers.GetDistance(a.Profile.Coordinates, airport.Profile.Coordinates) > 50);

            Parallel.For(
                0,
                count - 1,
                i =>
                    {
                        // the pairs of one airport in a plain loop: the outer loop already uses every processor,
                        // and starting a parallel loop for each airport cost more than the work in it
                        for (int j = i + 1; j < count; j++)
                                {
                                    if (airports[i].Profile.Town == airports[j].Profile.Town) continue;
                                    double distance = points[i].GetDistanceTo(points[j])/1000;
                                    if (distance > 50)
                                    {
                                        airports[j].Statics.AddDistance(airports[i], distance);

                                        CreateDestinationPassengers(airports[j], airports[i]);
                                        CreateDestinationPassengers(airports[i], airports[j]);

                                        CreateDestinationCargo(airports[j], airports[i]);
                                        CreateDestinationCargo(airports[i], airports[j]);
                                    }
                                }

                        if (airports[i].GetDestinationPassengersSum() == 0)
                        {
                            List<Airport> subAirports =
                                airports.FindAll(a => a.Profile.Country == airports[i].Profile.Country)
                                        .DefaultIfEmpty()
                                        .ToList();
                            subAirports.RemoveAll(a => a == null);

                            if (subAirports.Any())
                            {
                                CreateDestinationPassengers(airports[i], subAirports);
                            }
                            else
                            {
                                subAirports =
                                    airports.FindAll(a => a.Profile.Country.Region == airports[i].Profile.Country.Region)
                                            .ToList();
                                CreateDestinationPassengers(airports[i], subAirports);
                            }
                        }
                    });
        }

        public static void CreateDestinationPassengers(Airport airport)
        {
            List<Airport> airports =
                Airports.GetAirports(
                    a =>
                    a != airport && a.Profile.Town != airport.Profile.Town
                    && MathHelpers.GetDistance(
                        a.Profile.Coordinates.ConvertToGeoCoordinate(),
                        airport.Profile.Coordinates.ConvertToGeoCoordinate()) > 50);
            //Parallel.ForEach(airports, dAirport =>
            foreach (Airport dAirport in airports)
            {
                CreateDestinationPassengers(airport, dAirport);
            } //);
            if (airport.GetDestinationsPassengers().Sum(d => d.Rate) == 0)
            {
                List<Airport> subAirports =
                    airports.FindAll(a => a.Profile.Country == airport.Profile.Country).DefaultIfEmpty().ToList();
                subAirports.RemoveAll(a => a == null);
                if (subAirports.Any())
                {
                    CreateDestinationPassengers(airport, subAirports);
                }
                else
                {
                    subAirports =
                        airports.FindAll(a => a.Profile.Country.Region == airport.Profile.Country.Region).ToList();
                    CreateDestinationPassengers(airport, subAirports);
                }
            }
        }

        // airport size names for the demand table (an enum's name was asked for twice per airport pair)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<GeneralHelpers.Size, string> SizeNames =
            new System.Collections.Concurrent.ConcurrentDictionary<GeneralHelpers.Size, string>();

        private static string SizeName(GeneralHelpers.Size size)
        {
            return SizeNames.GetOrAdd(size, s => s.ToString());
        }

        public static void CreateDestinationPassengers(Airport airport, Airport dAirport)
        {
            //short_haul, long_haul, demand both ways
            double estimatedPassengerLevel = 0;
            bool isSameCountry = airport.Profile.Country == dAirport.Profile.Country;
            bool isSameContinent = airport.Profile.Country.Region == dAirport.Profile.Country.Region
                                      && !isSameCountry;

            String dAirportSize = SizeName(dAirport.Profile.Size);
            String airportSize = SizeName(airport.Profile.Size);
            double dist = MathHelpers.GetDistance(dAirport, airport);

            if (airport.Profile.MajorDestionations.Keys.Contains(dAirport.Profile.IATACode))
            {
                estimatedPassengerLevel =
                    (Convert.ToDouble(airport.Profile.MajorDestionations[dAirport.Profile.IATACode])*1000)/365;
                estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
            }
            else
            {
                switch (airportSize)
                {
                        #region Origin"Largest" switches

                    case "Largest":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 40000*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 40000*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 40000*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 40000*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 40000*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Very_small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVery_small = 40000*0.04/Airports.VerySmallAirports;
                                    paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVery_small*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Smallest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 40000*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Very_large" switches

                    case "Very_large":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 20000*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 20000*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 20000*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 20000*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 20000*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 20000*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":

                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 20000*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Large" switches

                    case "Large":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 10000*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 10000*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 10000*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 10000*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 10000*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 10000*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":

                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 10000*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Medium" switches

                    case "Medium":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 6000*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 6000*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 6000*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 6000*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 6000*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":

                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 6000*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 6000*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Small" switches

                    case "Small":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 1250*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 1250*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 1250*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 1250*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 1250*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 1250*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 1250*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Very_small" switches

                    case "Very_small":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 225*0.21/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 225*0.24/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 225*0.24/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 225*0.15/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 225*0.10/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 225*0.04/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 225*0.02/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Smallest" switches

                    case "Smallest":
                        switch (dAirportSize)
                        {
                            case "Largest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLargest = airport.Profile.Pax*0.25/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 50*0.25/Airports.LargestAirports;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }

                                break;

                            case "Very_large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = airport.Profile.Pax*0.32/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 50*0.32/Airports.VeryLargeAirports;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }

                                break;

                            case "Large":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxLarge = airport.Profile.Pax*0.32/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 50*0.32/Airports.LargeAirports;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;

                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxMedium = airport.Profile.Pax*0.09/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 2;
                                }
                                else
                                {
                                    double paxMedium = 50*0.09/Airports.MediumAirports;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 2;
                                }
                                break;

                            case "Small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmall = airport.Profile.Pax*0.02/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 1.2;
                                }
                                else
                                {
                                    double paxSmall = 50*0.02/Airports.SmallAirports;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 1.2;
                                }
                                break;

                            case "Very_small":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxVerySmall = airport.Profile.Pax*0/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 50*0/Airports.VerySmallAirports;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (airport.Profile.Pax > 0)
                                {
                                    double paxSmallest = airport.Profile.Pax*0/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 50*0/Airports.SmallestAirports;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion
                }
            }

            #region Demand with "if" statements

            //PLEASE don't change the same country/continent/international values. Most of these were specifically calculated and are not yet calculated 
            //by the program itself! Based largely on US airport system values.
            /*     #region largest airports
                 if (dAirportSize.Equals("Largest") && airportSize.Equals("Largest"))
                 {
                     if (airport.Profile.Pax > 0)
                     {
                         double paxLargest = airport.Profile.Pax * 0.21 / Airports.LargestAirports;
                         paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                         estimatedPassengerLevel = (paxLargest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxLargest = 40000 * 0.21 / Airports.LargestAirports;
                         paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                         estimatedPassengerLevel = (paxLargest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dAirportSize.Equals("Very_large") && airportSize.Equals("Largest"))
                 {
                     if (airport.Profile.Pax == 0)
                     {
                         double paxVeryLarge = 40000 * 0.24 / Airports.VeryLargeAirports;
                         paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                         estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxVeryLarge = airport.Profile.Pax * 0.24 / Airports.VeryLargeAirports;
                         paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                         estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dAirportSize.Equals("Large") && airportSize.Equals("Largest"))
                 {
                     if (airport.Profile.Pax == 0)
                     {
                         double paxLarge = 40000 * 0.24 / Airports.LargeAirports;
                         paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                         estimatedPassengerLevel = (paxLarge * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxLarge = airport.Profile.Pax * 0.24 / Airports.LargeAirports;
                         paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                         estimatedPassengerLevel = (paxLarge * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dAirportSize.Equals("Medium") && airportSize.Equals("Largest"))
                 {
                     if (airport.Profile.Pax == 0)
                     {
                         double paxMedium = 40000 * 0.15 / Airports.MediumAirports;
                         paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                         estimatedPassengerLevel = (paxMedium * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                         paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                         estimatedPassengerLevel = (paxMedium * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dAirportSize.Equals("Small") && airportSize.Equals("Largest"))
                 {
                     if (airport.Profile.Pax == 0)
                     {
                         double paxSmall = 40000 * 0.10 / Airports.SmallAirports;
                         paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                         estimatedPassengerLevel = (paxSmall * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.2;
                     }
                     else
                     {
                         double paxSmall = airport.Profile.Pax * 0.10 / Airports.SmallAirports;
                         paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                         estimatedPassengerLevel = (paxSmall * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.2;
                     }
                 }

                 if (dAirportSize.Equals("Very_small") && airportSize.Equals("Largest"))
                 {
                     if (airport.Profile.Pax == 0)
                     {
                         double paxVery_small = 40000 * 0.04 / Airports.VerySmallAirports;
                         paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                         estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.1;
                     }
                     else
                     {
                         double paxVery_small = airport.Profile.Pax * 0.04 / Airports.VerySmallAirports;
                         paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                         estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.1;
                     }
                 }

                 if (dAirportSize.Equals("Smallest") && airportSize.Equals("Largest"))
                 {
                     if (dist > 1600)
                     { estimatedPassengerLevel = 0; }

                     else if (airport.Profile.Pax == 0)
                     {
                         double paxSmallest = 40000 * 0.02 / Airports.SmallestAirports;
                         paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                         estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.8;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 0.8;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0;
                     }
                     else
                     {
                         double paxSmallest = 40000 * 0.02 / Airports.SmallestAirports;
                         paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                         estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.8;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 0.8;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0;
                     }
                 }
             }
                 #endregion
             #region very large airports
             if (dAirportSize.Equals("Largest") && airportSize.Equals("Very_large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLargest = 20000 * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = airport.Profile.Pax * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Very_large") && airportSize.Equals("Very_large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 20000 * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = airport.Profile.Pax * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Large") && airportSize.Equals("Very_large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLarge = 20000 * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = airport.Profile.Pax * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Medium") && airportSize.Equals("Very_large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxMedium = 20000 * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Small") && airportSize.Equals("Very_large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxSmall = 20000 * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.7;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.35;
                 }
                 else
                 {
                     double paxSmall = airport.Profile.Pax * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.7;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.35;
                 }
             }

             if (dAirportSize.Equals("Very_small") && airportSize.Equals("Very_large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVery_small = 20000 * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.2;
                 }
                 else
                 {
                     double paxVery_small = airport.Profile.Pax * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.2;
                 }
             }

             if (dAirportSize.Equals("Smallest") && airportSize.Equals("Very_large"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0; }

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmallest = 20000 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.8;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
                 else
                 {
                     double paxSmallest = 20000 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.8;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
             }

             #endregion
             #region large airports
             if (dAirportSize.Equals("Largest") && airportSize.Equals("Large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLargest = 10000 * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = airport.Profile.Pax * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Very_large") && airportSize.Equals("Large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 10000 * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = airport.Profile.Pax * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Large") && airportSize.Equals("Large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLarge = 10000 * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = airport.Profile.Pax * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Medium") && airportSize.Equals("Large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxMedium = 10000 * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Small") && airportSize.Equals("Large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxSmall = 10000 * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.15;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.3;
                 }
                 else
                 {
                     double paxSmall = airport.Profile.Pax * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.15;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.3;
                 }
             }

             if (dAirportSize.Equals("Very_small") && airportSize.Equals("Large"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVery_small = 10000 * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.9;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.13;
                 }
                 else
                 {
                     double paxVery_small = airport.Profile.Pax * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.9;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.13;
                 }
             }

             if (dAirportSize.Equals("Smallest") && airportSize.Equals("Large"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0; }

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmallest = 10000 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.7;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
                 else
                 {
                     double paxSmallest = 10000 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.7;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
             }
             #endregion
             #region medium airports
             if (dAirportSize.Equals("Largest") && airportSize.Equals("Medium"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLargest = 6000 * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = airport.Profile.Pax * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Very_large") && airportSize.Equals("Medium"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 6000 * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = airport.Profile.Pax * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Large") && airportSize.Equals("Medium"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLarge = 6000 * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = airport.Profile.Pax * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Medium") && airportSize.Equals("Medium"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxMedium = 6000 * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Small") && airportSize.Equals("Medium"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxSmall = 6000 * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.25;
                 }
                 else
                 {
                     double paxSmall = airport.Profile.Pax * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.25;
                 }
             }

             if (dAirportSize.Equals("Very_small") && airportSize.Equals("Medium"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0; }
                
                 else if (airport.Profile.Pax == 0)
                 {
                     double paxVery_small = 6000 * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.95;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
                 else
                 {
                     double paxVery_small = airport.Profile.Pax * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.95;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
             }

             if (dAirportSize.Equals("Smallest") && airportSize.Equals("Medium"))
             {
                 if (dist > 1200 )
                 { estimatedPassengerLevel = 0; }

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmallest = 6000 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
                 else
                 {
                     double paxSmallest = 6000 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
             }
             #endregion
             #region small airports
             if (dAirportSize.Equals("Largest") && airportSize.Equals("Small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLargest = 1250 * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = airport.Profile.Pax * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Very_large") && airportSize.Equals("Small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 1250 * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = airport.Profile.Pax * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Large") && airportSize.Equals("Small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLarge = 1250 * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = airport.Profile.Pax * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Medium") && airportSize.Equals("Small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxMedium = 1250 * 0.17 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Small") && airportSize.Equals("Small"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmall = 1250 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= .9;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
                 else
                 {
                     double paxSmall = airport.Profile.Pax * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= .7;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
             }

             if (dAirportSize.Equals("Very_small") && airportSize.Equals("Small"))
             {
                 if (dist > 1200)
                 {estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxVery_small = 1250 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.1;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.4;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
                 else
                 {
                     double paxVery_small = airport.Profile.Pax * 0.04 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.1;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.4;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
             }

             if (dAirportSize.Equals("Smallest") && airportSize.Equals("Small"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmallest = 0 * 0.02 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.25;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
                 else
                 {
                     double paxSmallest = airport.Profile.Pax * 0 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
             }
             #endregion
             #region very small airports
             if (dAirportSize.Equals("Largest") && airportSize.Equals("Very_small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLargest = 350 * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = airport.Profile.Pax * 0.21 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Very_large") && airportSize.Equals("Very_small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 350 * 0.27 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = airport.Profile.Pax * 0.24 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Large") && airportSize.Equals("Very_small"))
             {
                 if (airport.Profile.Pax == 0)
                 {
                     double paxLarge = 350 * 0.27 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = airport.Profile.Pax * 0.24 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Medium") && airportSize.Equals("Very_small"))
             {
                 if ( dist > 1600)
                 { estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxMedium = 350 * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
                 else
                 {
                     double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
             }

             if (dAirportSize.Equals("Small") && airportSize.Equals("Very_small"))
             {
                 if (dist > 1200 )
                 { estimatedPassengerLevel = 0; }

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmall = 350 * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.25;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.35;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxSmall = airport.Profile.Pax * 0.10 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.25;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.35;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dAirportSize.Equals("Very_small") && airportSize.Equals("Very_small"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxVery_small = 350 * 0 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
                 else
                 {
                     double paxVery_small = airport.Profile.Pax * 0 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
             }

             if (dAirportSize.Equals("Smallest") && airportSize.Equals("Very_small"))
             {
                 if ( dist > 800)
                 {estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmallest = 350 * 0 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
                 else
                 {
                     double paxSmallest = 350 * 0 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
             }
             #endregion
             #region smallest airports
             if (dAirportSize.Equals("Largest") && airportSize.Equals("Smallest"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0; }
                 else if (airport.Profile.Pax == 0)
                 {
                     double paxLargest = 50 * 0.25 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxLargest = airport.Profile.Pax * 0.25 / Airports.LargestAirports;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 if (dist < 1600)
                 { estimatedPassengerLevel *= 2; }
                 else if (dist < 2400)
                 { estimatedPassengerLevel *= 0.5; }
                 else
                 { estimatedPassengerLevel = 0; }
             }

             if (dAirportSize.Equals("Very_large") && airportSize.Equals("Smallest"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0; }
                 else if (airport.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 50 * 0.32 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxVeryLarge = airport.Profile.Pax * 0.32 / Airports.VeryLargeAirports;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 if (dist < 1600)
                 { estimatedPassengerLevel *= 2; }
                 else if (dist < 2400)
                 { estimatedPassengerLevel *= 0.5; }
                 else
                 { estimatedPassengerLevel = 0; }
             }

             if (dAirportSize.Equals("Large") && airportSize.Equals("Smallest"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0; }
                 else if (airport.Profile.Pax == 0)
                 {
                     double paxLarge = 50 * 0.32 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxLarge = airport.Profile.Pax * 0.32 / Airports.LargeAirports;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 if (dist < 1600)
                 { estimatedPassengerLevel *= 2; }
                 else if (dist < 2400)
                 { estimatedPassengerLevel *= 0.5; }
                 else
                 { estimatedPassengerLevel = 0; }
             }

             if (dAirportSize.Equals("Medium") && airportSize.Equals("Smallest"))
             {
                 if (dist > 1200)
                 {estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxMedium = 50 * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 2;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxMedium = airport.Profile.Pax * 0.15 / Airports.MediumAirports;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 2;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
             }

             if (dAirportSize.Equals("Small") && airportSize.Equals("Smallest"))
             {
                 if (dist > 800)
                 {estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmall = 50 * 0 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 1.2;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.0;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxSmall = airport.Profile.Pax * 0 / Airports.SmallAirports;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 1.2;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.0;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.00;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
             }

             if (dAirportSize.Equals("Very_small") && airportSize.Equals("Smallest"))
             {
                 if (dist > 500)
                 {estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxVery_small = 50 * 0 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxVery_small = airport.Profile.Pax * 0 / Airports.VerySmallAirports;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
             }

             if (dAirportSize.Equals("Smallest") && airportSize.Equals("Smallest"))
             {
                 if (dist > 200)
                 {estimatedPassengerLevel = 0;}

                 else if (airport.Profile.Pax == 0)
                 {
                     double paxSmallest = 50 * 0 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxSmallest = 50 * 0 / Airports.SmallestAirports;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel = 0;
                 }

                      

             }*/

            #endregion

            double value = estimatedPassengerLevel*GetDemandYearFactor(GameObject.GetInstance().GameTime.Year);

            var rate = (ushort) value;

            if (rate > 0)
            {
                airport.AddDestinationPassengersRate(new DestinationDemand(dAirport.Profile.IATACode, rate));
                //DatabaseObject.GetInstance().addToTransaction(airport, dAirport, classType, rate);
            }
        }

        public static double GetCargoPrice(Airport dest1, Airport dest2)
        {
            // the market rate per unit (see PricingService)
            return PricingService.CargoMarketRate(dest1, dest2);
        }

        // chs, 2011-13-10 added for loading of passenger happiness

        //returns the cargo for a flight
        public static double GetFlightCargo(FleetAirliner airliner)
        {
            Airport airportCurrent = airliner.CurrentFlight.GetDepartureAirport();
            Airport airportDestination = airliner.CurrentFlight.Entry.Destination.Airport;

            return GetFlightCargo(airportCurrent, airportDestination, airliner);
        }

        public static double GetFlightCargo(Airport airportCurrent, Airport airportDestination, FleetAirliner airliner)
        {
            // the flying airline's cargo facility at the destination (the original used the player's for everyone)
            double destinationFacilityFactor =
                airportDestination.GetAirportFacility(
                    airliner.Airliner.Airline,
                    AirportFacility.FacilityType.Cargo,
                    true).ServiceLevel;

            double distance = MathHelpers.GetDistance(airportCurrent, airportDestination);

            double capacity = 0;

            var type = airliner.Airliner.Type as AirlinerCargoType;
            if (type != null)
                capacity = type.CargoSize;
            var combiType = airliner.Airliner.Type as AirlinerCombiType;
            if (combiType != null)
                capacity = combiType.CargoSize;

            double demand = airportCurrent.GetDestinationCargoRate(airportDestination)
                            *(destinationFacilityFactor/100);

            double cargoDemand = demand*1000;

            // with realistic costs shippers react to the rate both ways
            double basicCargoPrice = GetCargoPrice(airportCurrent, airportDestination);
            double cargoPrice = airliner.CurrentFlight.GetCargoPrice();
            cargoDemand *= PricingService.CargoDemandFactor(cargoPrice, basicCargoPrice);

            // every airline's flying cargo routes on this pair, nonstop or as a stopover leg
            var routes = new List<Route>();
            foreach (Route r in RouteIndex.Direct(airportCurrent, airportDestination))
            {
                if ((r.Type == Route.RouteType.Cargo || r.Type == Route.RouteType.Mixed) && r.HasAirliner) routes.Add(r);
            }
            foreach (Route r in RouteIndex.WithLeg(airportCurrent, airportDestination))
            {
                if ((r.Type == Route.RouteType.Cargo || r.Type == Route.RouteType.Mixed) && r.HasAirliner) routes.Add(r);
            }

            double flightsPerDay = Convert.ToDouble(routes.Sum(r => r.TimeTable.Entries.Count))/7;

            cargoDemand = cargoDemand/flightsPerDay;

            double totalCapacity;
            if (routes.Count > 0 && routes.Count(r => r.HasAirliner) > 0)
            {
                totalCapacity =
                    routes.Where(r => r.HasAirliner)
                          .Sum(r => r.GetAirliners().Max(a => (a.Airliner.Type as AirlinerCombiType)?.CargoSize ?? ((AirlinerCargoType) a.Airliner.Type).CargoSize));
                //SelectMany(r => r.Stopovers.Where(s=>s.Legs.Count >0))).Sum(s=>s.;//a => a.Routes.SelectMany(r=>r.Stopovers.SelectMany(s=>s.Legs.Where(l=>r.HasAirliner && (l.Destination1 == airportCurrent || l.Destination1 == airportDestination) && (l.Destination2 == airportDestination || l.Destination2 == airportCurrent))).Sum(r=>r.getAirliners().Max(a=>a.Airliner.getTotalSeatCapacity()));
            }
            else
            {
                totalCapacity = 0;
                // routes.Where(r => r.HasAirliner).Sum(r => r.getAirliners().Max(a => a.Airliner.getTotalSeatCapacity()));
            }

            double capacityPercent = cargoDemand > totalCapacity ? 1 : cargoDemand/totalCapacity;

            double priceDiff = basicCargoPrice/cargoPrice;

            double cargoPriceDiff = priceDiff < 0.75 ? priceDiff : 1;

            Route cargoRoute = airliner.CurrentFlight.Entry?.TimeTable?.Route;
            if (PricingService.Realistic)
            {
                // the rate already acted on demand; on a shared route the cheaper airline gets more
                cargoPriceDiff = cargoRoute != null ? PricingService.CargoCompetitionFactor(cargoRoute, routes) : 1;
            }

            cargoPriceDiff *= GameObject.GetInstance().Difficulty.PriceLevel;

            double randomCargo = Convert.ToDouble(Rnd.Next(97, 103))/100;

            var cargo = (int) Math.Min(capacity, (capacity*capacityPercent*randomCargo*cargoPriceDiff));

            // the month's load of the route
            ConnectionService.Record(cargoRoute, cargo, 0, (int) capacity);

            CargoTrace?.Invoke(new CargoFlightTrace
            {
                Airline = airliner.Airliner.Airline, Capacity = capacity, Cargo = cargo, DemandPerFlight = cargoDemand, TotalCapacity = totalCapacity,
                MarketPrice = basicCargoPrice, Price = cargoPrice, Km = distance, Facility = destinationFacilityFactor
            });

            return cargo;
        }

        public static int GetFlightPassengers(
            Airport airportCurrent,
            Airport airportDestination,
            FleetAirliner airliner,
            AirlinerClass.ClassType type)
        {
            double distance = MathHelpers.GetDistance(airportCurrent, airportDestination);

            Route currentRoute =
                airliner.Routes.Find(
                    r =>
                    r.Stopovers.SelectMany(s => s.Legs)
                     .ToList()
                     .Exists(
                         l =>
                         (l.Destination1 == airportCurrent || l.Destination1 == airportDestination)
                         && (l.Destination2 == airportDestination || l.Destination2 == airportCurrent))
                    || (r.Destination1 == airportCurrent || r.Destination1 == airportDestination)
                    && (r.Destination2 == airportDestination || r.Destination2 == airportCurrent));

            if (currentRoute == null)
            {
                return 0;
            }

            //double basicPrice = GetPassengerPrice(currentRoute.Destination1, currentRoute.Destination2, type);
            //double routePrice = ((PassengerRoute)currentRoute).getFarePrice(type);

            //double priceDiff = basicPrice / routePrice;

            long pfAll = Profiler.Start();
            long pf = Profiler.Start();
            double routeScoreFactor = RouteScore(currentRoute)/5;
            Profiler.Stop("pax: route score", pf);

            double demand = airportCurrent.GetDestinationPassengersRate(airportDestination, type);

            // passengers changing to or from the airline's (and its partners') other routes
            pf = Profiler.Start();
            double connecting = ConnectionService.FlightDemand(airliner.Airliner.Airline, currentRoute, airportCurrent, airportDestination, type);
            Profiler.Stop("pax: connecting demand", pf);
            pf = Profiler.Start();
            double nearby = GetNearbyPassengerDemand(airportCurrent, airportDestination, airliner, type);
            Profiler.Stop("pax: nearby demand", pf);
            double connectingShare = demand + connecting + nearby > 0 ? connecting/(demand + connecting + nearby) : 0;

            double traceBase = demand + connecting + nearby;
            double passengerDemand = (demand + connecting + nearby)
                                     *GetSeasonFactor(airportDestination)*GetHolidayFactor(airportDestination)
                                     *GetHolidayFactor(airportCurrent);

            passengerDemand *= GameObject.GetInstance().Difficulty.PassengersLevel;
            passengerDemand *= routeScoreFactor;

            // fewer passengers above the market fare, more below it
            if (currentRoute is PassengerRoute pricedRoute)
            {
                passengerDemand *= PricingService.DemandFactor(pricedRoute, type);
            }

            if (airliner.Airliner.Airline.MarketFocus == AirlineFocus.Global && distance > 3000
                && airportCurrent.Profile.Country != airportDestination.Profile.Country)
            {
                passengerDemand = passengerDemand*1.15;
            }

            if (airliner.Airliner.Airline.MarketFocus == AirlineFocus.Regional && distance < 1500)
            {
                passengerDemand = passengerDemand*1.15;
            }

            if (airliner.Airliner.Airline.MarketFocus == AirlineFocus.Domestic && distance < 1500
                && airportDestination.Profile.Country == airportCurrent.Profile.Country)
            {
                passengerDemand = passengerDemand*1.15;
            }

            if (airliner.Airliner.Airline.MarketFocus == AirlineFocus.Local && distance < 1000)
            {
                passengerDemand = passengerDemand*1.15;
            }

            pf = Profiler.Start();
            Hub hub = airportDestination.GetHubs().Find(h => h.Airline == airliner.Airliner.Airline);

            if (hub != null)
            {
                switch (hub.Type.Type)
                {
                    case HubType.TypeOfHub.FocusCity:
                        if (airportDestination.Profile.Country == airportCurrent.Profile.Country)
                        {
                            passengerDemand = passengerDemand*1.15;
                        }
                        break;

                    case HubType.TypeOfHub.RegionalHub:
                        if (airportDestination.Profile.Country.Region == airportCurrent.Profile.Country.Region)
                        {
                            passengerDemand = passengerDemand*1.20;
                        }
                        break;

                    case HubType.TypeOfHub.Hub:
                        passengerDemand = passengerDemand*1.20;
                        break;

                    case HubType.TypeOfHub.FortressHub:
                        passengerDemand = passengerDemand*1.30;
                        break;
                }
            }

            // every airline's flying passenger routes on this pair, nonstop or as a stopover leg
            var routes = new List<Route>();
            foreach (Route r in RouteIndex.Direct(airportCurrent, airportDestination))
            {
                if ((r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed) && r.HasAirliner) routes.Add(r);
            }
            foreach (Route r in RouteIndex.WithLeg(airportCurrent, airportDestination))
            {
                if ((r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed) && r.HasAirliner) routes.Add(r);
            }

            double flightsPerDay = Convert.ToDouble(routes.Sum(r => r.TimeTable.Entries.Count))/7;

            Profiler.Stop("pax: routes on the pair", pf);
            double traceDaily = passengerDemand;
            passengerDemand = passengerDemand/flightsPerDay;

            double totalCapacity;
            if (routes.Count > 0 && routes.Count(r => r.HasAirliner) > 0)
            {
                totalCapacity =
                    routes.Where(r => r.HasAirliner)
                          .Sum(r => r.GetAirliners().Max(a => a.Airliner.GetTotalSeatCapacity()));
                //SelectMany(r => r.Stopovers.Where(s=>s.Legs.Count >0))).Sum(s=>s.;//a => a.Routes.SelectMany(r=>r.Stopovers.SelectMany(s=>s.Legs.Where(l=>r.HasAirliner && (l.Destination1 == airportCurrent || l.Destination1 == airportDestination) && (l.Destination2 == airportDestination || l.Destination2 == airportCurrent))).Sum(r=>r.getAirliners().Max(a=>a.Airliner.getTotalSeatCapacity()));
            }
            else
            {
                totalCapacity = 0;
                // routes.Where(r => r.HasAirliner).Sum(r => r.getAirliners().Max(a => a.Airliner.getTotalSeatCapacity()));
            }

            double capacityPercent = passengerDemand > totalCapacity ? 1 : passengerDemand/totalCapacity;

            var rations = new Dictionary<Route, double>();

            foreach (Route route in routes)
            {
                double level = ((PassengerRoute) route).GetServiceLevel(type)
                               /((PassengerRoute) route).GetFarePrice(type);

                rations.Add(route, level);
            }

            double totalRatio = rations.Values.Sum();

            double routeRatioPercent = 1;

            if (PricingService.Realistic)
            {
                // the cheaper, better airline gets more of the passengers on a shared route
                routeRatioPercent = PricingService.CompetitionFactor(currentRoute, routes, type);
            }
            else if (rations.ContainsKey(currentRoute))
            {
                routeRatioPercent = Math.Max(1, rations[currentRoute]/Math.Max(1, totalRatio));
            }

            pf = Profiler.Start();
            double reputation = ReputationScale(airportCurrent, airliner.Airliner.Airline);
            Profiler.Stop("pax: reputation of all airlines", pf);

            if (reputation < 76)
            {
                reputation = 75;
            }

            double reputationPercent = reputation/100;

            double routePriceDiff = 1; // priceDiff < 0.75 ? priceDiff : 1;

            routePriceDiff *= GameObject.GetInstance().Difficulty.PriceLevel;

            double randomPax = Convert.ToDouble(Rnd.Next(97, 103))/100;

            var pax =
                (int)
                Math.Min(
                    airliner.Airliner.GetAirlinerClass(type).SeatingCapacity,
                    (airliner.Airliner.GetAirlinerClass(type).SeatingCapacity*routeRatioPercent
                     *reputationPercent*capacityPercent*routePriceDiff*randomPax));

            if (pax < 0)
            {
                pax = 0;
            }

            ConnectionService.Record(currentRoute, pax, pax*connectingShare, airliner.Airliner.GetAirlinerClass(type).SeatingCapacity);
            Profiler.Stop("pax: whole calculation", pfAll);

            FlightTrace?.Invoke(new FlightPaxTrace
            {
                Airline = airliner.Airliner.Airline, Route = currentRoute, Type = type, BaseDemand = traceBase, Direct = demand, Connecting = connecting, Nearby = nearby, DailyDemand = traceDaily,
                FlightsPerDay = flightsPerDay, PerFlight = passengerDemand, TotalCapacity = totalCapacity, CapacityPercent = capacityPercent,
                Competition = routeRatioPercent, Reputation = reputationPercent, Score = routeScoreFactor,
                Seats = airliner.Airliner.GetAirlinerClass(type).SeatingCapacity, Passengers = pax
            });

            return pax;
        }

        /// <summary>What went into one flight's passengers in one class (for the economy probe; off in normal play).</summary>
        public class FlightPaxTrace
        {
            public Airline Airline;
            public Route Route;
            public AirlinerClass.ClassType Type;
            public double BaseDemand, Direct, Connecting, Nearby, DailyDemand, FlightsPerDay, PerFlight, TotalCapacity, CapacityPercent, Competition, Reputation, Score;
            public int Seats, Passengers;
        }

        public static Action<FlightPaxTrace> FlightTrace;

        /// <summary>What went into one cargo flight's load (for the economy probe; off in normal play).</summary>
        public class CargoFlightTrace
        {
            public Airline Airline;
            public double Capacity, Cargo, DemandPerFlight, TotalCapacity, MarketPrice, Price, Km, Facility;
        }

        public static Action<CargoFlightTrace> CargoTrace;

        //returns the number of passengers for a flight
        public static int GetFlightPassengers(FleetAirliner airliner, AirlinerClass.ClassType type)
        {
            Airport airportCurrent = airliner.CurrentFlight.GetDepartureAirport();
            Airport airportDestination = airliner.CurrentFlight.Entry.Destination.Airport;

            return GetFlightPassengers(airportCurrent, airportDestination, airliner, type);
        }

        public static double GetHappinessValue(Airline airline)
        {
            if (HappinessPercent.ContainsKey(airline))
            {
                return HappinessPercent[airline];
            }
            return 0;
        }

        //returns the holiday factor for an airport

        //returns the suggested passenger price for a route
        public static double GetPassengerPrice(Airport dest1, Airport dest2)
        {
            // the market fare (see PricingService; the original curve when the original costs are chosen)
            return PricingService.MarketFare(MathHelpers.GetDistance(dest1, dest2));
        }

        public static double GetPassengerPrice(Airport dest1, Airport dest2, AirlinerClass.ClassType type)
        {
            return GetPassengerPrice(dest1, dest2)*GeneralHelpers.ClassToPriceFactor(type);
        }

        public static double GetPassengersHappiness(Airline airline)
        {
            double passengers = airline.Statistics.GetYears().Sum(year => Convert.ToDouble(airline.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("Passengers"))));
            double value = GetHappinessValue(airline);

            if (passengers.Equals(0))
            {
                return 0;
            }
            return value/passengers*100.0;
        }

        public static double GetStopoverFlightCargo(
            FleetAirliner airliner,
            Airport dept,
            Airport dest,
            List<Route> routes,
            bool isInbound)
        {
            Route currentRoute =
                routes.Find(
                    r =>
                    (r.Destination1 == dept && r.Destination2 == dest)
                    || (r.Destination2 == dept && r.Destination1 == dest));
            int index = routes.IndexOf(currentRoute);

            double cargo = 0;
            for (int i = 0; i <= index; i++)
            {
                if (isInbound)
                {
                    cargo += GetFlightCargo(routes[i].Destination2, dest, airliner);
                }
                else
                {
                    cargo += GetFlightCargo(routes[i].Destination1, dest, airliner);
                }
            }

            return Math.Min(airliner.Airliner.GetCargoCapacity(), cargo);
        }

        public static int GetStopoverFlightPassengers(
            FleetAirliner airliner,
            AirlinerClass.ClassType type,
            Airport dept,
            Airport dest,
            List<Route> routes,
            bool isInbound)
        {
            Route currentRoute =
                routes.Find(
                    r =>
                    (r.Destination1 == dept && r.Destination2 == dest)
                    || (r.Destination2 == dept && r.Destination1 == dest));
            int index = routes.IndexOf(currentRoute);

            int passengers = 0;
            for (int i = 0; i <= index; i++)
            {
                if (isInbound)
                {
                    passengers += GetFlightPassengers(routes[i].Destination2, dest, airliner, type);
                }
                else
                {
                    passengers += GetFlightPassengers(routes[i].Destination1, dest, airliner, type);
                }
            }

            if (passengers < 0)
            {
                passengers = 0;
            }

            return Math.Min(airliner.Airliner.GetAirlinerClass(type).SeatingCapacity, passengers);
        }

        //returns the number of passengers for a flight on a stopover route
        public static int GetStopoverFlightPassengers(FleetAirliner airliner, AirlinerClass.ClassType type)
        {
            RouteTimeTableEntry mainEntry = airliner.CurrentFlight.Entry.MainEntry;
            RouteTimeTableEntry entry = airliner.CurrentFlight.Entry;

            List<Route> legs = mainEntry.TimeTable.Route.Stopovers.SelectMany(s => s.Legs).ToList();

            bool isInbound = mainEntry.DepartureAirport == mainEntry.TimeTable.Route.Destination2;

            int passengers;
            //inboound
            if (isInbound)
            {
                legs.Reverse();
                passengers = GetFlightPassengers(
                    mainEntry.TimeTable.Route.Destination2,
                    mainEntry.TimeTable.Route.Destination1,
                    airliner,
                    type);
            }
            else
            {
                passengers = GetFlightPassengers(
                    mainEntry.TimeTable.Route.Destination1,
                    mainEntry.TimeTable.Route.Destination2,
                    airliner,
                    type);
            }

            int index = legs.IndexOf(entry.TimeTable.Route);

            for (int i = index; i < legs.Count; i++)
            {
                if (isInbound)
                {
                    passengers += GetFlightPassengers(
                        entry.TimeTable.Route.Destination1,
                        legs[i].Destination1,
                        airliner,
                        type);
                }
                else
                {
                    passengers += GetFlightPassengers(
                        entry.TimeTable.Route.Destination1,
                        legs[i].Destination2,
                        airliner,
                        type);
                }
            }

            if (passengers < 0)
            {
                passengers = 0;
            }

            return Math.Min(airliner.Airliner.GetAirlinerClass(type).SeatingCapacity, passengers);
        }

        public static void SetCargoSize()
        {
            List<Airport> airports = Airports.GetAllActiveAirports();

            foreach (Airport airport in airports)
            {
                var volume = (int) airport.Profile.CargoVolume;
                if (volume < 200)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.Smallest;
                    break;
                }

                if (volume < 400)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.VerySmall;
                    break;
                }
                if (volume < 750)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.Small;
                    break;
                }
                if (volume < 1500)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.Medium;
                    break;
                }

                if (volume < 2500)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.Large;
                    break;
                }

                if (volume > 2500)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.VeryLarge;
                    break;
                }
                if (volume > 4500)
                {
                    airport.Profile.Cargo = GeneralHelpers.Size.Largest;
                    break;
                }
            }
        }

        public static void SetPassengerHappiness(Airline airline, double value)
        {
            if (HappinessPercent.ContainsKey(airline))
            {
                HappinessPercent[airline] = value;
            }
            else
            {
                HappinessPercent.Add(airline, value);
            }
        }

        #endregion

        #region Methods

        private static void CreateDestinationPassengers(Airport airport, List<Airport> subAirports)
        {
            List<Airport> largestAirports =
                subAirports.FindAll(
                    a =>
                    a.Profile.Size == GeneralHelpers.Size.Largest
                    || a.Profile.Size == GeneralHelpers.Size.VeryLarge);

            int maxValue = Math.Max(2, (int) Math.Ceiling(airport.Profile.Pax/2));

            if (largestAirports.Count > 0)
            {
                foreach (Airport lAirport in largestAirports)
                {
                    airport.AddDestinationPassengersRate(
                        new DestinationDemand(lAirport.Profile.IATACode, (ushort) Rnd.Next(1, maxValue)));
                }
            }
            else
            {
                subAirports = subAirports.OrderByDescending(a => a.Profile.Size).ToList();
                airport.AddDestinationPassengersRate(
                    new DestinationDemand(subAirports[0].Profile.IATACode, (ushort) Rnd.Next(1, maxValue)));
            }
        }

        //creates the airport destination passengers a destination

        //returns the cargo mass per day for an airport
        private static double GetAirportCargoMass(Airport airport)
        {
            //density cargo 3000 kg/cu m.
            //1 cu m. vejer 3000 kg. eller 3.0 metric ton

            /*--------------------READ THE TWO LINES BELOW --- VERY USEFUL ----------------*/

            //for reference, most commercial aircraft have capacity of about 5.5(5.3-5.8)lb per cu ft
            //to convert values to cu meters, multiply the cargo cu footage * 35.3 (194lb/m3 and 88kg/m3 at standard density)

            //total cargo volume from/to airport in tonnes
            double totalCargoVolume = airport.Profile.CargoVolume.Equals(0)
                                          ? ((int) airport.Profile.Cargo + 1)*10000
                                          : airport.Profile.CargoVolume*1000; //in metric ton
            const double cargoDensity = 3000; //kg/cu m

            //in cargo mass
            double cargoMass = totalCargoVolume/(cargoDensity/1000);

            double cargoMassPerDay = cargoMass/365;

            return cargoMassPerDay;
        }

        //sets the cargo sizes based on the volume

        //returns the demand factor based on the year of playing
        private static double GetDemandYearFactor(int year)
        {
            double yearDiff = Convert.ToDouble(year - GameObject.StartYear)/10;

            return 0.15*(yearDiff + 1);
        }

        private static double GetHolidayFactor(Airport airport)
        {
            if (HolidayYear.IsHoliday(airport.Profile.Country, GameObject.GetInstance().GameTime))
            {
                HolidayYearEvent holiday = HolidayYear.GetHoliday(
                    airport.Profile.Country,
                    GameObject.GetInstance().GameTime);

                if (holiday.Holiday.Travel == Holiday.TravelType.Both
                    || holiday.Holiday.Travel == Holiday.TravelType.Travel)
                {
                    return 1.5;
                }
            }
            return 1;
        }

        /// <summary>Passengers from airports near the start who would use the flight (for the route forecast).</summary>
        public static double NearbyDemandFor(Airport from, Airport to, FleetAirliner airliner, AirlinerClass.ClassType type)
        {
            return GetNearbyPassengerDemand(from, to, airliner, type);
        }

        /// <summary>The season's effect on the demand to an airport today (for the route forecast).</summary>
        /// <summary>A holiday's effect on the demand at an airport today (for the route forecast).</summary>
        public static double HolidayFactorFor(Airport airport)
        {
            return GetHolidayFactor(airport);
        }

        /// <summary>A route's service score as the flights use it today (out of 10).</summary>
        public static double RouteScoreFor(Route route)
        {
            return RouteScore(route);
        }

        public static double SeasonFactorFor(Airport airport)
        {
            return GetSeasonFactor(airport);
        }

        /// <summary>The share the airline's reputation at the airport gives (0.75 to 1, as for a flight).</summary>
        public static double ReputationShare(Airport airport, Airline airline)
        {
            return Math.Max(75, ReputationScale(airport, airline))/100;
        }

        // a route's service score (aircraft age, meals, seats, fare and so on), worked out once a game day per route
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Route, Tuple<DateTime, double>> RouteScoreCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Route, Tuple<DateTime, double>>();

        private static double RouteScore(Route route)
        {
            DateTime day = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (RouteScoreCache.TryGetValue(route, out cached) && cached.Item1 == day) return cached.Item2;
            double score = RouteHelpers.GetRouteTotalScore(route);
            RouteScoreCache.AddOrUpdate(route, Tuple.Create(day, score));
            return score;
        }

        // each airline's reputation at an airport on a 1-100 scale against all airlines, worked out once a game day
        // per airport (it looks at every airline's routes and facilities there, which used to happen for every flight)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Airport, Tuple<DateTime, IDictionary<Airline, double>>> ReputationCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Airport, Tuple<DateTime, IDictionary<Airline, double>>>();

        private static double ReputationScale(Airport airport, Airline airline)
        {
            DateTime day = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, IDictionary<Airline, double>> cached;
            double value;
            if (ReputationCache.TryGetValue(airport, out cached) && cached.Item1 == day && cached.Item2.TryGetValue(airline, out value))
            {
                return value;
            }

            IDictionary<Airline, double> airlineScores = new Dictionary<Airline, double>();
            foreach (Airline a in Airlines.GetAllAirlines())
            {
                if (!airlineScores.ContainsKey(a)) airlineScores.Add(a, airport.GetAirlineReputation(a));
            }
            if (!airlineScores.ContainsKey(airline)) airlineScores.Add(airline, airport.GetAirlineReputation(airline));

            IDictionary<Airline, double> scale = StatisticsHelpers.GetRatingScale(airlineScores);
            ReputationCache.AddOrUpdate(airport, Tuple.Create(day, scale));
            return scale[airline];
        }

        private static double GetNearbyPassengerDemand(
            Airport airportCurrent,
            Airport airportDestination,
            FleetAirliner airliner,
            AirlinerClass.ClassType type)
        {
            TimeSpan flightTime = MathHelpers.GetFlightTime(airportCurrent, airportDestination, airliner.Airliner.Type);

            double maxDistance = (flightTime.TotalHours*0.5)*100;

            IEnumerable<Airport> nearbyAirports =
                AirportHelpers.GetAirportsNearAirport(airportCurrent, maxDistance)
                              .DefaultIfEmpty()
                              .Where(a => !AirportHelpers.HasRoute(a, airportDestination));

            double demand = 0;

            foreach (Airport airport in nearbyAirports)
            {
                if (airport != null)
                {
                    double distance = MathHelpers.GetDistance(airportCurrent, airport);

                    double airportDemand = airport.GetDestinationPassengersRate(airportDestination, type);

                    if (distance < 150)
                    {
                        demand += airportDemand*0.75;
                    }

                    if (distance >= 150 && distance < 225)
                    {
                        demand += airportDemand*0.5;
                    }

                    if (distance >= 225 && distance < 300)
                    {
                        demand += airportDemand*0.25;
                    }

                    if (distance >= 300 && distance < 400)
                    {
                        demand += airportDemand*0.10;
                    }
                }
            }

            return demand;
        }

        private static Airport GetRandomDestination(Airport currentAirport)
        {
            var airportsList = new Dictionary<Airport, int>();
            Airports.GetAirports(
                a =>
                currentAirport != null && a != currentAirport
                && !FlightRestrictions.HasRestriction(
                    currentAirport.Profile.Country,
                    a.Profile.Country,
                    GameObject.GetInstance().GameTime,
                    FlightRestriction.RestrictionType.Flights))
                    .ForEach(
                        a =>
                        airportsList.Add(
                            a,
                            (int) a.Profile.Size*(a.Profile.Country == currentAirport.Profile.Country ? 7 : 3)));

            if (airportsList.Count > 0)
            {
                return AIHelpers.GetRandomItem(airportsList);
            }
            return null;
        }

        // (the factors were written as 150/100 and 50/100, whole-number divisions that came out as 1 and 0: a summer
        // airport got no passengers at all from September to February, a winter airport none from March to August)
        private static double GetSeasonFactor(Airport airport)
        {
            bool isSummer = GameObject.GetInstance().GameTime.Month >= 3
                               && GameObject.GetInstance().GameTime.Month < 9;

            if (airport.Profile.Season == Weather.Season.AllYear)
            {
                return 1;
            }
            if (airport.Profile.Season == Weather.Season.Summer)
            {
                if (isSummer)
                {
                    return 1.5;
                }
                return 0.5;
            }
            if (airport.Profile.Season == Weather.Season.Winter)
            {
                if (isSummer)
                {
                    return 0.5;
                }
                return 1.5;
            }

            return 1;
        }

        #endregion

        //returns the passengers happiness for an airline

        //adds happiness to an airline

        //creates the random airport destination for a list of destinations

        //changes the demand for all airports with a factor
    }
}