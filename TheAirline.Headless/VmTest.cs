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
            // loans
            var ln = av.Loans;
            Console.WriteLine("LOANS: " + ln.Summary);
            ln.AmountText = "10000000"; ln.Years = 5;
            Console.WriteLine("offer: " + ln.OfferText);
            double mBefore = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money;
            ln.TakeLoanCommand.Execute(null);
            Console.WriteLine("take: " + ln.Status + " money change=" + (TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money - mBefore) + " loans=" + ln.Loans.Count);
            Console.WriteLine("  " + ln.Loans[0].Text + " | " + ln.Loans[0].Detail);
            ln.AmountText = "900000000000"; ln.TakeLoanCommand.Execute(null);
            Console.WriteLine("too big: " + ln.Status);
            ln.AmountText = "abc"; ln.TakeLoanCommand.Execute(null);
            Console.WriteLine("bad: " + ln.Status);
            ln.Loans[0].PayText = "2500000"; ln.Loans[0].PayCommand.Execute(null);
            Console.WriteLine("pay: " + ln.Status + " | " + ln.Loans[0].Detail);
            for (int i = 0; i < 35; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            ln.Reload();
            Console.WriteLine("after a month: " + ln.Loans[0].Detail);
            ln.Loans[0].PayOffCommand.Execute(null);
            Console.WriteLine("payoff: " + ln.Status + " loans=" + ln.Loans.Count);
            // new aircraft order
            var na = av.NewAircraft;
            Console.WriteLine("ORDER types=" + na.Types.Count);
            na.Filter = "737"; Console.WriteLine("  filtered=" + na.Types.Count);
            na.Selected = na.Types.FirstOrDefault() ?? (na.Filter == "" ? null : null);
            if (na.Selected == null) { na.Filter = ""; na.Selected = na.Types.First(); }
            na.Amount = 2; na.DownPayment = true;
            Console.WriteLine("  " + na.Selected.Name + " | homebases=" + na.Homebases.Count + " engines=" + na.Engines.Count + " | " + na.Summary);
            var gt = TheAirline.Models.General.GameObject.GetInstance();
            double m0 = gt.HumanAirline.Money; int f0 = gt.HumanAirline.Fleet.Count;
            na.TerminateContract = true;
            na.OrderCommand.Execute(null);
            Console.WriteLine("  order: " + na.Status + " | money change=" + (gt.HumanAirline.Money - m0) + " fleet " + f0 + "->" + gt.HumanAirline.Fleet.Count + " delivered=" + gt.HumanAirline.DeliveredFleet.Count);
            av.Refresh();
            Console.WriteLine("  fleet row: " + av.Fleet.Last().Name + " " + av.Fleet.Last().Status + " canStart=" + av.Fleet.Last().CanStart);
            // airport manager
            var am = av.AirportManager;
            Console.WriteLine("MY AIRPORTS: " + string.Join(", ", am.Airports.Select(a => a.Text.Split(' ')[0])));
            Console.WriteLine("DETAIL: " + am.Title + " | " + am.GateSummary + " | contracts=" + am.Contracts.Count + " | " + string.Join(" ; ", am.Contracts.Select(c => c.Text)));
            foreach (var f in am.Facilities) Console.WriteLine("  facility " + f.Name + ": " + f.Current + " -> " + f.Next + " canBuy=" + f.CanBuy);
            Console.WriteLine("gate choices=" + am.GateChoices.Count + " price: " + am.PriceText);
            am.Gates = 1; am.LeaseCommand.Execute(null);
            Console.WriteLine("lease more: " + am.Status + " | " + am.GateSummary);
            var up = am.Facilities.FirstOrDefault(f => f.CanBuy);
            if (up != null) { up.BuyCommand.Execute(null); Console.WriteLine("facility: " + am.Status); }
            am.Search = "JFK";
            Console.WriteLine("search JFK: " + am.Airports.Count + " " + am.Airports.FirstOrDefault()?.Text);
            am.Selected = am.Airports.First();
            Console.WriteLine("JFK: " + am.GateSummary + " | " + am.PriceText + " canBuyFacility=" + am.Facilities.Any(f => f.CanBuy));
            am.LeaseCommand.Execute(null);
            Console.WriteLine("lease JFK: " + am.Status + " | " + am.GateSummary + " contracts=" + am.Contracts.Count);
            am.SelectedContract = am.Contracts.FirstOrDefault();
            am.EndContractCommand.Execute(null);
            Console.WriteLine("end: " + am.EndText);
            am.ConfirmEndCommand.Execute(null);
            Console.WriteLine("end result: " + am.Status + " contracts=" + am.Contracts.Count);
            am.Search = "";
            // terminals
            am.Search = "";
            am.Selected = am.Airports.First();
            foreach (var t in am.Terminals.Take(4)) Console.WriteLine("  terminal " + t.Text + " buy=" + t.CanBuy + " sell=" + t.CanSell);
            Console.WriteLine("terminal price: " + am.TerminalPriceText);
            am.TerminalName = "Test Terminal"; am.TerminalGates = 5;
            double moneyBefore = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money;
            am.BuildTerminalCommand.Execute(null);
            Console.WriteLine("pending: " + am.PendingText);
            am.ConfirmPendingCommand.Execute(null);
            Console.WriteLine("build: " + am.Status + " money change=" + (TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money - moneyBefore));
            var mine = am.Terminals.First(t => t.Text.StartsWith("Test Terminal"));
            Console.WriteLine("  " + mine.Text + " sell/cancel=" + mine.CanSell + " " + mine.ActionText);
            for (int i = 0; i < 125; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            am.Reload();
            Console.WriteLine("after delivery: " + am.GateSummary + " contracts=" + am.Contracts.Count + " | " + string.Join(" ; ", am.Contracts.Select(c => c.Text)));
            mine = am.Terminals.First(t => t.Text.StartsWith("Test Terminal"));
            Console.WriteLine("  " + mine.Text + " sell=" + mine.CanSell + " " + mine.ActionText);
            mine.SellCommand.Execute(null);
            Console.WriteLine("pending: " + am.PendingText);
            am.ConfirmPendingCommand.Execute(null);
            Console.WriteLine("sell: " + am.Status);
            var buy = am.Terminals.FirstOrDefault(t => t.CanBuy);
            if (buy != null) { buy.BuyCommand.Execute(null); Console.WriteLine("buy pending: " + am.PendingText); am.CancelPendingCommand.Execute(null); }
            // crew the aircraft, then start it
            var pv = av.Pilots;
            Console.WriteLine("targets: " + string.Join(" | ", pv.Targets.Select(t => t.Name)) + " ; available(any)=" + pv.Available.Count);
            pv.Target = pv.Targets.First(t => t.Airliner != null);
            Console.WriteLine("available for target=" + pv.Available.Count);
            pv.HireCrewCommand.Execute(null);
            Console.WriteLine("pilots: " + pv.Status + " hired=" + pv.Hired.Count);
            av.Refresh();
            var fi = av.Fleet.First();
            Console.WriteLine("fleet: " + fi.Name + " " + fi.Status + " " + fi.Crew + " canStart=" + fi.CanStart);
            fi.StartCommand.Execute(null);
            av.Refresh();
            Console.WriteLine("after start: " + av.Fleet.First().Status + " | " + av.Status);
            av.Refresh();
            Console.WriteLine(av.Summary + " flights listed=" + av.Flights.Count + " first=" + av.Flights.FirstOrDefault()?.When + " " + av.Flights.FirstOrDefault()?.Route);
            // route detail: select, change a fare, save, delete
            av.SelectedRoute = av.Routes.First();
            var rd = av.RouteDetail;
            Console.WriteLine("ROUTE " + rd.Code + " | " + rd.Numbers + " | " + rd.AircraftText);
            Console.WriteLine("fares: " + string.Join(", ", rd.Fares.Select(f => f.ClassName + " " + f.Price)));
            rd.Fares[0].Price = "199.5"; rd.SaveFaresCommand.Execute(null);
            Console.WriteLine("fare save: " + rd.Status);
            rd.Fares[0].Price = "abc"; rd.SaveFaresCommand.Execute(null);
            Console.WriteLine("bad fare: " + rd.Status);
            rd.DeleteCommand.Execute(null); rd.ConfirmDeleteCommand.Execute(null);
            av.Refresh();
            Console.WriteLine("after delete routes=" + av.Routes.Count + " detail=" + (av.RouteDetail == null ? "none" : "still") + " fleet status=" + av.Fleet.First().Status);
            foreach (var fr in av.Finance.Rows) Console.WriteLine("FIN " + fr.Label + ": " + string.Join(" ; ", fr.Values));
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
