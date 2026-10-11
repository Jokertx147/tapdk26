using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    //the helper class for loading and saving using serialization
    public class SerializedLoadSaveHelpers
    {
        #region Public Methods and Operators

        public static void DeleteSavedGame(string name)
        {
            string fileName = AppSettings.GetCommonApplicationDataPath() + "\\saves\\" + name + ".sav";

            File.Delete(fileName);
        }

        //returns if a saved game exits

        //loads a game 
        public static void LoadGame(string file)
        {
            string fileName = AppSettings.GetCommonApplicationDataPath() + "\\saves\\" + file + ".sav";

            StartupLog.Write("LoadGame: reading " + fileName + " (" + (File.Exists(fileName) ? new FileInfo(fileName).Length / 1024 + " KB" : "missing") + ")");
            var deserializedSaveObject = FileSerializer.Deserialize<SaveObject>(fileName);
            StartupLog.Write("LoadGame: file read, " + (deserializedSaveObject == null ? "NOTHING could be read" : "restoring the game"));
            if (deserializedSaveObject == null)
            {
                throw new InvalidDataException("The save file could not be read, see startup.log");
            }

            /*
          
           DataContractSerializer serializer = new DataContractSerializer(typeof(SaveObject));
           SaveObject deserializedSaveObject;
           string loading;
           int version;

           using (FileStream stream = new FileStream(fileName, FileMode.Open))
           {
               using (DeflateStream decompress = new DeflateStream(stream, CompressionMode.Decompress))
               {
                   deserializedSaveObject = (SaveObject)serializer.ReadObject(decompress);
               }
           }

           loading = deserializedSaveObject.savetype;
           version = deserializedSaveObject.saveversionnumber;
           */
            //Parrarel for loading the game
            StartupLog.Write("LoadGame: restoring lists");
            Parallel.Invoke(
                () =>
                    {
                        Airlines.Clear();

                        foreach (Airline airline in deserializedSaveObject.AirlinesList)
                        {
                            Airlines.AddAirline(airline);
                        }
                    },
                () =>
                    {
                        Setup.LoadAirports();

                        List<Airport> airports = Airports.GetAllAirports();

                        Airports.Clear();

                        foreach (Airport airport in deserializedSaveObject.AirportsList)
                        {
                            airport.Statics = new AirportStatics(airport);
                            Airports.AddAirport(airport);
                        }

                        foreach (string iata in deserializedSaveObject.AirportsFromStringList)
                        {
                            Airport airport = airports.FirstOrDefault(a => a.Profile.IATACode == iata);

                            if (airport != null)
                            {
                                Airports.AddAirport(airport);
                            }
                        }
                    },
                () =>
                    {
                        Airliners.Clear();

                        foreach (Airliner airliner in deserializedSaveObject.AirlinersList)
                        {
                            Airliners.AddAirliner(airliner);
                        }
                    },
                () =>
                    {
                        CalendarItems.Clear();

                        foreach (CalendarItem item in deserializedSaveObject.CalendarItemsList)
                        {
                            CalendarItems.AddCalendarItem(item);
                        }
                    },
                () =>
                    {
                        Configurations.Clear();

                        foreach (Configuration configuration in deserializedSaveObject.ConfigurationList)
                        {
                            Configurations.AddConfiguration(configuration);
                        }
                    },
                () =>
                    {
                        RandomEvents.Clear();

                        foreach (RandomEvent e in deserializedSaveObject.EventsList)
                        {
                            RandomEvents.AddEvent(e);
                        }
                    },
                () =>
                    {
                        Alliances.Clear();

                        foreach (Alliance alliance in deserializedSaveObject.AllianceList)
                        {
                            Alliances.AddAlliance(alliance);
                        }
                    },
                () =>
                    {
                        AirportFacilities.Clear();

                        foreach (AirportFacility facility in deserializedSaveObject.AirportFacilitiesList)
                        {
                            AirportFacilities.AddFacility(facility);
                        }
                    },
                () =>
                    {
                        FeeTypes.Clear();

                        foreach (FeeType type in deserializedSaveObject.FeeTypeslist)
                        {
                            FeeTypes.AddType(type);
                        }
                    },
                () =>
                    {
                        AdvertisementTypes.Clear();

                        foreach (AdvertisementType addtype in deserializedSaveObject.AdvertisementTypesList)
                        {
                            AdvertisementTypes.AddAdvertisementType(addtype);
                        }
                    },
                () =>
                    {
                        AirlinerFacilities.Clear();

                        foreach (AirlinerFacility airlinerfas in deserializedSaveObject.AirlinerFacilitiesList)
                        {
                            AirlinerFacilities.AddFacility(airlinerfas);
                        }
                    },
                () =>
                    {
                        RouteFacilities.Clear();

                        foreach (RouteFacility routefas in deserializedSaveObject.RouteFacilitiesList)
                        {
                            RouteFacilities.AddFacility(routefas);
                        }
                    },
                () =>
                    {
                        GameObject.SetInstance(deserializedSaveObject.Instance);
                        // the money shown in the header is a separate value that is not part of the save
                        if (GameObject.GetInstance().HumanAirline != null)
                        {
                            GameObject.GetInstance().HumanMoney = GameObject.GetInstance().HumanAirline.Money;
                        }
                        Infrastructure.Settings.SetInstance(deserializedSaveObject.Settings);
                    },
                () =>
                    {
                        if (deserializedSaveObject.AirlineFacilitiesList != null)
                        {
                            AirlineFacilities.Clear();

                            foreach (AirlineFacility airlinefac in deserializedSaveObject.AirlineFacilitiesList)
                            {
                                AirlineFacilities.AddFacility(airlinefac);
                            }
                        }
                    }); //close parallel.invoke

            StartupLog.Write("LoadGame: lists restored");
            //for 0.3.9.2 and the issue with no saved facilities on a route classes
            IEnumerable<RouteClassConfiguration> emptyRouteClassesFacilities =
                Configurations.GetConfigurations(Configuration.ConfigurationType.Routeclasses)
                              .SelectMany(c => ((RouteClassesConfiguration) c).Classes.Where(cl => cl.Facilities == null));

            foreach (RouteClassConfiguration rClassConfiguration in emptyRouteClassesFacilities)
            {
                rClassConfiguration.Facilities = new List<RouteFacility>();
            }


            StartupLog.Write("LoadGame: running SetupLoadedGame");
            Setup.SetupLoadedGame();
            StartupLog.Write("LoadGame: SetupLoadedGame done");
            //Maybe this helps? But i doubt this is the best way
            Action action = () =>
                {
                    var swPax = new Stopwatch();
                    swPax.Start();

                    PassengerHelpers.CreateDestinationDemand();

                    //Console.WriteLine("Demand have been created in {0} ms.", swPax.ElapsedMilliseconds);
                    swPax.Stop();
                };


            StartupLog.Write("LoadGame: creating pilots");
            //create some pilots for the game
            int pilotsPool = 100*Airlines.GetAllAirlines().Count;
            GeneralHelpers.CreatePilots(pilotsPool);
            int instructorsPool = 75*Airlines.GetAllAirlines().Count;
            GeneralHelpers.CreateInstructors(instructorsPool);

            StartupLog.Write("LoadGame: creating start up airliners");
            //creates some airliners for the game
            AirlinerHelpers.CreateStartUpAirliners();

            StartupLog.Write("LoadGame: starting the game paused");
            //Start the game paused
            GameObjectWorker.GetInstance().StartPaused();

            //Task is needed this unlocks the game agian.
            Task.Factory.StartNew(action);
            StartupLog.Write("LoadGame: finished");
        }

        public static void SaveGame(string name)
        {
            var sw = new Stopwatch();
            sw.Start();

            StartupLog.Write("SaveGame: " + name + " start");
            var so = new SaveObject();
            Parallel.Invoke(
                () =>
                    {
                        so.AirportsList = new List<Airport>();
                        so.AirportsFromStringList = new List<string>();

                        IEnumerable<Airport> airportsInUse =
                            Airports.GetAllAirports()
                                    .Where(
                                        a =>
                                        Airlines.GetAllAirlines().Exists(al => al.Airports.Contains(a))
                                        || a.HasAirlineFacility());
                        Airport[] inUse = airportsInUse as Airport[] ?? airportsInUse.ToArray();
                        so.AirportsList.AddRange(inUse);

                        foreach (Airport airport in Airports.GetAirports(a => !inUse.Contains(a)))
                        {
                            so.AirportsFromStringList.Add(airport.Profile.IATACode);
                        }
                    },
                () =>
                    {
                        so.AirlinesList = new List<Airline>();
                        so.AirlinesList.AddRange(Airlines.GetAllAirlines());
                    },
                () =>
                    {
                        so.AirlinersList = new List<Airliner>();
                        so.AirlinersList.AddRange(Airliners.GetAllAirliners().Where(a => a.Airline != null));
                    },
                () =>
                    {
                        so.CalendarItemsList = new List<CalendarItem>();
                        so.CalendarItemsList.AddRange(CalendarItems.GetCalendarItems());
                    },
                () =>
                    {
                        so.ConfigurationList = new List<Configuration>();
                        so.ConfigurationList.AddRange(Configurations.GetConfigurations());
                    },
                () =>
                    {
                        so.EventsList = new List<RandomEvent>();
                        so.EventsList.AddRange(RandomEvents.GetEvents());
                    },
                () =>
                    {
                        so.AllianceList = new List<Alliance>();
                        so.AllianceList.AddRange(Alliances.GetAlliances());
                    },
                () =>
                    {
                        so.AirportFacilitiesList = new List<AirportFacility>();
                        so.AirportFacilitiesList.AddRange(AirportFacilities.GetFacilities());
                    },
                () =>
                    {
                        so.FeeTypeslist = new List<FeeType>();
                        so.FeeTypeslist.AddRange(FeeTypes.GetTypes());
                    },
                () =>
                    {
                        so.AdvertisementTypesList = new List<AdvertisementType>();
                        so.AdvertisementTypesList.AddRange(AdvertisementTypes.GetTypes());
                    },
                () =>
                    {
                        so.AirlinerFacilitiesList = new List<AirlinerFacility>();
                        so.AirlinerFacilitiesList.AddRange(AirlinerFacilities.GetAllFacilities());
                    },
                () =>
                    {
                        so.RouteFacilitiesList = new List<RouteFacility>();
                        so.RouteFacilitiesList.AddRange(RouteFacilities.GetAllFacilities());
                    },
                () =>
                    {
                        so.Instance = GameObject.GetInstance();
                        so.Settings = Infrastructure.Settings.GetInstance();
                    },
                () =>
                    {
                        so.AirlineFacilitiesList = new List<AirlineFacility>();
                        so.AirlineFacilitiesList.AddRange(AirlineFacilities.GetFacilities());
                    });

            string fileName = AppSettings.GetCommonApplicationDataPath() + "\\saves\\" + name + ".sav";

            StartupLog.Write("SaveGame: collected the data in " + sw.ElapsedMilliseconds + " ms, writing " + fileName);
            FileSerializer.Serialize(fileName, so);
            StartupLog.Write("SaveGame: written, " + (File.Exists(fileName) ? new FileInfo(fileName).Length / 1024 + " KB" : "NO FILE"));

            sw.Stop();
            Console.WriteLine(@"Saving: {0} ms", sw.ElapsedMilliseconds);

            //Clearing stats because there is no need for saving those.
            if (name != "autosave")
            {
                Airports.GetAllAirports().ForEach(a => a.ClearDestinationPassengerStatistics());
                Airports.GetAllAirports().ForEach(a => a.ClearDestinationCargoStatistics());
                AirlineHelpers.ClearRoutesStatistics();
                AirlineHelpers.ClearAirlinesStatistics();
                AirportHelpers.ClearAirportStatistics();
            }
            /*
            SaveObject so = new SaveObject();
            Parallel.Invoke(() =>
            {
                so.airportsList = new List<Airport>();
                so.airportsList.AddRange(Airports.GetAllAirports());
            }, () =>
            {
                so.airlinesList = new List<Airline>();
                so.airlinesList.AddRange(Airlines.GetAllAirlines());
            }, () =>
            {
                so.airlinersList = new List<Airliner>();
                so.airlinersList.AddRange(Airliners.GetAllAirliners());
            }, () =>
            {
                so.calendaritemsList = new List<CalendarItem>();
                so.calendaritemsList.AddRange(CalendarItems.GetCalendarItems());
            }, () =>
            {
                so.configurationList = new List<Configuration>();
                so.configurationList.AddRange(Configurations.GetConfigurations());
            }, () =>
            {
                so.eventsList = new List<RandomEvent>();
                so.eventsList.AddRange(RandomEvents.GetEvents());
            }, () =>
            {
                so.allianceList = new List<Alliance>();
                so.allianceList.AddRange(Alliances.GetAlliances());
            }, () =>
            {
                so.Airportfacilitieslist = new List<AirportFacility>();
                so.Airportfacilitieslist.AddRange(AirportFacilities.GetFacilities());
            }, () =>
            {
                so.feeTypeslist = new List<FeeType>();
                so.feeTypeslist.AddRange(FeeTypes.GetTypes());
            }, () =>
            {
                so.advertisementTypeslist = new List<AdvertisementType>();
                so.advertisementTypeslist.AddRange(AdvertisementTypes.GetTypes());
            }, () =>
            {
                so.airlinerfacilitieslist = new List<AirlinerFacility>();
                so.airlinerfacilitieslist.AddRange(AirlinerFacilities.GetAllFacilities());
            }, () =>
            {
                so.routefacilitieslist = new List<RouteFacility>();
                so.routefacilitieslist.AddRange(RouteFacilities.GetAllFacilities());
            }, () =>
            {
                so.instance = GameObject.GetInstance();
                so.settings = Settings.GetInstance();
                so.savetype = "039";
                so.saveversionnumber = 1;
            });
            */
            /*
            DataContractSerializer serializer = new DataContractSerializer(typeof(SaveObject), null, Int32.MaxValue, false, true, null);

            using (Stream stream = new FileStream(fileName, FileMode.Create))
            {
                using (DeflateStream compress = new DeflateStream(stream, CompressionLevel.Fastest))
                {
                    serializer.WriteObject(compress, so);
                }
            }
          */
        }

        public static bool SaveGameExists(string name)
        {
            string fileName = AppSettings.GetCommonApplicationDataPath() + "\\saves\\" + name + ".sav";

            return File.Exists(fileName);
        }

        #endregion

        //deletes a saved game
    }

    [Serializable]
    public class SaveObject
    {
        #region Public Properties

        [Versioning("airportfacilities")]
        public List<AirportFacility> AirportFacilitiesList { get; set; }

        [Versioning("advertisementtypes")]
        public List<AdvertisementType> AdvertisementTypesList { get; set; }

        [Versioning("airlinefacilities")]
        public List<AirlineFacility> AirlineFacilitiesList { get; set; }

        [Versioning("airlinerfacilities")]
        public List<AirlinerFacility> AirlinerFacilitiesList { get; set; }

        [Versioning("airliners")]
        public List<Airliner> AirlinersList { get; set; }

        [Versioning("airlines")]
        public List<Airline> AirlinesList { set; get; }

        [Versioning("airports")]
        public List<Airport> AirportsList { set; get; }

        [Versioning("airportsfromstrings")]
        public List<string> AirportsFromStringList { get; set; }

        [Versioning("alliances")]
        public List<Alliance> AllianceList { get; set; }

        [Versioning("calendaritems")]
        public List<CalendarItem> CalendarItemsList { get; set; }

        [Versioning("configurations")]
        public List<Configuration> ConfigurationList { get; set; }

        [Versioning("events")]
        public List<RandomEvent> EventsList { get; set; }

        [Versioning("feetypes")]
        public List<FeeType> FeeTypeslist { get; set; }

        [Versioning("instance")]
        public GameObject Instance { get; set; }

        [Versioning("routefacilities")]
        public List<RouteFacility> RouteFacilitiesList { get; set; }

        public string SaveType { get; set; }

        public int SaveVersionNumber { get; set; }

        [Versioning("settings")]
        public Infrastructure.Settings Settings { get; set; }

        #endregion
    }

    /// <summary>
    ///     Finds types by name while reading and writing a save. The game stores many values (nested enums, nested classes,
    ///     closed generics such as Period of DateTime) inside serialization info with their runtime type, so a fixed list of
    ///     known types cannot cover them all.
    /// </summary>
    public sealed class SaveTypeResolver : DataContractResolver
    {
        private readonly System.Xml.XmlDictionary _dictionary = new System.Xml.XmlDictionary();

        private static bool IsBuiltIn(Type type)
        {
            return type.IsPrimitive || type == typeof (string) || type == typeof (decimal) || type == typeof (DateTime)
                   || type == typeof (TimeSpan) || type == typeof (Guid) || type == typeof (DateTimeOffset)
                   || type == typeof (byte[]);
        }

        public override bool TryResolveType(Type type, Type declaredType, DataContractResolver knownTypeResolver,
            out System.Xml.XmlDictionaryString typeName, out System.Xml.XmlDictionaryString typeNamespace)
        {
            if (IsBuiltIn(type))
            {
                return knownTypeResolver.TryResolveType(type, declaredType, null, out typeName, out typeNamespace);
            }

            // everything else is named by its assembly and full name, and found again by the same
            typeNamespace = _dictionary.Add(type.Assembly.FullName);
            typeName = _dictionary.Add(type.FullName);
            return true;
        }

        public override Type ResolveName(string typeName, string typeNamespace, Type declaredType,
            DataContractResolver knownTypeResolver)
        {
            if (typeNamespace.StartsWith("http://", StringComparison.Ordinal))
            {
                return knownTypeResolver.ResolveName(typeName, typeNamespace, declaredType, null);
            }

            var assemblyName = new System.Reflection.AssemblyName(typeNamespace).Name;
            return System.Reflection.Assembly.Load(assemblyName).GetType(typeName);
        }
    }

    public static class FileSerializer
    {
        //BinaryFormatter is removed from modern .NET, so saves use DataContractSerializer.
        private static DataContractSerializer CreateSerializer(Type type)
        {
            return new DataContractSerializer(type, new DataContractSerializerSettings
            {
                DataContractResolver = new SaveTypeResolver(),
                MaxItemsInObjectGraph = int.MaxValue,
                PreserveObjectReferences = true
            });
        }

        #region Public Methods and Operators

        public static T Deserialize<T>(string filename)
        {
            T objectToSerialize = default(T);

            Stream stream = null;

            try
            {
                stream = File.Open(filename, FileMode.Open);

                var serializer = CreateSerializer(typeof (T));

                objectToSerialize = (T) serializer.ReadObject(stream);
            }

            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
                StartupLog.Write("Reading the save failed: " + err);
            }

            finally
            {
                stream?.Close();
            }

            return objectToSerialize;
        }

        public static void Serialize(string filename, object objectToSerialize)
        {
            if (objectToSerialize == null)
            {
                throw new ArgumentNullException(nameof(objectToSerialize));
            }

            // write to a temporary file and replace the save only when that worked, so a failed save never leaves
            // an empty or half written file behind
            var temp = filename + ".tmp";

            try
            {
                using (var stream = File.Open(temp, FileMode.Create))
                {
                    var serializer = CreateSerializer(objectToSerialize.GetType());

                    serializer.WriteObject(stream, objectToSerialize);
                }

                File.Copy(temp, filename, true);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                StartupLog.Write("Writing the save failed: " + e);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }
            }
        }

        #endregion
    }
}
