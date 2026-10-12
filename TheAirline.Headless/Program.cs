using System;
using System.Linq;
using System.Threading;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using TheAirline.Models.General.Environment;

class P {
  static void Main() {
    try {
      GameDataLoader.EnsureLoaded();
      Console.WriteLine("LOADED airports=" + TheAirline.Models.Airports.Airports.GetAllAirports().Count);
      var airline = Airlines.GetAllAirlines().First(a => a.Profile.IATACode == "DL" || a.Profile.IATACode == "AA");
      var airport = airline.Airports.FirstOrDefault() ?? TheAirline.Models.Airports.Airports.GetAllAirports().First(a => a.Profile.IATACode == "JFK");
      var sd = new StartDataObject {
        Airline = airline, Airport = airport, CEO = "Test CEO",
        Continent = new Continent("100", "All continents"),
        Difficulty = DifficultyLevels.GetDifficultyLevel("Normal"),
        Focus = default(AirlineFocus), HomeCountry = airport.Profile.Country,
        IsPaused = true, LocalCurrency = false, MajorAirports = true, NumberOfOpponents = 3, RandomOpponents = true,
        RealData = false, Region = Regions.GetRegion("100"), SameRegion = true,
        TimeZone = TimeZones.GetTimeZones().First(), UseDayTurns = true, Year = 2007 };
      GameObjectHelpers.CreateGame(sd);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      while (!GameObjectWorker.GetInstance().IsBusy() && sw.Elapsed.TotalSeconds < 10) Thread.Sleep(200);
      Console.WriteLine("worker started after " + sw.Elapsed.TotalSeconds + "s (demand built)");
      GameObjectWorker.GetInstance().Pause();
      var g = GameObject.GetInstance();
      Console.WriteLine("human=" + g.HumanAirline.Profile.Name + " start=" + g.GameTime.ToShortDateString() + " money=" + g.HumanAirline.Money);
      for (int i = 0; i < 40; i++) GameObjectHelpers.SimulateTurn();
      Console.WriteLine("after 40 days: " + g.GameTime.ToShortDateString() + " money=" + g.HumanAirline.Money);
      Console.WriteLine("SIMULATED OK");
    } catch (Exception e) { Console.WriteLine("FAILED: " + e); }
  }
}
