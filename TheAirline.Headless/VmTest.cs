using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers.Workers;
using TheAirline.Presentation;

// Drives the view models the way a UI would: menu -> new game -> airline screen -> save -> menu -> load.
static class VmTest
{
    static async Task Wait(Func<bool> done, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done()) { if (sw.Elapsed.TotalSeconds > 120) throw new Exception("timeout: " + what); await Task.Delay(100); }
    }

    public static async Task Run()
    {
        try
        {
            var shell = new ShellViewModel(a => a(), () => Console.WriteLine("exit"));
            var menu = (MainMenuViewModel)shell.Current;
            menu.NewGameCommand.Execute(null);
            var ng = (NewGameViewModel)shell.Current;
            await Wait(() => !ng.IsBusy, "new game screen loads");
            Console.WriteLine("airlines for " + ng.StartYear + ": " + ng.Airlines.Count);
            Console.WriteLine("continents: " + string.Join(", ", ng.Continents.Select(c => c.Name)));
            ng.SelectedContinent = ng.Continents.First(c => c.Name == "North America");
            Console.WriteLine("NA regions: " + string.Join(", ", ng.Regions.Select(r => r.Name)) + " -> airlines " + ng.Airlines.Count);
            ng.SelectedRegion = ng.Regions[1];
            Console.WriteLine("region " + ng.SelectedRegion.Name + " -> airlines " + ng.Airlines.Count + " e.g. " + ng.Airlines.First().Profile.Name);
            ng.SelectedRegion = ng.Regions[0];
            Console.WriteLine("all -> airlines " + ng.Airlines.Count);
            ng.SelectedAirline = ng.Airlines.First(a => a.Profile.IATACode == "DL") ;
            ng.CreateCommand.Execute(null);
            await Wait(() => shell.Current is AirlineViewModel, "game created");
            var av = (AirlineViewModel)shell.Current;
            Console.WriteLine("AIRLINE SCREEN: " + av.AirlineName + " " + av.Date + " " + av.Money + " fleet=" + av.Fleet.Count + " routes=" + av.Routes.Count + " running=" + av.IsRunning);
            // aircraft market: pick the cheapest-looking aircraft we can afford and buy it
            var mk = av.Market;
            Console.WriteLine("market: " + mk.Aircraft.Count + " aircraft");
            foreach (var item in mk.Aircraft.OrderBy(a => a.Airliner.GetPrice()).Take(200))
            {
                mk.Selected = item;
                if (mk.Homebase != null) break;
            }
            Console.WriteLine("selected " + mk.Selected?.Name + " homebase " + mk.Homebase?.Profile.IATACode + " price " + mk.Selected?.PriceText);
            mk.BuyCommand.Execute(null);
            Console.WriteLine("buy: " + mk.Status);
            av.Refresh();
            Console.WriteLine("fleet=" + av.Fleet.Count);

            // airports tab: advice from the home airport, then create a route
            var ap = av.Airports;
            await Wait(() => !ap.IsBusy, "advice");
            Console.WriteLine("airports shown " + ap.Airports.Count + ", advice " + ap.Advice.Count);
            ap.SelectedAdvice = ap.Advice.First(a => a.Advice.FleetCanFly);
            var cr = ap.Create;
            Console.WriteLine(cr.Title + " aircraft options=" + cr.Aircraft.Count + " offers=" + cr.Offers.Count + " trips=" + cr.Trips.Count + " start=" + cr.StartTime);
            foreach (var o in cr.Offers.ToList()) { Console.WriteLine("  gate offer: " + o.Text + " options=" + o.Options.Count); o.LeaseCommand.Execute(null); }
            Console.WriteLine("offers after lease=" + cr.Offers.Count + " info=" + cr.Info);
            cr.CreateCommand.Execute(null);
            Console.WriteLine("create: " + ap.Status + " | info=" + cr.Info);
            foreach (var o in cr.Offers.ToList()) { Console.WriteLine("  gate offer: " + o.Text); o.LeaseCommand.Execute(null); }
            if (ap.Create != null) { ap.Create.CreateCommand.Execute(null); Console.WriteLine("create2: " + ap.Status + " | info=" + ap.Create?.Info); }
            av.Refresh();
            Console.WriteLine(av.Summary + " flights listed=" + av.Flights.Count + " first=" + av.Flights.FirstOrDefault()?.When + " " + av.Flights.FirstOrDefault()?.Route);
            Console.WriteLine("routes=" + av.Routes.Count + " " + string.Join(",", av.Routes.Select(r => r.Name + " " + r.Balance)));
            if (!av.IsRunning) av.TogglePauseCommand.Execute(null);
            Thread.Sleep(5000);
            av.Refresh();
            Console.WriteLine("after 5s: " + av.Date + " running=" + av.IsRunning);
            av.TogglePauseCommand.Execute(null);
            av.SaveCommand.Execute(null);
            await Wait(() => av.Status != null && av.Status.StartsWith("Saved"), "save");
            Console.WriteLine(av.Status);
            av.MainMenuCommand.Execute(null);
            ((MainMenuViewModel)shell.Current).LoadGameCommand.Execute(null);
            var lg = (LoadGameViewModel)shell.Current;
            await Wait(() => !lg.IsBusy, "load list");
            Console.WriteLine("saves: " + string.Join(", ", lg.Saves.Select(s => s.Name)));
            lg.Selected = lg.Saves.First();
            lg.LoadCommand.Execute(null);
            await Wait(() => shell.Current is AirlineViewModel, "load");
            var av2 = (AirlineViewModel)shell.Current;
            Console.WriteLine("LOADED: " + av2.Date + " " + av2.Money);
            GameSession.StopClock();
            Console.WriteLine("VM TEST OK");
            Environment.Exit(0);
        }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); Environment.Exit(1); }
    }
}
