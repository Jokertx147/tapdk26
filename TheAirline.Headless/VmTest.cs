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
            // renting gates: more gates go on the matching contract, a different kind gets its own, and the rent follows
            {
                var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
                var ap2 = TheAirline.Models.Airports.Airports.GetAllActiveAirports().First(x => x.Terminals.GetFreeGates(TheAirline.Models.Airports.Terminal.TerminalType.Passenger) >= 6 && !x.AirlineContracts.Any(c => c.Airline == h));
                var pax = TheAirline.Models.Airports.Terminal.TerminalType.Passenger;
                TheAirline.Helpers.AirportHelpers.RentGates(ap2, h, TheAirline.Models.Airports.AirportContract.ContractType.Full, pax, 2);
                TheAirline.Helpers.AirportHelpers.RentGates(ap2, h, TheAirline.Models.Airports.AirportContract.ContractType.LowService, pax, 1);
                TheAirline.Helpers.AirportHelpers.RentGates(ap2, h, TheAirline.Models.Airports.AirportContract.ContractType.Full, pax, 2);
                foreach (var c in ap2.AirlineContracts.Where(c => c.Airline == h)) Console.WriteLine("  RENT " + c.Type + " gates=" + c.NumberOfGates + " yearly=" + c.YearlyPayment.ToString("0"));
                Console.WriteLine("  RENT owned gates=" + ap2.Terminals.GetGates().Count(g => g.Airline == h) + " (expected 5)");
            }
            // the menu pages, alliances, airlines and options
            {
                Console.WriteLine("  PAGES: " + string.Join(" | ", av.Pages));
                for (int i = 0; i < av.Pages.Length; i++) av.Page = i;
                av.Page = 4;
                var al = av.Alliances;
                Console.WriteLine("  ALLIANCES: " + al.Alliances.Count + " first=" + al.Selected?.Text + " | members=" + al.Members.Count + " member=" + al.IsMember + " canJoin=" + al.CanJoin);
                var human = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
                al.NewName = "Test Alliance"; al.CreateCommand.Execute(null);
                Console.WriteLine("  created: " + al.Status + " | selected=" + al.Title + " member=" + al.IsMember + " invite candidates=" + al.InviteCandidates.Count);
                al.InviteCommand.Execute(null); Console.WriteLine("  invite: " + al.Status + " members=" + al.Members.Count);
                al.LeaveCommand.Execute(null); al.ConfirmCommand.Execute(null); Console.WriteLine("  leave: " + al.Status + " alliances=" + al.Alliances.Count);
                var other = al.Alliances.FirstOrDefault(x => !x.Alliance.Members.Any(m => m.Airline == human));
                if (other != null) { al.Selected = other; al.JoinCommand.Execute(null); al.ConfirmCommand.Execute(null); Console.WriteLine("  join " + other.Alliance.Name + ": " + al.Status + " member=" + al.IsMember); }
                av.Page = 6;
                var sw2 = System.Diagnostics.Stopwatch.StartNew(); var lst = av.AirlineList; lst.Reload();
                Console.WriteLine("  AIRLINES: " + lst.Count + " (" + sw2.ElapsedMilliseconds + " ms) first=" + lst.Airlines.First().Name + " " + lst.Airlines.First().Value + " | " + lst.Details);
                lst.SortBy = "Fleet"; lst.Search = "air"; Console.WriteLine("  filtered: " + lst.Count);
                if (lst.Selected != null)
                {
                    Console.WriteLine("  PROFILE " + lst.ProfileTitle + ": facts=" + lst.Facts.Count + " fleetLines=" + lst.FleetLines.Count + " routeLines=" + lst.RouteLines.Count);
                    foreach (var x in lst.Facts) Console.WriteLine("    " + x);
                    foreach (var x in lst.FleetLines.Take(3)) Console.WriteLine("    fleet " + x);
                    foreach (var x in lst.RouteLines.Take(3)) Console.WriteLine("    route " + x);
                }
                av.Page = 7;
                {
                    var nw = av.News;
                    var box = TheAirline.Models.General.GameObject.GetInstance().NewsBox;
                    box.AddNews(new TheAirline.Models.General.News(TheAirline.Models.General.News.NewsType.AirlineNews, TheAirline.Models.General.GameObject.GetInstance().GameTime, "Test subject", "Test body"));
                    nw.Poll();
                    Console.WriteLine("  NEWS: " + nw.Count + " unread=" + nw.Unread + " first=" + nw.Rows.FirstOrDefault()?.Subject);
                    nw.Selected = nw.Rows.First(r => r.Subject == "Test subject");
                    Console.WriteLine("  opened: body=" + nw.Body + " unread=" + nw.Unread + " marker='" + nw.Selected.Marker + "'");
                    nw.Filter = "Fleet"; Console.WriteLine("  filter Fleet: " + nw.Count); nw.Filter = "All";
                    nw.UnreadOnly = true; Console.WriteLine("  unread only: " + nw.Count); nw.UnreadOnly = false;
                    int before = box.GetNews().Count;
                    nw.Selected = nw.Rows.First();
                    nw.DeleteSelectedCommand.Execute(null); Console.WriteLine("  delete selected: " + before + "->" + box.GetNews().Count);
                    nw.MarkAllReadCommand.Execute(null); Console.WriteLine("  mark all: unread=" + nw.Unread);
                    nw.DeleteReadCommand.Execute(null); Console.WriteLine("  delete read: " + box.GetNews().Count);
                }
                av.Page = 9;
                var op = av.Options;
                Console.WriteLine("  OPTIONS: save name=" + op.SaveName + " saves=" + op.Saves.Count);
                op.SaveName = "vmtest-options"; op.SaveCommand.Execute(null);
                System.Threading.Thread.Sleep(4000);
                Console.WriteLine("  saved: " + op.Status + " saves=" + op.Saves.Count + " has pending=" + op.HasPending);
                op.SaveCommand.Execute(null); Console.WriteLine("  save again: pending=" + op.HasPending + " " + op.PendingText);
                op.CancelCommand.Execute(null);
                op.Selected = op.Saves.FirstOrDefault(x => x.Name == "vmtest-options"); op.DeleteCommand.Execute(null); op.ConfirmCommand.Execute(null);
                Console.WriteLine("  deleted: " + op.Status + " saves=" + op.Saves.Count);
            }
            // refit the cabin of an owned aircraft
            {
                av.Refresh();
                var row = av.Fleet.FirstOrDefault(f => f.CanEditCabin);
                Console.WriteLine("  REFIT: fleet rows=" + av.Fleet.Count + " editable=" + av.Fleet.Count(f => f.CanEditCabin));
                if (row != null)
                {
                    var human2 = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
                    row.EditCabinCommand.Execute(null);
                    var rf = av.Refit;
                    Console.WriteLine("  open: " + rf.Title + " | " + rf.Editor.Summary + " | " + rf.CostText);
                    rf.Editor.AddCommand.Execute(null);
                    var biz2 = rf.Editor.Classes.Last(); biz2.Seats = 14; biz2.Seat.Selected = biz2.Seat.Choices.Last();
                    Console.WriteLine("  edited: " + rf.Editor.Summary + " | " + rf.CostText);
                    double money0 = human2.Money;
                    rf.ApplyCommand.Execute(null);
                    var fa = human2.Fleet.First(x => x.Name == rf.Title.Split('(')[0].Replace("Cabin of ", "").Trim());
                    Console.WriteLine("  applied: " + fa.Airliner.CabinConfiguration + " money change=" + (human2.Money - money0).ToString("0") + " refit closed=" + (av.Refit == null) + " status=" + rf.Status);
                    av.Fleet.First(f => f.CanEditCabin).EditCabinCommand.Execute(null);
                    Console.WriteLine("  reopened: " + av.Refit.Editor.Layout + " (no change cost: " + av.Refit.CostText.Substring(0, 20) + ")");
                    av.Refit.CancelCommand.Execute(null);
                }
            }
            // new aircraft order
            var na = av.NewAircraft;
            Console.WriteLine("ORDER types=" + na.Types.Count);
            Console.WriteLine("  manufacturers: " + string.Join("; ", na.Manufacturers.Select(m => m.Text))); na.Manufacturer = na.Manufacturers[1]; Console.WriteLine("  first maker types=" + na.Types.Count + " " + na.Types.First().Details); na.Manufacturer = na.Manufacturers[0]; na.Filter = "737"; Console.WriteLine("  filtered=" + na.Types.Count);
            na.Selected = na.Types.FirstOrDefault() ?? (na.Filter == "" ? null : null);
            if (na.Selected == null) { na.Filter = ""; na.Selected = na.Types.First(); }
            na.Amount = 2; na.DownPayment = true;
            Console.WriteLine("  " + na.Selected.Name + " | homebases=" + na.Homebases.Count + " engines=" + na.Engines.Count + " | " + na.Summary);
            var cab = na.Cabin;
            Console.WriteLine("  CABIN: " + (cab == null ? "none" : cab.Summary + " | presets=" + string.Join("; ", cab.Presets.Select(x => x.Name))));
            if (cab != null)
            {
                string before = na.Summary;
                cab.AddCommand.Execute(null);
                Console.WriteLine("  +class: " + cab.Summary + " classes=" + cab.Classes.Count + " canAdd=" + cab.CanAdd);
                var biz = cab.Classes.Last();
                biz.Seats = 9999; Console.WriteLine("  max business: " + biz.Seats + " (max " + biz.MaxSeats + ") economy=" + cab.Classes[0].Seats + " total=" + cab.TotalSeats);
                biz.Seats = 12;
                var fac = biz.Seat.Choices; Console.WriteLine("  seat choices: " + string.Join("; ", fac.Select(x => x.Text)));
                biz.Seat.Selected = fac.Last();
                Console.WriteLine("  luxury seats: " + cab.Summary + " eco=" + cab.Classes[0].Seats + " floor used=" + cab.Classes.Sum(c => c.Regular));
                biz.Audio.Selected = biz.Audio.Choices.Last(); biz.Video.Selected = biz.Video.Choices.Last();
                Console.WriteLine("  with audio/video: " + cab.Summary);
                Console.WriteLine("  order summary: " + na.Summary);
                foreach (var pr in cab.Presets.Skip(1).Take(2)) { cab.Preset = pr; Console.WriteLine("  preset " + pr.Name + ": " + cab.Summary + " floor=" + cab.Classes.Sum(c => c.Regular) + "/" + cab.TotalFloor); }
                cab.Preset = cab.Presets[0]; cab.AddCommand.Execute(null); cab.Classes.Last().Seats = 8;
                Console.WriteLine("  final: " + cab.Layout + " | " + na.Summary);
            }
            var gt = TheAirline.Models.General.GameObject.GetInstance();
            double m0 = gt.HumanAirline.Money; int f0 = gt.HumanAirline.Fleet.Count;
            na.TerminateContract = true;
            na.OrderCommand.Execute(null);
            { var last = gt.HumanAirline.Fleet.Last(); Console.WriteLine("  ordered cabin: " + last.Airliner.CabinConfiguration + " classes=" + last.Airliner.Classes.Count + " seats=" + last.Airliner.GetTotalSeatCapacity()); }
            Console.WriteLine("  order: " + na.Status + " | money change=" + (gt.HumanAirline.Money - m0) + " fleet " + f0 + "->" + gt.HumanAirline.Fleet.Count + " delivered=" + gt.HumanAirline.DeliveredFleet.Count);
            av.Refresh();
            Console.WriteLine("  fleet row: " + av.Fleet.Last().Name + " " + av.Fleet.Last().Status + " canStart=" + av.Fleet.Last().CanStart);
            // buying gates from AI airlines
            {
                var gtGame = TheAirline.Models.General.GameObject.GetInstance();
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(gtGame.HumanAirline, gtGame.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 50000000);
                TheAirline.Models.Airports.Airport gtAirport = null; TheAirline.Models.Airports.AirportContract gtContract = null; int gtSpare = 0, scanned = 0, noSpare = 0;
                foreach (var a in TheAirline.Models.Airports.Airports.GetAllAirports())
                {
                    foreach (var c in a.AirlineContracts.ToList())
                    {
                        if (!TheAirline.Helpers.GateTrading.IsTradable(a, c)) continue;
                        scanned++;
                        int sp = TheAirline.Helpers.GateTrading.GetSpareGates(a, c);
                        if (sp == 0) noSpare++;
                        if (sp > 0 && gtContract == null) { gtAirport = a; gtContract = c; gtSpare = sp; }
                    }
                    if (gtContract != null && scanned > 300) break;
                }
                Console.WriteLine("GATE TRADE: scanned " + scanned + " AI contracts, without spare gates " + noSpare + ", found " + (gtContract == null ? "none" : gtAirport.Profile.IATACode + " " + gtContract.Airline.Profile.Name + " gates=" + gtContract.NumberOfGates + " spare=" + gtSpare));
                if (gtContract != null)
                {
                    var seller = gtContract.Airline;
                    int sellerGatesBefore = gtAirport.Terminals.GetGates().Count(g => g.Airline == seller);
                    int myBefore = gtAirport.Terminals.GetGates().Count(g => g.Airline == gtGame.HumanAirline);
                    double m1 = gtGame.HumanAirline.Money, sm1 = seller.Money;
                    string msg;
                    bool tooMany = TheAirline.Helpers.GateTrading.BuyGates(gtAirport, gtContract, gtSpare + 1, TheAirline.Models.Airports.AirportContract.ContractType.Full, out msg);
                    Console.WriteLine("  too many: " + tooMany + " | " + msg);
                    bool ok = TheAirline.Helpers.GateTrading.BuyGates(gtAirport, gtContract, 1, TheAirline.Models.Airports.AirportContract.ContractType.Full, out msg);
                    Console.WriteLine("  buy 1: " + ok + " | " + msg + " | my money change=" + (gtGame.HumanAirline.Money - m1) + " seller money change=" + (seller.Money - sm1));
                    Console.WriteLine("  seller gates " + sellerGatesBefore + "->" + gtAirport.Terminals.GetGates().Count(g => g.Airline == seller) + " | mine " + myBefore + "->" + gtAirport.Terminals.GetGates().Count(g => g.Airline == gtGame.HumanAirline) + " | my contracts here=" + gtAirport.GetAirlineContracts(gtGame.HumanAirline).Count + " | seller contract gates now=" + gtContract.NumberOfGates);
                    for (int i = 0; i < 20; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
                    Console.WriteLine("  after 20 days ok, seller flights still fit: " + TheAirline.Helpers.AirportHelpers.CanFillRoutesEntries(gtAirport, seller, gtAirport.GetAirlineContracts(seller), TheAirline.Models.General.Environment.Weather.Season.AllYear));
                }
            }
            // airport manager
            var am = av.AirportManager;
            Console.WriteLine("ALL AIRPORTS rows=" + am.Airports.Count + " first=" + am.Airports.First().Text);
            am.OnlyMine = true;
            Console.WriteLine("MY AIRPORTS: " + string.Join(", ", am.Airports.Select(a => a.Text.Split(' ')[0])));
            Console.WriteLine("DETAIL: " + am.Title + " | " + am.GateSummary + " | contracts=" + am.Contracts.Count + " | " + string.Join(" ; ", am.Contracts.Select(c => c.Text)));
            foreach (var f in am.Facilities) Console.WriteLine("  facility " + f.Name + ": " + f.Current + " -> " + f.Next + " canBuy=" + f.CanBuy);
            Console.WriteLine("gate choices=" + am.GateChoices.Count + " price: " + am.PriceText);
            am.Gates = 1; am.LeaseCommand.Execute(null);
            Console.WriteLine("lease more: " + am.Status + " | " + am.GateSummary);
            var up = am.Facilities.FirstOrDefault(f => f.CanBuy);
            if (up != null) { up.BuyCommand.Execute(null); Console.WriteLine("facility: " + am.Status); }
            am.OnlyMine = false; am.Search = "JFK";
            Console.WriteLine("search JFK: " + am.Airports.Count + " " + am.Airports.FirstOrDefault()?.Text);
            foreach (var sc in am.SortChoices) { am.SortBy = sc; Console.WriteLine("  sort " + sc + ": " + am.Airports.Count); }
            {
                am.Search = ""; am.OnlyMine = false; am.SortBy = "Airlines";
                var sw = System.Diagnostics.Stopwatch.StartNew(); int shown = 0, withOffers = 0;
                AirportItem target = null;
                foreach (var row in am.Airports.Take(25).ToList()) { am.Selected = row; shown++; if (am.GateOffers.Count > 0) { withOffers++; target = target ?? row; } }
                Console.WriteLine("  GATE OFFERS: " + withOffers + "/" + shown + " busiest airports have offers, " + (sw.ElapsedMilliseconds / Math.Max(1, shown)) + " ms per airport");
                if (target != null)
                {
                    am.Selected = target;
                    var offer = am.GateOffers.First();
                    Console.WriteLine("  at " + target.Text + ": " + offer.Text);
                    double mon = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money; int offersBefore = am.GateOffers.Count;
                    offer.BuyOneCommand.Execute(null); Console.WriteLine("  pending: " + am.PendingText);
                    am.ConfirmPendingCommand.Execute(null);
                    Console.WriteLine("  bought: " + am.Status + " | " + am.GateSummary + " | money change " + (TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money - mon).ToString("0") + " | offers " + offersBefore + "->" + am.GateOffers.Count + " | contracts " + am.Contracts.Count);
                }
            }
            am.SortBy = "Name";
            var jr = (AirportRow)am.Airports.First();
            Console.WriteLine("  row: " + jr.Name + " | " + jr.Codes + " | " + jr.Country + " | " + jr.Type + " | " + jr.Size + " | gates " + jr.Gates + " | rwy " + jr.Runway + " | airlines " + jr.Airlines + " routes " + jr.Routes + " canLease=" + jr.CanLease);
            if (jr.CanLease) { jr.LeaseCommand.Execute(null); Console.WriteLine("  quick lease: " + am.Status + " | " + am.GateSummary); am.Search = "JFK"; }
            am.Selected = am.Airports.First();
            Console.WriteLine("JFK: " + am.GateSummary + " | " + am.PriceText + " canBuyFacility=" + am.Facilities.Any(f => f.CanBuy));
            am.LeaseCommand.Execute(null);
            Console.WriteLine("lease JFK: " + am.Status + " | " + am.GateSummary + " contracts=" + am.Contracts.Count);
            am.SelectedContract = am.Contracts.FirstOrDefault();
            am.EndContractCommand.Execute(null);
            Console.WriteLine("end: " + am.EndText);
            am.ConfirmEndCommand.Execute(null);
            Console.WriteLine("end result: " + am.Status + " contracts=" + am.Contracts.Count);
            am.Search = ""; am.OnlyMine = true;
            // terminals
            am.Search = ""; am.OnlyMine = true;
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
            var gh = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            var dp = gh.Fleet.Where(f => f.Purchased == TheAirline.Models.Airliners.FleetAirliner.PurchasedType.BoughtDownPayment).ToList();
            Console.WriteLine("DOWN-PAYMENT AIRCRAFT before: " + dp.Count + " remaining each=" + (dp.Count > 0 ? dp[0].RemainingPayment : 0) + " date=" + TheAirline.Models.General.GameObject.GetInstance().GameTime.ToShortDateString() + " delivery=" + (dp.Count > 0 ? dp[0].Airliner.BuiltDate.ToShortDateString() : "-"));
            TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(gh, TheAirline.Models.General.GameObject.GetInstance().GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 300000000);
            double dpMoney = gh.Money;
            { var swT = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 125; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn(); Console.WriteLine("  TURN TIME: " + (swT.ElapsedMilliseconds / 125.0).ToString("0.0") + " ms per turn (day rounds), 125 turns"); }
            Console.WriteLine("DOWN-PAYMENT AIRCRAFT after: still pending=" + dp.Count(f => f.Purchased == TheAirline.Models.Airliners.FleetAirliner.PurchasedType.BoughtDownPayment) + " in fleet=" + dp.Count(f => gh.Fleet.Contains(f)) + " remaining=" + string.Join(",", dp.Select(f => f.RemainingPayment)) + " money change=" + (gh.Money - dpMoney) + " date=" + TheAirline.Models.General.GameObject.GetInstance().GameTime.ToShortDateString());
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
            // timetable editing
            var tt = av.Timetable;
            Console.WriteLine("TT aircraft=" + tt.Aircraft.Count + " sel=" + tt.SelectedAircraft?.Text + " routes=" + tt.Routes.Count + " rows=" + tt.Rows.Count + " | " + tt.Summary);
            if (tt.SelectedAircraft != null && tt.SelectedRoute != null)
            {
                int r0 = tt.Rows.Count;
                tt.RemoveRouteFlightsCommand.Execute(null);
                Console.WriteLine("  cleared: " + tt.Status + " rows=" + tt.Rows.Count + " | " + tt.Summary + " routes on aircraft=" + tt.SelectedAircraft.Airliner.Routes.Count);
                tt.SelectedTime = tt.Times.First(t => t.Time == new TimeSpan(7, 0, 0));
                tt.WeekdaysCommand.Execute(null);
                tt.AddRoundTripCommand.Execute(null);
                Console.WriteLine("  weekdays round trips: " + tt.Status + " rows=" + tt.Rows.Count);
                foreach (var r in tt.Rows.Take(4)) Console.WriteLine("    " + r.Day + " " + r.Time + " -> " + r.Arrives + " " + r.Flight + " " + r.Route);
                tt.AddRoundTripCommand.Execute(null);
                Console.WriteLine("  duplicate add: " + tt.Status + " rows=" + tt.Rows.Count);
                tt.DailyCommand.Execute(null); tt.SelectedTime = tt.Times.First(t => t.Time == new TimeSpan(15, 0, 0));
                tt.AddOneWayCommand.Execute(null);
                Console.WriteLine("  one way daily 15:00: " + tt.Status + " rows=" + tt.Rows.Count);
                tt.Rows.First().RemoveCommand.Execute(null);
                Console.WriteLine("  remove one: " + tt.Status + " rows=" + tt.Rows.Count);
                av.Refresh();
                Console.WriteLine("  all flights list=" + av.Flights.Count + " fleet row routes=" + av.Fleet.First().Routes);
            }
            rd.DeleteCommand.Execute(null); rd.ConfirmDeleteCommand.Execute(null);
            av.Refresh();
            Console.WriteLine("after delete routes=" + av.Routes.Count + " detail=" + (av.RouteDetail == null ? "none" : "still") + " fleet status=" + av.Fleet.First().Status);
            {
                // fleet overview
                var ov = av.Overview;
                Console.WriteLine("OVERVIEW: rows=" + ov.Rows.Count + " | " + ov.Title);
                ov.SortBy = "Value"; ov.Search = "zzzz"; Console.WriteLine("  search none: " + ov.Rows.Count); ov.Search = "";
                ov.Selected = ov.Rows.First();
                Console.WriteLine("  row: " + ov.Selected.Name + " " + ov.Selected.Type + " " + ov.Selected.Cabin + " " + ov.Selected.Ownership + " " + ov.Selected.Status + " value=" + ov.Selected.Value);
                foreach (var d in ov.Details) Console.WriteLine("    " + d);
                foreach (var l in ov.RouteLines) Console.WriteLine("    route " + l.Name + " " + l.Balance + " " + l.Filling);
                Console.WriteLine("  canStart=" + ov.CanStart + " canStop=" + ov.CanStop + " canCabin=" + ov.CanCabin + " sell=" + ov.SellText);
                if (ov.CanStart) { ov.StartCommand.Execute(null); Console.WriteLine("  start: " + ov.Status + " canStop=" + ov.CanStop); }
                if (ov.CanStop) { ov.StopCommand.Execute(null); Console.WriteLine("  stop: " + ov.Status + " canStart=" + ov.CanStart); }
                ov.CabinCommand.Execute(null);
                Console.WriteLine("  refit opened=" + av.HasRefit); av.Refit?.CancelCommand.Execute(null);
                var owned = ov.Selected.Aircraft; var hum = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
                double mo = hum.Money; int fl = hum.Fleet.Count;
                ov.SellCommand.Execute(null);
                Console.WriteLine("  sell asks: " + ov.PendingText);
                ov.CancelCommand.Execute(null); Console.WriteLine("  cancel keeps fleet: " + (hum.Fleet.Count == fl));
                ov.SellCommand.Execute(null); ov.ConfirmCommand.Execute(null);
                Console.WriteLine("  sold: " + ov.Status + " fleet " + fl + "->" + hum.Fleet.Count + " money change=" + (hum.Money - mo) + " rows=" + ov.Rows.Count + " fleetRows=" + av.Fleet.Count + " owner cleared=" + (owned.Airliner.Airline == null));
            }
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
            {
                var sw = new System.Diagnostics.Stopwatch(); double worst = 0; string worstWhere = ""; int cnt = 0;
                foreach (var a in TheAirline.Models.Airports.Airports.GetAllAirports())
                    foreach (var c in a.AirlineContracts.ToList())
                    {
                        if (!TheAirline.Helpers.GateTrading.IsTradable(a, c)) continue;
                        sw.Restart(); TheAirline.Helpers.GateTrading.GetSpareGates(a, c); sw.Stop(); cnt++;
                        if (sw.Elapsed.TotalMilliseconds > worst) { worst = sw.Elapsed.TotalMilliseconds; worstWhere = a.Profile.IATACode + " " + c.Airline.Profile.IATACode + " gates=" + c.NumberOfGates + " routes=" + TheAirline.Helpers.AirportHelpers.GetAirportRoutes(a, c.Airline).Count; }
                    }
                int mismatches = 0;
                foreach (var a in TheAirline.Models.Airports.Airports.GetAllAirports())
                    foreach (var c in a.AirlineContracts.ToList().Take(3))
                    {
                        if (!TheAirline.Helpers.GateTrading.IsTradable(a, c)) continue;
                        if (cnt > 140) break;
                        var seller0 = c.Airline;
                        int total0 = a.GetAirlineContracts(seller0).Where(x => x.TerminalType == TheAirline.Models.Airports.Terminal.TerminalType.Passenger).Sum(x => x.NumberOfGates);
                        int owned0 = a.Terminals.GetGates().Count(g => g.Airline == seller0);
                        bool hasRoutes0 = TheAirline.Helpers.AirportHelpers.GetAirportRoutes(a, seller0).Count > 0;
                        int legacy = 0;
                        for (int n = 1; n <= Math.Min(c.NumberOfGates, owned0); n++)
                        {
                            int left = total0 - n;
                            if (left == 0 && hasRoutes0) break;
                            var rem = new System.Collections.Generic.List<TheAirline.Models.Airports.AirportContract> { new TheAirline.Models.Airports.AirportContract(seller0, a, TheAirline.Models.Airports.AirportContract.ContractType.Full, TheAirline.Models.Airports.Terminal.TerminalType.Passenger, TheAirline.Models.General.GameObject.GetInstance().GameTime, left, 1, 0, false) };
                            if (left > 0 && !TheAirline.Helpers.AirportHelpers.CanFillRoutesEntries(a, seller0, rem, TheAirline.Models.General.Environment.Weather.Season.AllYear)) break;
                            legacy = n;
                        }
                        int fast = TheAirline.Helpers.GateTrading.GetSpareGates(a, c);
                        if (legacy != fast) { mismatches++; Console.WriteLine("  MISMATCH " + a.Profile.IATACode + " " + seller0.Profile.IATACode + " legacy=" + legacy + " new=" + fast + " gates=" + c.NumberOfGates); }
                        cnt++;
                    }
                Console.WriteLine("SPARE EQUIVALENCE: checked " + (cnt - 0) + " mismatches=" + mismatches);
                Console.WriteLine("SPARE TIMING: contracts=" + cnt + " worst ms=" + worst.ToString("0") + " at " + worstWhere);
            }
            // ---- cargo routes ----
            {
                Console.WriteLine("CARGO TEST");
                var cav = av2;
                var cam = cav.AirportManager;
                cam.OnlyMine = false;
                foreach (var code in new[] { "DFW", "RSW" })
                {
                    cam.Search = code;
                    cam.Selected = cam.Airports.First(a => a.Text.Contains(code));
                    Console.WriteLine("  " + code + " before: " + cam.GateSummary);
                    cam.TerminalCargo = true; cam.TerminalName = "Cargo " + code; cam.TerminalGates = 3;
                    cam.BuildTerminalCommand.Execute(null); cam.ConfirmPendingCommand.Execute(null);
                    Console.WriteLine("  build cargo terminal: " + cam.Status);
                    cam.TerminalCargo = false;
                }
                for (int d = 0; d < 200; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
                Console.WriteLine("  date now " + TheAirline.Models.General.GameObject.GetInstance().GameTime.ToShortDateString());
                foreach (var code in new[] { "DFW", "RSW" })
                {
                    cam.Search = code;
                    cam.Selected = cam.Airports.First(a => a.Text.Contains(code));
                    cam.LeaseCargo = true;
                    Console.WriteLine("  header: " + cam.LeaseHeader + " gates choices=" + cam.GateChoices.Count);
                    cam.Gates = 1; cam.LeaseCommand.Execute(null);
                    Console.WriteLine("  lease cargo " + code + ": " + cam.Status + " | " + cam.GateSummary);
                    foreach (var f in cam.Facilities.Where(x => x.Name.Contains("argo"))) Console.WriteLine("    facility " + f.Name + ": " + f.Current + " -> " + f.Next);
                    cam.LeaseCargo = false;
                }
                foreach (var code in new[] { "DFW", "RSW" })
                {
                    cam.Search = code;
                    cam.Selected = cam.Airports.First(a => a.Text.Contains(code));
                    var cf = cam.Facilities.First(x => x.Name.Contains("argo"));
                    Console.WriteLine("  " + code + " cargo facility canBuy=" + cf.CanBuy + " building=" + cf.Building);
                    cf.BuyCommand.Execute(null);
                    Console.WriteLine("  buy facility: " + cam.Status);
                }
                for (int d = 0; d < 60; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
                // buy a cargo aircraft
                var cna = cav.NewAircraft;
                cna.Reload();
                var cargoType = cna.Types.FirstOrDefault(t => t.Type is TheAirline.Models.Airliners.AirlinerCargoType && t.Type.Range >= 1700);
                Console.WriteLine("  cargo types in new-aircraft list: " + cna.Types.Count(t => t.Type is TheAirline.Models.Airliners.AirlinerCargoType) + " pick=" + cargoType?.Name + " | " + cargoType?.Details);
                cna.Selected = cargoType;
                cna.Amount = 1; cna.DownPayment = false;
                cna.OrderCommand.Execute(null);
                Console.WriteLine("  order: " + cna.Status);
                foreach (var fa in TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Fleet.Where(x => x.Airliner.Type is TheAirline.Models.Airliners.AirlinerCargoType))
                    fa.Airliner.BuiltDate = TheAirline.Models.General.GameObject.GetInstance().GameTime.AddDays(-2);
                cav.Refresh();

                var cap = cav.Airports;
                cap.CargoMode = true;
                cap.Search = "DFW";
                cap.Selected = cap.Airports.First(a => a.Text.Contains("DFW"));
                await Wait(() => !cap.IsBusy, "cargo advice");
                Console.WriteLine("  " + cap.AdviceTitle + " rows=" + cap.Advice.Count);
                foreach (var ad in cap.Advice.Take(3)) Console.WriteLine("    " + ad.Name + " | " + ad.Numbers + " | " + ad.Notes);
                var rsw = cap.Advice.FirstOrDefault(a => a.Name.Contains("RSW"));
                Console.WriteLine("  RSW advice: " + (rsw == null ? "none" : rsw.Name + " | " + rsw.Numbers + " | " + rsw.Notes));
                if (rsw != null) cap.SelectedAdvice = rsw;
                var ccr = rsw != null ? cap.Create : new CreateRouteViewModel(cap.Selected.Airport, cam.Airports.First(a => a.Text.Contains("RSW")).Airport, m => Console.WriteLine("  done: " + m), true);
                Console.WriteLine("  " + ccr.Title + " cargo=" + ccr.IsCargo + " price=" + ccr.CargoPrice + " aircraft=" + ccr.Aircraft.Count + " offers=" + ccr.Offers.Count + " trips=" + ccr.Trips.Count + " info=" + ccr.Info);
                ccr.CargoPrice = "abc"; ccr.CreateCommand.Execute(null);
                Console.WriteLine("  bad price: " + ccr.Info);
                ccr.CargoPrice = "7.5";
                ccr.CreateCommand.Execute(null);
                if (rsw == null) Console.WriteLine("  (direct)");
                Console.WriteLine("  create: " + cap.Status + " | " + ccr.Info);
                var croute = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Routes.FirstOrDefault(r => r.IsCargoRoute);
                Console.WriteLine("  cargo route exists=" + (croute != null) + (croute == null ? "" : " " + croute.Destination1.Profile.IATACode + "-" + croute.Destination2.Profile.IATACode + " price=" + ((TheAirline.Models.Routes.CargoRoute) croute).PricePerUnit + " entries=" + croute.TimeTable.Entries.Count));
                cav.Refresh();
                Console.WriteLine("  routes list: " + string.Join(" ; ", cav.Routes.Select(r => r.Name)));
                var crow = cav.Routes.FirstOrDefault(r => r.Name.Contains("cargo"));
                if (crow != null)
                {
                    cav.SelectedRoute = crow;
                    var det = cav.RouteDetail;
                    Console.WriteLine("  detail IsCargo=" + det.IsCargo + " price=" + det.CargoPrice);
                    det.CargoPrice = "9.25"; det.SaveCargoPriceCommand.Execute(null);
                    Console.WriteLine("  saved price: " + ((TheAirline.Models.Routes.CargoRoute) croute).PricePerUnit);
                }
                var ctt = cav.Timetable;
                Console.WriteLine("  timetable aircraft: " + string.Join(", ", ctt.Aircraft.Select(x => x.ToString())));
            }
            {
                Console.WriteLine("ASSIGN TEST");
                var pap = av2.Airports;
                pap.CargoMode = false;
                pap.Search = "ATL";
                pap.Selected = pap.Airports.First(a => a.Text.Contains("ATL"));
                await Wait(() => !pap.IsBusy, "passenger advice");
                var free = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Fleet.Where(f => !f.HasRoute).Select(f => f.Name).ToList();
                Console.WriteLine("  free aircraft: " + string.Join(", ", free));
                var pick = pap.Advice.FirstOrDefault(a => a.Advice.FleetCanFly) ?? pap.Advice.First();
                pap.SelectedAdvice = pick;
                var pcr = pap.Create;
                Console.WriteLine("  " + pcr.Title + " options: " + string.Join(" | ", pcr.Aircraft.Select(x => x.Name)));
                pcr.SelectedAircraft = pcr.Aircraft[0];
                pcr.CreateCommand.Execute(null);
                for (int k = 0; k < 3 && pcr.Offers.Count > 0; k++) { Console.WriteLine("  leasing: " + pcr.Offers[0].Text); pcr.Offers[0].LeaseCommand.Execute(null); pcr.CreateCommand.Execute(null); }
                Console.WriteLine("  created without aircraft: " + pap.Status + " | " + pcr.Info);
                av2.Refresh();
                var prow = av2.Routes.FirstOrDefault(r => !r.Route.HasAirliner && !r.Route.IsCargoRoute);
                Console.WriteLine("  route without aircraft: " + prow?.Name);
                if (prow != null)
                {
                    av2.SelectedRoute = prow;
                    var pd = av2.RouteDetail;
                    Console.WriteLine("  canAssign=" + pd.CanAssign + " button=" + pd.ShowAssignButton + " text=" + pd.AircraftText);
                    pd.AssignCommand.Execute(null);
                    var asg = pd.Assign;
                    Console.WriteLine("  form: " + asg.Title + " | newRoute=" + asg.IsNewRoute + " label=" + asg.CreateLabel + " aircraft=" + asg.Aircraft.Count + " trips=" + asg.Trips.Count + " info=" + asg.Info);
                    asg.CreateCommand.Execute(null);
                    Console.WriteLine("  assign result: status=" + pd.Status + " | info=" + asg.Info + " | hasAssign=" + pd.HasAssign);
                    Console.WriteLine("  after: canAssign=" + pd.CanAssign + " " + pd.AircraftText);
                    Console.WriteLine("  canMove=" + pd.CanMove + " moveButton=" + pd.ShowMoveButton);
                    {
                        var nb = av2.NewAircraft; nb.Reload();
                        nb.Selected = nb.Types.First(t => t.Name.Contains("737-600"));
                        nb.Amount = 1; nb.DownPayment = false; nb.OrderCommand.Execute(null);
                        Console.WriteLine("  bought 737 for the move: " + nb.Status);
                        foreach (var fa in TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Fleet) fa.Airliner.BuiltDate = TheAirline.Models.General.GameObject.GetInstance().GameTime.AddDays(-2);
                        av2.Refresh();
                    }
                    pd.MoveCommand.Execute(null);
                    var mv = pd.Assign;
                    Console.WriteLine("  move form: " + mv?.Title + " | " + mv?.CreateLabel + " | options=" + string.Join(", ", mv?.Aircraft.Select(x => x.Name) ?? new string[0]) + " | info=" + mv?.Info);
                    if (mv != null && mv.Aircraft.Count > 0)
                    {
                        mv.CreateCommand.Execute(null);
                        Console.WriteLine("  move result: " + pd.Status + " | info=" + mv.Info + " | now " + pd.AircraftText);
                        Console.WriteLine("  fleet routes: " + string.Join(", ", TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Fleet.Select(x => x.Name + "=" + x.Routes.Count + "/" + x.Status)));
                    }
                }
            }
            {
                Console.WriteLine("SCHEDULER TEST");
                var sf = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Fleet.First(x => x.Name == "N005DL");
                var fi2 = av2.Fleet.First(x => x.Name == "N005DL");
                Console.WriteLine("  fleet row can schedule=" + fi2.CanSchedule);
                fi2.ScheduleCommand.Execute(null);
                var tv = av2.Timetable;
                Console.WriteLine("  page=" + av2.Pages[av2.Page] + " tab=" + av2.FlightsTab + " aircraft=" + tv.SelectedAircraft?.Text);
                Console.WriteLine("  routes in dropdown: " + string.Join(" | ", tv.Routes.Select(r => r.Text)));
                Console.WriteLine("  blocks=" + tv.Blocks.Count + " preview=" + tv.Preview.Count + " days checked=" + tv.Days.Count(d => d.IsChecked));
                foreach (var bk in tv.Blocks.Take(3)) Console.WriteLine("    block day " + bk.Day + " " + bk.Start + "-" + bk.End + " " + bk.Label);
                tv.SelectedBlock = tv.Blocks.FirstOrDefault();
                Console.WriteLine("  selected: " + tv.SelectedBlockText);
                tv.WeekendCommand.Execute(null);
                Console.WriteLine("  weekend preview blocks=" + tv.Preview.Count);
                tv.SelectedTime = tv.Times.First(t => t.Time == new TimeSpan(21, 0, 0));
                Console.WriteLine("  21:00 preview blocks=" + tv.Preview.Count + " (night flights wrap past midnight)");
                int before = tv.Blocks.Count;
                tv.AddRoundTripCommand.Execute(null);
                Console.WriteLine("  added: " + tv.Status + " | blocks " + before + " -> " + tv.Blocks.Count);
                tv.SelectedBlock = tv.Blocks.Last();
                tv.RemoveSelectedCommand.Execute(null);
                Console.WriteLine("  removed one: " + tv.Status + " | blocks now " + tv.Blocks.Count);
                var ov = av2.Overview; ov.Selected = ov.Rows.First(r => r.Name == "N005DL");
                Console.WriteLine("  overview canSchedule=" + ov.CanSchedule);
                av2.Page = 0; ov.ScheduleCommand.Execute(null);
                Console.WriteLine("  from overview: page=" + av2.Pages[av2.Page] + " tab=" + av2.FlightsTab);
            }
            {
                Console.WriteLine("ANY DESTINATION TEST");
                var dap = av2.Airports;
                dap.CargoMode = false;
                dap.Search = "ATL";
                dap.Selected = dap.Airports.First(a => a.Text.StartsWith("ATL"));
                dap.DestinationSearch = "HNL";
                Console.WriteLine("  search HNL: " + dap.Destinations.Count + " first=" + dap.Destinations.FirstOrDefault()?.Name + " | " + dap.Destinations.FirstOrDefault()?.Details + " searching=" + dap.IsSearchingDestinations + " advice visible=" + dap.ShowAdvice);
                dap.SelectedDestination = dap.Destinations.First();
                Console.WriteLine("  create form: " + dap.Create?.Title + " aircraft=" + dap.Create?.Aircraft.Count + " offers=" + dap.Create?.Offers.Count + " info=" + dap.Create?.Info);
                dap.DestinationSearch = "japan";
                Console.WriteLine("  search japan: " + dap.Destinations.Count + " nearest=" + dap.Destinations.FirstOrDefault()?.Name + " " + dap.Destinations.FirstOrDefault()?.Details);
                dap.DestinationSearch = "";
                Console.WriteLine("  cleared: searching=" + dap.IsSearchingDestinations + " advice visible=" + dap.ShowAdvice);
            }
            {
                Console.WriteLine("HUB TEST");
                var hm = av2.AirportManager;
                hm.OnlyMine = false; hm.Search = "ATL";
                hm.Selected = hm.Airports.First(a => a.Text.StartsWith("ATL"));
                Console.WriteLine("  " + hm.HubText + " existing=" + string.Join(", ", hm.Hubs.Select(h => h.Airline + " " + h.Type)));
                foreach (var o in hm.HubOffers) Console.WriteLine("  offer " + o.Name + " " + o.Price + " | " + o.Effect + " | can=" + o.CanBuild + " reason=" + o.Reason);
                var sc = hm.Facilities.FirstOrDefault(f2 => f2.Name == "Service");
                if (sc != null && sc.CanBuy && !hm.HubOffers.Any(o => o.CanBuild))
                {
                    sc.BuyCommand.Execute(null); Console.WriteLine("  service center: " + hm.Status);
                    for (int d = 0; d < 40; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
                    hm.Selected = hm.Airports.First(a => a.Text.StartsWith("ATL"));
                    foreach (var o in hm.HubOffers) Console.WriteLine("  offer after service center " + o.Name + " can=" + o.CanBuild + " reason=" + o.Reason);
                }
                var ok = hm.HubOffers.FirstOrDefault(o => o.CanBuild);
                if (ok != null)
                {
                    double hm0 = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money;
                    ok.BuyCommand.Execute(null); Console.WriteLine("  pending: " + hm.PendingText);
                    hm.ConfirmPendingCommand.Execute(null);
                    Console.WriteLine("  created: " + hm.Status + " | " + hm.HubText + " | money " + (TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Money - hm0).ToString("0") + " | hubs: " + string.Join(", ", hm.Hubs.Select(h => h.Airline + " " + h.Type)));
                    Console.WriteLine("  offers now: " + string.Join(" / ", hm.HubOffers.Select(o => o.Name + "=" + o.CanBuild + ":" + o.Reason)));
                    av2.Refresh();
                    Console.WriteLine("  map hubs: " + string.Join(",", av2.Map.Places.Where(p => p.IsHub).Select(p => p.Label)));
                    var hmine = hm.Hubs.First(h => h.IsMine);
                    hmine.RemoveCommand.Execute(null); Console.WriteLine("  pending: " + hm.PendingText);
                    hm.ConfirmPendingCommand.Execute(null);
                    Console.WriteLine("  closed: " + hm.Status + " | " + hm.HubText);
                }
            }
            {
                Console.WriteLine("MAP TEST");
                var map = av2.Map;
                map.Reload();
                Console.WriteLine("  " + map.Caption + " | places " + map.Places.Count + " lines " + map.Lines.Count + " (cargo " + map.Lines.Count(l => l.IsCargo) + ")");
                foreach (var a in map.Aircraft) Console.WriteLine("  aircraft " + a.Name + " u=" + a.U.ToString("0.000") + " v=" + a.V.ToString("0.000") + " flying=" + a.InFlight + " hdg=" + a.Heading.ToString("0") + " | " + a.Info);
                foreach (var pl in map.Places.Take(5)) Console.WriteLine("  place " + pl.Label + " u=" + pl.U.ToString("0.000") + " v=" + pl.V.ToString("0.000") + " hub=" + pl.IsHub);
                Console.WriteLine("  bad pieces: " + map.Lines.Sum(l => l.Pieces.Count(p => p.Any(q => q[0] < 0 || q[0] > 1 || q[1] < 0 || q[1] > 1))));
                if (map.Aircraft.Count > 0) { map.Selected = map.Aircraft[0]; Console.WriteLine("  selected: " + map.SelectedText); }
            }
            {
                Console.WriteLine("MAINTENANCE AND INSURANCE TEST");
                var gm = TheAirline.Models.General.GameObject.GetInstance();
                var hum = gm.HumanAirline;
                var mvm = new MaintenanceViewModel(() => { });
                Console.WriteLine("  " + mvm.Summary);
                var worst = mvm.Items.FirstOrDefault();
                if (worst == null) { Console.WriteLine("  FAILED no aircraft"); Environment.Exit(1); }
                worst.Aircraft.Airliner.Condition = 60;
                mvm.Reload();
                worst = mvm.Items.First(i => i.Aircraft.Airliner.Condition <= 60);
                var ac = worst.Aircraft;
                foreach (var c in worst.Checks) Console.WriteLine("  " + c.Label + " last " + c.Last + " " + c.Due + " | " + c.Effect);
                double m0x = gm.HumanMoney; double c0 = ac.Airliner.Condition;
                worst.Checks[0].Command.Execute(null);
                Console.WriteLine("  pending: " + mvm.PendingText);
                mvm.ConfirmCommand.Execute(null);
                Console.WriteLine("  A check: " + mvm.Status + " | money " + (gm.HumanMoney - m0x).ToString("0") + " condition " + c0 + " -> " + ac.Airliner.Condition);
                if (!(ac.Airliner.Condition > c0) || !(gm.HumanMoney < m0x)) { Console.WriteLine("  FAILED A check"); Environment.Exit(1); }
                string msg;
                ac.GroundedToDate = DateTime.MinValue;
                bool okC = TheAirline.Helpers.MaintenanceService.PerformCheck(ac, TheAirline.Helpers.MaintenanceCheck.C, out msg);
                Console.WriteLine("  C check: " + msg + " grounded=" + TheAirline.Helpers.MaintenanceService.IsGrounded(ac));
                bool again = TheAirline.Helpers.MaintenanceService.PerformCheck(ac, TheAirline.Helpers.MaintenanceCheck.A, out msg);
                Console.WriteLine("  while grounded: " + again + " " + msg);
                if (!okC || again || !TheAirline.Helpers.MaintenanceService.IsGrounded(ac)) { Console.WriteLine("  FAILED C check"); Environment.Exit(1); }
                worst.AText = "120"; worst.BText = "300"; worst.CText = "12"; worst.DText = "60";
                worst.ApplyIntervalsCommand.Execute(null);
                Console.WriteLine("  intervals: " + ac.AMaintenanceInterval + "/" + ac.BMaintenanceInterval + "/" + ac.CMaintenanceInterval + "/" + ac.DMaintenanceInterval);

                // wear and breakdowns, uninsured
                var flying = hum.Fleet.Where(f => f.Routes.Count > 0 && f.Airliner.BuiltDate <= gm.GameTime).ToList();
                Console.WriteLine("  flying aircraft: " + flying.Count);
                var rndT = new Random(7);
                int breakdowns = 0; string firstText = null;
                if (flying.Count > 0)
                {
                    var h0 = flying[0];
                    double cb = h0.Airliner.Condition = 90;
                    TheAirline.Helpers.MaintenanceService.ProcessMonth(hum, rndT);
                    Console.WriteLine("  wear in a month: " + (cb - h0.Airliner.Condition).ToString("0.00"));
                    for (int i = 0; i < 400 && breakdowns == 0; i++)
                    {
                        h0.Airliner.Condition = 30; h0.GroundedToDate = DateTime.MinValue;
                        var ms = TheAirline.Helpers.MaintenanceService.ProcessMonth(hum, rndT);
                        if (ms.Count > 0) { breakdowns += ms.Count; firstText = ms[0]; }
                    }
                    Console.WriteLine("  uninsured breakdown: " + firstText);
                    if (breakdowns == 0 || !firstText.Contains("not covered")) { Console.WriteLine("  FAILED breakdown"); Environment.Exit(1); }
                }

                // insurance
                var ivm = new InsuranceViewModel(() => { });
                Console.WriteLine("  " + ivm.Summary);
                Console.WriteLine("  quote: " + ivm.QuoteText);
                ivm.Type = ivm.Types[3]; ivm.Scope = ivm.Scopes[3]; ivm.PaymentTerm = ivm.Terms[0]; ivm.Years = 2; ivm.AmountText = "40000000";
                double mb = gm.HumanMoney;
                ivm.BuyCommand.Execute(null);
                Console.WriteLine("  pending: " + ivm.PendingText);
                ivm.ConfirmCommand.Execute(null);
                Console.WriteLine("  buy: " + ivm.Status + " policies=" + ivm.Policies.Count + " money " + (gm.HumanMoney - mb).ToString("0"));
                if (ivm.Policies.Count != 1 || !(gm.HumanMoney < mb)) { Console.WriteLine("  FAILED buy"); Environment.Exit(1); }
                Console.WriteLine("  " + ivm.Policies[0].Text + "\n  " + ivm.Policies[0].Detail);
                if (flying.Count > 0)
                {
                    var g0 = flying[0]; int paid = 0; string t2 = null;
                    for (int i = 0; i < 400 && paid == 0; i++)
                    {
                        g0.Airliner.Condition = 30; g0.GroundedToDate = DateTime.MinValue;
                        var ms = TheAirline.Helpers.MaintenanceService.ProcessMonth(hum, rndT);
                        if (ms.Count > 0) { paid++; t2 = ms[0]; }
                    }
                    Console.WriteLine("  insured breakdown: " + t2);
                }
                double mp = gm.HumanMoney; var pol = hum.InsurancePolicies[0]; double rem0 = pol.RemainingPayments;
                for (int i = 0; i < 70; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
                Console.WriteLine("  after 70 days: remaining payments " + rem0 + " -> " + pol.RemainingPayments + " next " + pol.NextPaymentDue.ToShortDateString());
                if (!(pol.RemainingPayments < rem0)) { Console.WriteLine("  FAILED monthly payment"); Environment.Exit(1); }
                ivm.Reload();
                ivm.Policies[0].CancelCommand.Execute(null);
                Console.WriteLine("  pending: " + ivm.PendingText);
                ivm.ConfirmCommand.Execute(null);
                Console.WriteLine("  cancel: " + ivm.Status + " policies=" + hum.InsurancePolicies.Count);
                if (hum.InsurancePolicies.Count != 0) { Console.WriteLine("  FAILED cancel"); Environment.Exit(1); }
            }
            {
                Console.WriteLine("AUTOSAVE TEST");
                GameSession.AutoSaveOnExit = true;
                Console.WriteLine("  on=" + GameSession.AutoSaveOnExit + " inGame=" + GameSession.InGame + " name=" + GameSession.AutoSaveName());
                string asv = GameSession.AutoSaveIfNeeded();
                Console.WriteLine("  saved as: " + asv);
                if (asv == null) { Console.WriteLine("  FAILED autosave"); Environment.Exit(1); }
                GameSession.AutoSaveOnExit = false;
                Console.WriteLine("  switched off -> " + (GameSession.AutoSaveIfNeeded() ?? "no save"));
                GameSession.AutoSaveOnExit = true;
            }
            GameSession.StopClock();
            Console.WriteLine("VM TEST OK");
            Environment.Exit(0);
        }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); Environment.Exit(1); }
    }
}
