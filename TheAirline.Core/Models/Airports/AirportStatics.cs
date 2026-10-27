using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Passengers;

namespace TheAirline.Models.Airports
{
    //some static values for an airport
    public class AirportStatics
    {
        #region Fields

        private readonly Dictionary<Airport, double> _airportDistances;

        private readonly List<DestinationDemand> _cargoDemand;

        private readonly List<DestinationDemand> _passengerDemand;

        // the same demand objects by destination code (the first one for a code, as a search of the list finds):
        // looking a destination up in the list took a pass over thousands of entries. Built on the first look-up,
        // so only airports that are actually asked about (the ones in use) pay the memory for it.
        private Dictionary<string, DestinationDemand> _passengerIndex;
        private Dictionary<string, DestinationDemand> _cargoIndex;

        private static Dictionary<string, DestinationDemand> BuildIndex(List<DestinationDemand> list)
        {
            var index = new Dictionary<string, DestinationDemand>(list.Count);
            foreach (DestinationDemand d in list)
                if (d?.Destination != null && !index.ContainsKey(d.Destination)) index[d.Destination] = d;
            return index;
        }

        private static void AddToIndex(Dictionary<string, DestinationDemand> index, DestinationDemand d)
        {
            if (index != null && d?.Destination != null && !index.ContainsKey(d.Destination)) index[d.Destination] = d;
        }

        // nearby airports, worked out from the positions on the first question (the distances kept above only hold
        // the airports the demand building happened to store for this one): within NearbyRange km, closest first
        private List<KeyValuePair<double, Airport>> _nearby;
        private double _nearbyRange;

        // the position (from 1) of each class in the class list, which divides the demand between the classes
        private static readonly Dictionary<AirlinerClass.ClassType, int> ClassFactors =
            Enum.GetValues(typeof (AirlinerClass.ClassType)).Cast<AirlinerClass.ClassType>()
                .Select((t, i) => new {t, i}).GroupBy(x => x.t).ToDictionary(g => g.Key, g => g.Last().i + 1);

        #endregion

        #region Constructors and Destructors

        public AirportStatics(Models.Airports.Airport airport)
        {
            _airportDistances = new Dictionary<Airport, double>();
            _passengerDemand = new List<DestinationDemand>();
            _cargoDemand = new List<DestinationDemand>();
            Airport = airport;
        }

        #endregion

        #region Public Properties

        public Models.Airports.Airport Airport { get; set; }

        #endregion

        #region Public Methods and Operators

        public void AddCargoDemand(DestinationDemand demand)
        {
            lock (_cargoDemand)
            {
                _cargoDemand.Add(demand);
                AddToIndex(_cargoIndex, demand);
            }
        }

        //adds a distance to the airport
        public void AddDistance(Models.Airports.Airport airport, double distance)
        {
            lock (_airportDistances)
            {
                if (!_airportDistances.ContainsKey(airport))
                {
                    _airportDistances.Add(airport, distance);
                }
            }
        }

        //sets (replaces) the base cargo demand to a destination
        public void SetCargoDemand(Models.Airports.Airport destination, ushort rate)
        {
            lock (_cargoDemand)
            {
                if (_cargoIndex == null) _cargoIndex = BuildIndex(_cargoDemand);
                DestinationDemand demand;
                _cargoIndex.TryGetValue(destination.Profile.IATACode, out demand);
                if (demand == null)
                {
                    demand = new DestinationDemand(destination.Profile.IATACode, rate);
                    _cargoDemand.Add(demand);
                    AddToIndex(_cargoIndex, demand);
                }
                else
                {
                    demand.Rate = rate;
                }
            }
        }

        //sets (replaces) the base passenger demand to a destination
        public void SetPassengerDemand(Models.Airports.Airport destination, ushort rate)
        {
            lock (_passengerDemand)
            {
                if (_passengerIndex == null) _passengerIndex = BuildIndex(_passengerDemand);
                DestinationDemand demand;
                _passengerIndex.TryGetValue(destination.Profile.IATACode, out demand);
                if (demand == null)
                {
                    demand = new DestinationDemand(destination.Profile.IATACode, rate);
                    _passengerDemand.Add(demand);
                    AddToIndex(_passengerIndex, demand);
                }
                else
                {
                    demand.Rate = rate;
                }
            }
        }

        public void AddPassengerDemand(DestinationDemand demand)
        {
            lock (_passengerDemand)
            {
                _passengerDemand.Add(demand);
                AddToIndex(_passengerIndex, demand);
            }
        }

        public List<Airport> GetAirportsWithin(double range)
        {
            List<KeyValuePair<double, Airport>> nearby;
            lock (_airportDistances)
            {
                if (_nearby == null || range > _nearbyRange)
                {
                    // every airport of the game within the range (at least 1,000 km, so later questions reuse it);
                    // like the distances stored while building the demand, not airports of the same town or closer
                    // than 50 km
                    double keep = Math.Max(1000, range);
                    var me = Airport.Profile.Coordinates.ConvertToGeoCoordinate();
                    var list = new List<KeyValuePair<double, Airport>>();
                    foreach (Models.Airports.Airport airport in Models.Airports.Airports.GetAllAirports())
                    {
                        if (airport == Airport || airport.Profile.Town == Airport.Profile.Town) continue;
                        double d = me.GetDistanceTo(airport.Profile.Coordinates.ConvertToGeoCoordinate())/1000;
                        if (d > 50 && d <= keep) list.Add(new KeyValuePair<double, Airport>(d, airport));
                    }
                    list.Sort((a, b) => a.Key.CompareTo(b.Key));
                    _nearby = list;
                    _nearbyRange = keep;
                }
                nearby = _nearby;
            }

            var airports = new List<Airport>();
            foreach (var kv in nearby)
            {
                if (kv.Key > range) break;
                airports.Add(kv.Value);
            }
            return airports;
        }

        public List<DestinationDemand> GetDemands()
        {
            var demands = new List<DestinationDemand>();

            demands.AddRange(_cargoDemand);
            demands.AddRange(_passengerDemand);

            return demands;
        }

        //returns if the airport have passenger demand to another airport

        //returns the destination cargo for a specific destination
        public ushort GetDestinationCargoRate(Models.Airports.Airport destination)
        {
            DestinationDemand cargo;
            lock (_cargoDemand)
            {
                if (_cargoIndex == null) _cargoIndex = BuildIndex(_cargoDemand);
                _cargoIndex.TryGetValue(destination.Profile.IATACode ?? "", out cargo);
            }

            if (cargo == null)
            {
                return 0;
            }
            return cargo.Rate;
        }

        //returns the destination passengers for a specific destination for a class
        public ushort GetDestinationPassengersRate(Models.Airports.Airport destination, AirlinerClass.ClassType type)
        {
            DestinationDemand pax;
            lock (_passengerDemand)
            {
                if (_passengerIndex == null) _passengerIndex = BuildIndex(_passengerDemand);
                _passengerIndex.TryGetValue(destination.Profile.IATACode ?? "", out pax);
            }

            int classFactor;
            ClassFactors.TryGetValue(type, out classFactor);

            if (pax == null)
            {
                return 0;
            }
            return (ushort) (pax.Rate/classFactor);
        }

        public int GetDestinationPassengersSum()
        {
            int sum;
            lock (_passengerDemand)
            {
                sum = _passengerDemand.Sum(p => p.Rate);
            }
            return sum;
        }

        //returns all demands

        //returns the distance for an airport
        public double GetDistance(Models.Airports.Airport airport)
        {
            double distance;
            lock (_airportDistances)
            {
                distance = _airportDistances.ContainsKey(airport) ? _airportDistances[airport] : 0;
            }

            return distance;
        }

        public bool HasDestinationCargoRate(Models.Airports.Airport destination)
        {
            lock (_cargoDemand)
            {
                if (_cargoIndex == null) _cargoIndex = BuildIndex(_cargoDemand);
                return _cargoIndex.ContainsKey(destination.Profile.IATACode ?? "");
            }
        }

        public bool HasDestinationPassengersRate(Models.Airports.Airport destination)
        {
            lock (_passengerDemand)
            {
                if (_passengerIndex == null) _passengerIndex = BuildIndex(_passengerDemand);
                return _passengerIndex.ContainsKey(destination.Profile.IATACode ?? "");
            }
        }

        #endregion

        //adds passenger demand to the airport

        //adds cargo demand to the airport

        //returns all airports within a range
    }
}