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
    /// <summary>"vm scenario": every scenario from the main menu: it loads, starts, plays a month; then a lost one ends with a message.</summary>
    static async Task ScenarioTest(ShellViewModel shell)
    {
        ((MainMenuViewModel)shell.Current).ScenariosCommand.Execute(null);
        var sv = (ScenariosViewModel)shell.Current;
        await Wait(() => !sv.IsBusy, "scenarios load");
        Console.WriteLine("SCENARIOS: " + sv.Items.Count + (sv.Status != null ? " | " + sv.Status : ""));
        var names = sv.Items.Select(i => i.Name).ToList();
        // TAP_SCEN picks one scenario (by the start of its name) instead of all
        string only = Environment.GetEnvironmentVariable("TAP_SCEN");
        if (!string.IsNullOrEmpty(only)) names = names.Where(n => only.Split(',').Any(o => n.StartsWith(o))).ToList();
        foreach (var it in sv.Items) Console.WriteLine("  " + it.Name + " | " + it.Summary + (it.Problem != null ? " | PROBLEM: " + it.Problem : ""));
        if (sv.Items.Count == 0 || sv.Items.Any(i => i.Problem != null)) { Console.WriteLine("  FAILED scenarios unplayable"); Environment.Exit(1); }
        sv.Selected = sv.Items.First();
        Console.WriteLine("  details: " + sv.Details.Replace(Environment.NewLine, " / "));
        foreach (var g in sv.Goals) Console.WriteLine("    goal: " + g.Goal + " | " + g.When);

        foreach (var name in names)
        {
            // back to the menu reloads the data, as in the game
            TheAirline.Infrastructure.GameDataLoader.Invalidate();
            shell.ShowScenarios();
            sv = (ScenariosViewModel)shell.Current;
            await Wait(() => !sv.IsBusy, "scenarios load");
            sv.Selected = sv.Items.First(i => i.Name == name);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sv.StartCommand.Execute(null);
            await Wait(() => shell.Current is AirlineViewModel || (sv.Status != null && sv.Status.Contains("failed")), "scenario " + name + " starts");
            if (!(shell.Current is AirlineViewModel)) { Console.WriteLine("  FAILED start " + name + ": " + sv.Status); Environment.Exit(1); }
            var av = (AirlineViewModel)shell.Current;
            var g = TheAirline.Models.General.GameObject.GetInstance();
            var me = g.HumanAirline;
            Console.WriteLine("== " + name + " started in " + sw.ElapsedMilliseconds + " ms: " + av.AirlineName + " " + g.GameTime.ToShortDateString() + " cash " + me.Money.ToString("N0") + " loans " + me.Loans.Sum(l => l.PaymentLeft).ToString("N0") + " fleet " + me.Fleet.Count + " routes " + me.Routes.Count + " airports " + me.Airports.Count + " | airlines " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Count + " | paused " + TheAirline.Helpers.Workers.GameObjectWorker.GetInstance().IsPaused);
            if (g.Scenario == null || me != g.Scenario.Scenario.Airline || me.Money != g.Scenario.Scenario.StartCash) { Console.WriteLine("  FAILED scenario setup"); Environment.Exit(1); }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Console.WriteLine("  memory after start: " + (GC.GetTotalMemory(true) / 1048576) + " MB");
            if (g.Scenario.Scenario.StartDebt > 0 && me.Loans.Sum(l => l.PaymentLeft) < g.Scenario.Scenario.StartDebt) { Console.WriteLine("  FAILED start debt"); Environment.Exit(1); }
            for (int d = 0; d < 35; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            av.Refresh();
            Console.WriteLine("  after a month: " + g.GameTime.ToShortDateString() + " | " + av.Scenario.YearsText + (av.Scenario.HasResult ? " | " + av.Scenario.ResultText : ""));
            foreach (var row in av.Scenario.Goals) Console.WriteLine("    " + row.Goal + " | " + row.When + " | " + row.Status + " " + row.Now);
            if (!av.Scenario.HasScenario || av.Scenario.Goals.Count == 0) { Console.WriteLine("  FAILED scenario tab"); Environment.Exit(1); }
            if (g.Scenario.IsEnded) Console.WriteLine("  (ended early: " + av.Scenario.ResultText + ")");
            // a goal met at the start must be met after the first month (debt: what is owed, not the interest)
            var goals = TheAirline.Helpers.ScenarioService.Goals(g.Scenario);
            var debtGoal = goals.FirstOrDefault(x => x.Failure.Type == TheAirline.Models.General.Scenarios.ScenarioFailure.FailureType.Debt);
            if (debtGoal != null && debtGoal.Ok != true) { Console.WriteLine("  FAILED debt goal at start: " + debtGoal.Now); Environment.Exit(1); }
        }

        // a lost scenario: cash below zero for the allowed months
        {
            var av = (AirlineViewModel)shell.Current;
            var g = TheAirline.Models.General.GameObject.GetInstance();
            var so = g.Scenario;
            var cash = so.Scenario.Failures.FirstOrDefault(f => f.Type == TheAirline.Models.General.Scenarios.ScenarioFailure.FailureType.Cash);
            if (cash != null && !so.IsEnded)
            {
                g.HumanAirline.Money = -50000000;
                var t = g.GameTime;
                for (int m = 1; m <= cash.MonthsOfFailure && !so.IsEnded; m++)
                {
                    g.GameTime = new DateTime(t.Year, t.Month, 1).AddMonths(m);
                    TheAirline.Helpers.ScenarioHelpers.UpdateScenario(so);
                    Console.WriteLine("  month " + m + " below zero: ended " + so.IsEnded);
                }
                av.Refresh();
                Console.WriteLine("  message: " + shell.MessageTitle + " | " + (shell.Message ?? "").Replace(Environment.NewLine, " / ") + " | paused " + TheAirline.Helpers.Workers.GameObjectWorker.GetInstance().IsPaused);
                Console.WriteLine("  tab: " + av.Scenario.ResultText);
                if (!so.IsEnded || so.IsSuccess || !shell.HasMessage) { Console.WriteLine("  FAILED losing"); Environment.Exit(1); }
                shell.DismissMessageCommand.Execute(null);
                av.Refresh();
                Console.WriteLine("  shown once: " + !shell.HasMessage);
                if (shell.HasMessage) { Console.WriteLine("  FAILED message repeats"); Environment.Exit(1); }
            }
        }
        if (Environment.GetEnvironmentVariable("TAP_LEAK") == "1") FindStale();
        if (Environment.GetEnvironmentVariable("TAP_LEAK") == "2") FindLeakRoots();
        // saved and loaded, the scenario and its result stay
        {
            var g = TheAirline.Models.General.GameObject.GetInstance();
            string name = g.Scenario.Scenario.Name; bool lost = g.Scenario.ScenarioFailed != null;
            TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("scenariotest");
            TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("scenariotest");
            g = TheAirline.Models.General.GameObject.GetInstance();
            Console.WriteLine("  after load: " + (g.Scenario == null ? "no scenario" : g.Scenario.Scenario.Name + ", lost " + (g.Scenario.ScenarioFailed != null) + ", shown " + g.Scenario.EndShown + ", goals " + TheAirline.Helpers.ScenarioService.Goals(g.Scenario).Count + ", success text " + (g.Scenario.Scenario.SuccessText != null)));
            if (g.Scenario == null || g.Scenario.Scenario.Name != name || (g.Scenario.ScenarioFailed != null) != lost || !g.Scenario.EndShown) { Console.WriteLine("  FAILED scenario save"); Environment.Exit(1); }
            // winning: every goal met when the end year comes
            var won = new TheAirline.Models.General.Scenarios.ScenarioObject(g.Scenario.Scenario);
            g.GameTime = new DateTime(won.Scenario.EndYear, 1, 1);
            g.HumanAirline.Money = 1e9;
            foreach (var f in won.Scenario.Failures.Where(x => x.Type != TheAirline.Models.General.Scenarios.ScenarioFailure.FailureType.Cash && x.Type != TheAirline.Models.General.Scenarios.ScenarioFailure.FailureType.Debt).ToList()) won.Scenario.Failures.Remove(f);
            TheAirline.Helpers.ScenarioService.Check(won);
            Console.WriteLine("  end year with cash: won " + won.IsSuccess + " | " + g.NewsBox.GetNews().Last().Subject + ": " + g.NewsBox.GetNews().Last().Body);
            if (!won.IsSuccess || won.ScenarioFailed != null) { Console.WriteLine("  FAILED winning"); Environment.Exit(1); }
        }
        Console.WriteLine("SCENARIO TEST OK");
        Environment.Exit(0);
    }

    /// <summary>Walks the game's object graph and prints the reference chain to an airport or country from an earlier data load.</summary>
    static void FindStale()
    {
        var airports = new System.Collections.Generic.HashSet<object>(TheAirline.Models.Airports.Airports.GetAllAirports(), System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var countries = new System.Collections.Generic.HashSet<object>(TheAirline.Models.General.Countries.Countries.GetAllCountries(), System.Collections.Generic.ReferenceEqualityComparer.Instance);
        foreach (var c in TheAirline.Models.General.Countries.TemporaryCountries.GetCountries()) countries.Add(c);
        var parent = new System.Collections.Generic.Dictionary<object, (object, string)>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var queue = new System.Collections.Generic.Queue<object>();
        void Add(object o, object from, string via)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o is Delegate || o is Type || o is System.Reflection.MemberInfo) return;
            if (parent.ContainsKey(o)) return;
            parent[o] = (from, via);
            queue.Enqueue(o);
        }
        Add(TheAirline.Models.General.GameObject.GetInstance(), null, "GameObject");
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines()) Add(a, null, "Airlines");
        foreach (var a in TheAirline.Models.Airliners.Airliners.GetAllAirliners()) Add(a, null, "Airliners");
        int seen = 0, staleAirports = 0, staleCountries = 0; object first = null;
        while (queue.Count > 0 && seen < 20000000)
        {
            var o = queue.Dequeue(); seen++;
            if (o is TheAirline.Models.Airports.Airport && !airports.Contains(o)) { staleAirports++; first = first ?? o; continue; }
            if (o is TheAirline.Models.General.Countries.Country && !countries.Contains(o)) { staleCountries++; first = first ?? o; continue; }
            if (o is System.Collections.IEnumerable en && !(o is string))
            {
                int i = 0;
                try { foreach (var x in en) { if (x is System.Collections.DictionaryEntry de) { Add(de.Key, o, "[key]"); Add(de.Value, o, "[value]"); } else Add(x, o, "[" + i + "]"); i++; } } catch { }
                if (o is Array) continue;
            }
            for (var t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPrimitive || f.FieldType.IsEnum) continue;
                    object v; try { v = f.GetValue(o); } catch { continue; }
                    Add(v, o, f.Name);
                }
        }
        Console.WriteLine("  graph: " + seen + " objects, stale airports " + staleAirports + ", stale countries " + staleCountries);
        foreach (var grp in parent.Keys.GroupBy(k => k.GetType().Name).OrderByDescending(x => x.Count()).Take(14))
            Console.WriteLine("    " + grp.Key + ": " + grp.Count().ToString("N0"));
        if (first != null)
        {
            var chain = new System.Collections.Generic.List<string>();
            object cur = first;
            for (int i = 0; i < 40 && cur != null; i++)
            {
                var (from, via) = parent[cur];
                string name = cur is TheAirline.Models.Airports.Airport ap ? "Airport " + ap.Profile.IATACode : cur is TheAirline.Models.General.Countries.Country cc ? "Country " + cc.Name : cur is TheAirline.Models.Airlines.Airline al ? "Airline " + al.Profile.Name : cur.GetType().Name;
                chain.Add(name + " <-" + via + "-");
                cur = from;
            }
            Console.WriteLine("  path: " + string.Join(" ", chain));
        }
    }

    /// <summary>Which static fields still reach an airport from an earlier data load (memory not freed between games).</summary>
    static void FindLeakRoots()
    {
        var current = new System.Collections.Generic.HashSet<object>(TheAirline.Models.Airports.Airports.GetAllAirports(), System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var asms = new[] { typeof(TheAirline.Models.Airports.Airport).Assembly, typeof(AirlineViewModel).Assembly };
        foreach (var asm in asms)
        foreach (var type in asm.GetTypes())
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.IsLiteral) continue;
                object root; try { root = f.GetValue(null); } catch { continue; }
                if (root == null) continue;
                // walk from this root, stop at current airports, count stale ones
                var seen = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
                var parent = new System.Collections.Generic.Dictionary<object, (object, string)>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
                var q = new System.Collections.Generic.Queue<object>();
                void Add(object o, object from, string via)
                {
                    if (o == null || o is string || o.GetType().IsPrimitive || o is Type || o is System.Reflection.MemberInfo || o is System.Threading.Thread) return;
                    if (!seen.Add(o)) return;
                    parent[o] = (from, via);
                    q.Enqueue(o);
                }
                Add(root, null, type.Name + "." + f.Name);
                object stale = null; int n = 0;
                while (q.Count > 0 && n < 40000000 && stale == null)
                {
                    var o = q.Dequeue(); n++;
                    if (o is TheAirline.Models.Airports.Airport) { if (!current.Contains(o)) stale = o; continue; }
                    if (o is Delegate d) { foreach (var x in d.GetInvocationList()) Add(x.Target, o, "target"); continue; }
                    if (o is System.Collections.IEnumerable en)
                    {
                        try { int i = 0; foreach (var x in en) { if (x is System.Collections.DictionaryEntry de) { Add(de.Key, o, "[key]"); Add(de.Value, o, "[value]"); } else Add(x, o, "[" + (i++) + "]"); } } catch { }
                        if (o is Array) continue;
                    }
                    for (var t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                        foreach (var ff in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                        {
                            if (ff.FieldType.IsPrimitive || ff.FieldType.IsEnum) continue;
                            object v; try { v = ff.GetValue(o); } catch { continue; }
                            Add(v, o, ff.Name);
                        }
                }
                if (stale == null) continue;
                var chain = new System.Collections.Generic.List<string>();
                object cur = stale;
                for (int i = 0; i < 25 && cur != null; i++)
                {
                    var (from, via) = parent[cur];
                    chain.Add((cur is TheAirline.Models.Airports.Airport ap ? "Airport " + ap.Profile.IATACode : cur.GetType().Name) + " <-" + via + "-");
                    cur = from;
                }
                Console.WriteLine("  LEAK " + type.FullName + "." + f.Name + ": " + string.Join(" ", chain));
            }
        }
    }

    static async Task Wait(Func<bool> done, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done()) { if (sw.Elapsed.TotalSeconds > 120) throw new Exception("timeout: " + what); await Task.Delay(100); }
    }

    // per-flight economics of common types at 80% load: "vm unit"
    /// <summary>"vm cargounit": what each freighter type earns and costs per flight at 75% full and the going cargo rate.</summary>
    static void CargoUnitTable()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        var atl = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "ATL");
        var types = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().OfType<TheAirline.Models.Airliners.AirlinerCargoType>()
            .Where(t => t.Produced.From.Year <= g.GameTime.Year).GroupBy(t => t.Name).Select(x => x.First()).OrderBy(t => t.CargoSize).ToList();
        Console.WriteLine(types.Count + " freighter types; inflation x" + TheAirline.Helpers.GeneralHelpers.GetInflationPrice(1).ToString("0.00") + ", fuel " + g.FuelPrice.ToString("0.000"));
        foreach (var t in types.Where((t, i) => i % Math.Max(1, types.Count / 14) == 0))
        {
            var a = new TheAirline.Models.Airliners.Airliner("CT", t, "N-CT", g.GameTime.AddYears(-10)) { Airline = me };
            var f = new TheAirline.Models.Airliners.FleetAirliner(TheAirline.Models.Airliners.FleetAirliner.PurchasedType.Bought, g.GameTime, me, a, atl);
            foreach (double km in new[] { 500.0, 1500, 3000, 6000 })
            {
                if (km > t.Range) continue;
                double hours = km / Math.Max(100, t.CruisingSpeed) + 0.5;
                double load = t.CargoSize * 0.75;
                double rate = km * TheAirline.Helpers.GeneralHelpers.GetInflationPrice(0.0081);
                double income = load * rate;
                double fuel = g.FuelPrice * km * t.CargoSize * a.FuelConsumption * 0.55 + g.FuelPrice * a.FuelConsumption * load * km * 0.45;
                double landing = TheAirline.Helpers.AirportHelpers.GetLandingFee(atl, a);
                double tax = 5 * load;
                double added = TheAirline.Helpers.FlightCosts.Calculate(a, km, hours, income).Total;
                double flightsMonth = 300.0 / hours; // about 10 hours a day in the air
                double own = TheAirline.Helpers.OwnershipService.MonthlyShare * a.GetPrice() / flightsMonth;
                double lease = a.LeasingPrice / flightsMonth;
                double cost = fuel + landing + tax + added;
                Console.WriteLine(string.Format("  {0,-26} {1,5:N0} units {2,5:N0} km | income {3,8:N0} | fuel {4,7:N0} landing {5,5:N0} tax {6,5:N0} added {7,6:N0} own {8,6:N0} lease {9,6:N0} | margin owned {10,4:0}% leased {11,4:0}% | rate for 10% owned {12:0.00}x",
                    t.Name, t.CargoSize, km, income, fuel, landing, tax, added, own, lease, 100 * (income - cost - own) / income, 100 * (income - cost - lease) / income, (cost + own) / 0.9 / income));
            }
        }
        Environment.Exit(0);
    }

    static void UnitTable()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        var atl = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "ATL");
        Console.WriteLine("year " + g.GameTime.Year + " fuel " + g.FuelPrice.ToString("0.000") + " inflation x" + TheAirline.Helpers.GeneralHelpers.GetInflationPrice(1).ToString("0.00") + " cost level " + g.CostLevel);
        string[] names = { "Beechcraft 1900D", "Saab 340B", "ATR 72-500", "Bombardier CRJ200", "Embraer ERJ 145", "Boeing 717-200", "Airbus A319-100", "Boeing 737-800", "Airbus A320-200", "Boeing 757-200", "Boeing 767-300ER", "Airbus A330-300", "Boeing 777-200ER", "Boeing 747-400" };
        foreach (string n in names)
        {
            var t = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().FirstOrDefault(x => x.Name == n) as TheAirline.Models.Airliners.AirlinerPassengerType;
            if (t == null) { Console.WriteLine("  (no " + n + ")"); continue; }
            var a = new TheAirline.Models.Airliners.Airliner("TEST", t, "N-TEST", g.GameTime);
            a.Airline = me;
            int seats = Math.Max(1, a.GetTotalSeatCapacity());
            foreach (double km in new[] { 400.0, 1200, 3000, 7000 })
            {
                if (km > t.Range) continue;
                double hours = km / Math.Max(100, t.CruisingSpeed) + 0.5;
                double pax = seats * 0.8;
                double fare = TheAirline.Helpers.PricingService.MarketFare(km);
                double tickets = pax * fare;
                double fuelPrice = g.FuelPrice;
                double fuel = fuelPrice * km * t.MaxSeatingCapacity * a.FuelConsumption * 0.55 + fuelPrice * km * a.FuelConsumption * pax * 0.45;
                double landing = TheAirline.Helpers.AirportHelpers.GetLandingFee(atl, a);
                double tax = 5 * pax;
                double cabin = t.CabinCrew * hours * me.Fees.GetValue(TheAirline.Models.Airlines.FeeTypes.GetType("Cabin Wage"));
                double pilotMonth = t.CockpitCrew * TheAirline.Helpers.GeneralHelpers.GetInflationPrice(1) * 0 ;
                var pc = TheAirline.Helpers.FlightCosts.Calculate(a, km, hours, tickets);
                double oldCost = fuel + landing + tax + cabin;
                double newCost = oldCost + pc.Total;
                Console.WriteLine(string.Format("  {0,-18} {1,4} seats {2,5:N0} km | fare {3,5:N0} tickets {4,8:N0} | fuel {5,7:N0} landing {6,5:N0} tax {7,5:N0} cabin {8,4:N0} | +ground {9,5:N0} maint {10,6:N0} enroute {11,5:N0} crew {12,5:N0} sales {13,6:N0} | margin old {14,4:0}% new {15,4:0}% | CASK {16:0.000} yield {17:0.000}",
                    n, seats, km, fare, tickets, fuel, landing, tax, cabin, pc.Ground, pc.Maintenance, pc.EnRoute, pc.Crew, pc.Distribution,
                    (tickets - oldCost) / tickets * 100, (tickets - newCost) / tickets * 100, newCost / (seats * km), tickets / (pax * km)));
            }
        }
        Environment.Exit(0);
    }

    // measures what flying costs: "vm costs"
    /// <summary>"vm year": a year month by month: each airline's fares against the market, load and margin.</summary>
    static void YearProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int m = 0; m < 12; m++)
        {
            int startMonth = g.GameTime.Month;
            var swY = System.Diagnostics.Stopwatch.StartNew();
            while (g.GameTime.Month == startMonth) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            var last = g.GameTime.AddMonths(-1);
            var margins = new System.Collections.Generic.List<double>();
            foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => a.Fleet.Count > 0 && !a.IsHuman))
            {
                double i2 = 0, c2 = 0;
                foreach (TheAirline.Models.General.Finances.Invoice.InvoiceType t in Enum.GetValues(typeof(TheAirline.Models.General.Finances.Invoice.InvoiceType)))
                {
                    if (t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Total || t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Purchases || t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Loans) continue;
                    double v = a.GetInvoicesAmountMonth(last.Year, last.Month, t);
                    if (v > 0) i2 += v; else c2 += v;
                }
                if (i2 > 0) margins.Add(100 * (i2 + c2) / i2);
            }
            margins.Sort();
            var shared = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(a => a.Routes).OfType<TheAirline.Models.Routes.PassengerRoute>().Where(r => r.HasAirliner).ToList();
            int sharedCount = shared.Count(r => TheAirline.Helpers.AIPricingService.Rivals(r).Count > 0);
            Console.WriteLine("-- " + last.ToString("MMM yyyy") + " (" + (swY.ElapsedMilliseconds / 1000.0).ToString("0") + " s) | " + margins.Count + " airlines earning | margin median " + (margins.Count > 0 ? margins[margins.Count / 2] : 0).ToString("0") + "%, " + margins.Count(x => x < 0) + " losing money | " + shared.Count + " passenger routes, " + sharedCount + " shared with a rival");
            foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => a.Fleet.Count > 0).OrderByDescending(a => a.Fleet.Count).Take(6))
            {
                var routes = a.Routes.OfType<TheAirline.Models.Routes.PassengerRoute>().Where(r => r.HasAirliner).ToList();
                var ratios = routes.Select(r => TheAirline.Helpers.AIPricingService.EconomyFare(r) / TheAirline.Helpers.PricingService.MarketFare(r.Destination1, r.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass)).Where(x => x > 0 && !double.IsInfinity(x)).ToList();
                var loads = routes.Select(r => TheAirline.Helpers.ConnectionService.MonthLoad(r)).Where(x => x > 0).ToList();
                double inc = 0, cost = 0;
                foreach (TheAirline.Models.General.Finances.Invoice.InvoiceType t in Enum.GetValues(typeof(TheAirline.Models.General.Finances.Invoice.InvoiceType)))
                {
                    if (t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Total || t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Purchases || t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Loans) continue;
                    double v = a.GetInvoicesAmountMonth(last.Year, last.Month, t);
                    if (v > 0) inc += v; else cost += v;
                }
                Console.WriteLine(string.Format("   {0,-22} {1,3} aircraft {2,3} routes | fare/market {3:0.00} | load {4:0}% | operating margin {5:0}% | cash {6:N0}",
                    a.Profile.Name, a.Fleet.Count, routes.Count, ratios.Count > 0 ? ratios.Average() : 0, loads.Count > 0 ? loads.Average() * 100 : 0, inc > 0 ? 100 * (inc + cost) / inc : 0, a.Money));
            }
        }
    }

    /// <summary>"vm demand": rebuild every airport's demand a few times, timing it, and time its parts on a sample.</summary>
    static void DemandProbe()
    {
        if (Environment.GetEnvironmentVariable("TAP_WORLD") == "1") TheAirline.Infrastructure.Setup.LoadAirports();
        var airports = TheAirline.Models.Airports.Airports.GetAllAirports();
        Console.WriteLine("airports: " + airports.Count);
        DemandCheck("at start");
        for (int k = 0; k < 2; k++)
        {
            foreach (var a in airports) a.Statics = new TheAirline.Models.Airports.AirportStatics(a);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TheAirline.Helpers.PassengerHelpers.CreateDestinationDemand();
            Console.WriteLine("DEMAND BUILD " + k + ": " + sw.ElapsedMilliseconds + " ms, memory " + GC.GetTotalMemory(true)/1048576 + " MB");
            DemandCheck("after build " + k);
        }
        var sample = airports.Take(300).ToList();
        var t = System.Diagnostics.Stopwatch.StartNew();
        double x = 0;
        foreach (var a in sample) foreach (var b in sample) x += TheAirline.Helpers.MathHelpers.GetDistance(a, b);
        Console.WriteLine("PART distance x90k: " + t.ElapsedMilliseconds + " ms");
        TheAirline.Helpers.Profiler.Reset(); TheAirline.Helpers.Profiler.Enabled = true;
        t.Restart();
        foreach (var a in sample) foreach (var b in sample) if (a != b) TheAirline.Helpers.PassengerHelpers.CreateDestinationPassengers(a, b);
        Console.WriteLine("PART passengers x90k: " + t.ElapsedMilliseconds + " ms");
        TheAirline.Helpers.Profiler.Enabled = false; Console.Write(TheAirline.Helpers.Profiler.Report());
        t.Restart();
        foreach (var a in sample) foreach (var b in sample) if (a != b) TheAirline.Helpers.PassengerHelpers.CreateDestinationCargo(a, b);
        Console.WriteLine("PART cargo x90k: " + t.ElapsedMilliseconds + " ms");
        t.Restart();
        foreach (var a in sample) foreach (var b in sample) x += a.GetDestinationPassengersRate(b, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
        Console.WriteLine("PART demand lookup x90k: " + t.ElapsedMilliseconds + " ms");
    }

    /// <summary>
    ///     "vm bloat": make a save the way an earlier version did after a load (the whole world's airports in a North
    ///     America game, saved in the old format), load it, check the hint in the news, and take the airports outside
    ///     North America out through Options > Game area.
    /// </summary>
    static async Task BloatTest(AirlineViewModel av)
    {
        Console.WriteLine("BLOAT TEST");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int i = 0; i < 20; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var game = TheAirline.Models.Airports.Airports.GetAllAirports().ToList();
        int gameCount = game.Count;
        var codes = new System.Collections.Generic.HashSet<string>(game.Select(a => a.Profile.IATACode));
        TheAirline.Infrastructure.Setup.LoadAirports();
        var extra = TheAirline.Models.Airports.Airports.GetAllAirports().Where(a => !codes.Contains(a.Profile.IATACode)).GroupBy(a => a.Profile.IATACode).Select(x => x.First()).ToList();
        TheAirline.Models.Airports.Airports.Clear();
        foreach (var a in game) TheAirline.Models.Airports.Airports.AddAirport(a);
        foreach (var a in extra) TheAirline.Models.Airports.Airports.AddAirport(a);
        // one of the extra airports is in use (the player's airline has London): it must be kept
        var keepAirport = extra.First(a => a.Profile.IATACode == "LHR");
        g.HumanAirline.AddAirport(keepAirport);
        Console.WriteLine("  game " + gameCount + " airports, made it " + TheAirline.Models.Airports.Airports.GetAllAirports().Count + " (as an old load did)");
        // an old save: it doesn't list the data file airports
        typeof(TheAirline.Infrastructure.Setup).GetProperty("DataAirportCodes").SetValue(null, null);
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("bloattest");
        string file = TheAirline.Infrastructure.AppSettings.GetCommonApplicationDataPath() + "/saves/bloattest.sav";
        Console.WriteLine("  bloated save " + new System.IO.FileInfo(file).Length/1024 + " KB");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("bloattest");
        int loaded = TheAirline.Models.Airports.Airports.GetAllAirports().Count;
        var hint = TheAirline.Models.General.GameObject.GetInstance().NewsBox.GetNews().FirstOrDefault(n => n.Subject == "Airports outside your area?");
        Console.WriteLine("  loaded in " + sw.ElapsedMilliseconds + " ms: " + loaded + " airports | news: " + hint?.Body);
        if (loaded < gameCount + 1000 || hint == null) { Console.WriteLine("  FAILED load or hint"); Environment.Exit(1); }

        var op = av.Options;
        op.Reload();
        Console.WriteLine("  " + op.AreaText);
        foreach (var a in op.Areas) Console.WriteLine("    area: " + a.Name);
        op.Area = op.Areas.First();
        Console.WriteLine("  picked " + op.Area.Name + " | " + op.AreaPreview);
        op.CleanAreaCommand.Execute(null);
        Console.WriteLine("  asks: " + op.PendingText);
        op.ConfirmCommand.Execute(null);
        await Wait(() => op.AreaStatus != null && op.AreaStatus.StartsWith("Took out"), "airports taken out");
        int after = TheAirline.Models.Airports.Airports.GetAllAirports().Count;
        bool lhrKept = TheAirline.Models.Airports.Airports.GetAllAirports().Any(a => a.Profile.IATACode == "LHR");
        Console.WriteLine("  " + op.AreaStatus + " | LHR kept " + lhrKept + " | after: " + op.AreaPreview);
        var atl = TheAirline.Models.Airports.Airports.GetAllAirports().First(a => a.Profile.IATACode == "ATL");
        Console.WriteLine("  ATL demand to " + atl.GetDestinationDemands().Count + " airports");
        if (after != gameCount + 1 || !lhrKept || atl.GetDestinationDemands().Count < 300) { Console.WriteLine("  FAILED clean-up"); Environment.Exit(1); }
        for (int i = 0; i < 5; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        TheAirline.Infrastructure.Setup.LoadAirports();
        TheAirline.Models.Airports.Airports.Clear();
        foreach (var a in game.Concat(new[] {keepAirport})) TheAirline.Models.Airports.Airports.AddAirport(a);
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("bloattest");
        Console.WriteLine("  saved again " + new System.IO.FileInfo(file).Length/1024 + " KB");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("bloattest");
        int again = TheAirline.Models.Airports.Airports.GetAllAirports().Count;
        bool hintAgain = TheAirline.Models.General.GameObject.GetInstance().NewsBox.GetNews().Count(n => n.Subject == "Airports outside your area?") > 1;
        Console.WriteLine("  loaded again: " + again + " airports, hint again " + hintAgain);
        if (again != after || hintAgain) { Console.WriteLine("  FAILED second load"); Environment.Exit(1); }
        TheAirline.Helpers.SerializedLoadSaveHelpers.DeleteSavedGame("bloattest");
        Console.WriteLine("BLOAT TEST OK");
    }

    /// <summary>"vm replace": a route flown by one aircraft, replaced by another on the route screen (and back).</summary>
    static async Task ReplaceTest(AirlineViewModel av)
    {
        Console.WriteLine("REPLACE TEST");
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var mk = av.Market;
        // two aircraft of the same type, the cheapest that has a home base
        foreach (var item in mk.Aircraft.OrderBy(a => a.Airliner.GetPrice()).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        var type = mk.Selected.Airliner.Type;
        mk.BuyCommand.Execute(null);
        av.Refresh();
        var second = mk.Aircraft.Where(a => a.Airliner.Type == type).OrderBy(a => a.Airliner.GetPrice()).FirstOrDefault();
        if (second != null) { mk.Selected = second; mk.BuyCommand.Execute(null); }
        av.Refresh();
        Console.WriteLine("  fleet: " + string.Join(", ", h.Fleet.Select(f => f.Name + " " + f.Airliner.Type.Name)));
        if (h.Fleet.Count < 2) { Console.WriteLine("  FAILED could not buy two aircraft"); Environment.Exit(1); }

        var ap = av.Airports;
        await Wait(() => !ap.IsBusy, "advice");
        ap.SelectedAdvice = ap.Advice.First(a => a.Advice.FleetCanFly);
        var cr = ap.Create;
        foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
        cr.CreateCommand.Execute(null);
        foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
        if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
        var route = h.Routes.First(r => r.HasAirliner);
        var a = route.GetAirliners()[0];
        var b = h.Fleet.First(f => f != a);
        if (b.Pilots.Count < b.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(b);
        int flights = route.TimeTable.Entries.Count(e => e.Airliner == a);
        Console.WriteLine("  route " + route.Destination1.Profile.IATACode + "-" + route.Destination2.Profile.IATACode + " flown by " + a.Name + " (" + flights + " weekly flights), spare " + b.Name);
        for (int i = 0; i < 3; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var times = route.TimeTable.Entries.Where(e => e.Airliner == a).Select(e => e.Day + " " + e.Time).OrderBy(x => x).ToList();

        var rd = new TheAirline.Presentation.RouteDetailViewModel(route, () => { });
        Console.WriteLine("  button shown " + rd.ShowReplaceButton);
        rd.OpenReplaceCommand.Execute(null);
        Console.WriteLine("  replace: " + string.Join(" / ", rd.ReplaceFromChoices.Select(c => c.Name)));
        foreach (var c in rd.ReplaceToChoices) Console.WriteLine("    with: " + c.Name);
        rd.ReplaceTo = rd.ReplaceToChoices.First(c => c.Value.Airliner == b);
        Console.WriteLine("  " + rd.ReplaceText);
        rd.ReplaceCommand.Execute(null);
        Console.WriteLine("  done: " + rd.Status + " | " + rd.AircraftText);
        var timesB = route.TimeTable.Entries.Where(e => e.Airliner == b).Select(e => e.Day + " " + e.Time).OrderBy(x => x).ToList();
        bool sameTimes = times.SequenceEqual(timesB);
        Console.WriteLine("  " + b.Name + " has " + timesB.Count + " flights, same times " + sameTimes + " | " + a.Name + " routes " + a.Routes.Count + " status " + a.Status + " | " + b.Name + " status " + b.Status);
        if (!sameTimes || route.TimeTable.Entries.Any(e => e.Airliner == a) || a.Routes.Contains(route) || !b.Routes.Contains(route) || rd.IsReplacing)
        { Console.WriteLine("  FAILED replace"); Environment.Exit(1); }

        double paxBefore = route.Statistics.GetTotalValue(TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Passengers"));
        for (int i = 0; i < 4; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        double paxAfter = route.Statistics.GetTotalValue(TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Passengers"));
        Console.WriteLine("  four days later: " + b.Name + " at " + b.CurrentPosition?.Profile.IATACode + ", status " + b.Status + " | passengers on the route " + paxBefore + " -> " + paxAfter);
        if (paxAfter <= paxBefore) { Console.WriteLine("  FAILED the new aircraft doesn't fly the route"); Environment.Exit(1); }

        // and back again: the first aircraft is free now
        rd.OpenReplaceCommand.Execute(null);
        rd.ReplaceTo = rd.ReplaceToChoices.FirstOrDefault(c => c.Value.Airliner == a);
        Console.WriteLine("  back: " + rd.ReplaceText);
        rd.ReplaceCommand.Execute(null);
        Console.WriteLine("  " + rd.Status);
        if (!a.Routes.Contains(route) || b.Routes.Contains(route)) { Console.WriteLine("  FAILED replace back"); Environment.Exit(1); }

        // something that can't: an aircraft whose range is too short
        var shortType = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().FirstOrDefault(t => t.TypeAirliner == a.Airliner.Type.TypeAirliner && t.Range < route.GetDistance() && t.Produced.From.Year <= TheAirline.Models.General.GameObject.GetInstance().GameTime.Year);
        if (shortType != null)
        {
            var c = TheAirline.Helpers.AirlineHelpers.CreateAirliner(h, shortType);
            h.AddAirliner(c);
            string msg;
            bool ok = TheAirline.Helpers.AircraftReplaceService.Replace(route, a, c, out msg);
            Console.WriteLine("  short range " + c.Name + " (" + shortType.Name + ", " + shortType.Range + " km): " + ok + " | " + msg);
            if (ok) { Console.WriteLine("  FAILED short range accepted"); Environment.Exit(1); }
        }
        Console.WriteLine("REPLACE TEST OK");
    }

    static void DemandCheck(string when)
    {
        var human = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var home = human.Airports.Count > 0 ? human.Airports[0] : TheAirline.Models.Airports.Airports.GetAllAirports().First();
        var all = TheAirline.Models.Airports.Airports.GetAllAirports();
        int withDemand = all.Count(a => a.Statics.GetDestinationPassengersSum() > 0);
        Console.WriteLine("DEMAND " + when + ": " + home.Profile.IATACode + " has demand to " + home.GetDestinationDemands().Count + " airports, total " + home.Statics.GetDestinationPassengersSum() + "; " + withDemand + " of " + all.Count + " airports have demand");
    }

    /// <summary>"vm savetime": play TAP_WARM days (default 60), then save a few times and show where the time goes.</summary>
    static void SaveProbe()
    {
        int warm = int.TryParse(Environment.GetEnvironmentVariable("TAP_WARM"), out var w) ? w : 60;
        string loadFirst = Environment.GetEnvironmentVariable("TAP_LOADFILE");
        if (!string.IsNullOrEmpty(loadFirst))
        {
            var lf = System.Diagnostics.Stopwatch.StartNew();
            TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame(loadFirst);
            Console.WriteLine("LOADED " + loadFirst + " in " + lf.ElapsedMilliseconds + " ms: " + TheAirline.Models.General.GameObject.GetInstance().GameTime.ToShortDateString() + ", " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Sum(a => a.Fleet.Count) + " aircraft");
        }
        for (int i = 0; i < warm; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var all = TheAirline.Models.Airlines.Airlines.GetAllAirlines();
        Console.WriteLine("after " + warm + " days: " + all.Sum(a => a.Fleet.Count) + " aircraft, " + all.Sum(a => a.Routes.Count) + " routes");
        DemandCheck("before saving");
        TheAirline.Helpers.GraphSerializer.CollectStats = true;
        for (int k = 0; k < 3; k++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("autosave");
            Console.WriteLine("SAVE " + k + ": " + sw.ElapsedMilliseconds + " ms");
            if (k == 0) Console.WriteLine(TheAirline.Helpers.GraphSerializer.LastStats);
        }
        TheAirline.Helpers.GraphSerializer.CollectStats = false;
        var lw = System.Diagnostics.Stopwatch.StartNew();
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("autosave");
        Console.WriteLine("LOAD: " + lw.ElapsedMilliseconds + " ms");
        DemandCheck("after the second load");
        for (int i = 0; i < 2; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("after load: " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Sum(a => a.Fleet.Count) + " aircraft, " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Sum(a => a.Routes.Count) + " routes");
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("autosave");
        string file = TheAirline.Infrastructure.AppSettings.GetCommonApplicationDataPath() + "/saves/autosave.sav";
        Console.WriteLine("SAVE after load: " + sw2.ElapsedMilliseconds + " ms, " + new System.IO.FileInfo(file).Length/1024 + " KB");
    }

    /// <summary>"vm perf": warm up (TAP_WARM days, default 40), then time TAP_DAYS game days (default 10) and show where the time goes.</summary>
    static void PerfProbe()
    {
        int warm = int.TryParse(Environment.GetEnvironmentVariable("TAP_WARM"), out var w) ? w : 40;
        int days = int.TryParse(Environment.GetEnvironmentVariable("TAP_DAYS"), out var d) ? d : 10;
        var all = TheAirline.Models.Airlines.Airlines.GetAllAirlines();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < warm; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("warm-up " + warm + " days: " + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s, " + (sw.ElapsedMilliseconds / Math.Max(1, warm)) + " ms per day");
        Console.WriteLine(all.Count(a => a.Fleet.Count > 0) + " airlines flying, " + all.Sum(a => a.Fleet.Count) + " aircraft, " + all.Sum(a => a.Routes.Count) + " routes, " + TheAirline.Models.Airliners.Airliners.GetAllAirliners().Count + " aircraft in the world (" + TheAirline.Models.Airliners.Airliners.GetAllAirliners().Count(a => a.Airline == null) + " not owned)");
        if (Environment.GetEnvironmentVariable("TAP_MICRO") == "1")
        {
            var sale = TheAirline.Models.Airliners.Airliners.GetAirlinersForSale(a => a.Type is TheAirline.Models.Airliners.AirlinerPassengerType);
            var t = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 10; k++) TheAirline.Models.Airliners.Airliners.GetAirlinersForSale(a => a.Type is TheAirline.Models.Airliners.AirlinerPassengerType);
            Console.WriteLine("MICRO for sale list x10: " + t.ElapsedMilliseconds + " ms (" + sale.Count + ")");
            t.Restart(); long x = 0; foreach (var a in sale) x += a.GetPrice(); Console.WriteLine("MICRO GetPrice all: " + t.ElapsedMilliseconds + " ms");
            t.Restart(); foreach (var a in sale) x += a.GetAge(); Console.WriteLine("MICRO GetAge all: " + t.ElapsedMilliseconds + " ms");
            t.Restart(); foreach (var a in sale) x += a.Range; Console.WriteLine("MICRO Range all: " + t.ElapsedMilliseconds + " ms");
            t.Restart(); foreach (var a in sale) x += a.LeasingPrice; Console.WriteLine("MICRO LeasingPrice all: " + t.ElapsedMilliseconds + " ms");
            t.Restart(); for (int k = 0; k < 10; k++) TheAirline.Models.Airliners.Airliners.GetAirlinersForLeasing(); Console.WriteLine("MICRO leasing list x10: " + t.ElapsedMilliseconds + " ms");
            var al = all.First(a => !a.IsHuman && a.Fleet.Count > 0 && a.Airports.Count > 1);
            t.Restart(); for (int k = 0; k < 10; k++) TheAirline.Helpers.AIHelpers.GetAirlinerForRoute(al, al.Airports[0], al.Airports[1], true, al.AirlineRouteFocus); Console.WriteLine("MICRO GetAirlinerForRoute lease x10: " + t.ElapsedMilliseconds + " ms, preferred types " + al.Profile.PreferedAircrafts.Count);
            t.Restart(); for (int k = 0; k < 10; k++) TheAirline.Helpers.AIHelpers.GetAirlinerForRoute(al, al.Airports[0], al.Airports[1], false, al.AirlineRouteFocus); Console.WriteLine("MICRO GetAirlinerForRoute buy x10: " + t.ElapsedMilliseconds + " ms");
        }
        TheAirline.Helpers.Profiler.Reset();
        TheAirline.Helpers.Profiler.Enabled = true;
        var times = new System.Collections.Generic.List<long>();
        sw.Restart();
        for (int i = 0; i < days; i++)
        {
            var one = System.Diagnostics.Stopwatch.StartNew();
            TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            times.Add(one.ElapsedMilliseconds);
        }
        TheAirline.Helpers.Profiler.Enabled = false;
        Console.WriteLine("PERF " + days + " days: " + sw.ElapsedMilliseconds + " ms, " + (sw.ElapsedMilliseconds / Math.Max(1, days)) + " ms per day (" + string.Join(", ", times) + ")");
        Console.WriteLine(TheAirline.Helpers.Profiler.Report());
    }

    /// <summary>"vm airlines": every airline at the start and after two months: who flies and who doesn't.</summary>
    static void AirlinesProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        System.Action show = () =>
        {
            var all = TheAirline.Models.Airlines.Airlines.GetAllAirlines();
            Console.WriteLine(g.GameTime.ToShortDateString() + ": " + all.Count + " airlines, " + all.Count(a => a.Fleet.Count > 0) + " with aircraft, " + all.Count(a => a.Routes.Count > 0) + " with routes");
            foreach (var a in all.OrderByDescending(a => a.Fleet.Count).ThenBy(a => a.Profile.Name))
                Console.WriteLine(string.Format("   {0,-28} {1,-4} {2,-10} {3,-9} fleet {4,3} routes {5,3} airports {6,3} money {7,14:N0} start {8:yyyy} | home {9}",
                    a.Profile.Name, a.Profile.IATACode, a.Mentality, a.AirlineRouteFocus, a.Fleet.Count, a.Routes.Count, a.Airports.Count, a.Money, a.Profile.Founded == 0 ? g.GameTime : new DateTime(Math.Max(1, a.Profile.Founded), 1, 1),
                    a.Airports.FirstOrDefault()?.Profile.IATACode ?? "-"));
        };
        show();
        for (int d = 0; d < 60; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        show();
    }

    /// <summary>"vm cargo": what fills cargo flights and what they earn and cost.</summary>
    static void CargoProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int d = 0; d < 45; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var tr = new System.Collections.Concurrent.ConcurrentBag<TheAirline.Helpers.PassengerHelpers.CargoFlightTrace>();
        TheAirline.Helpers.PassengerHelpers.CargoTrace = t => tr.Add(t);
        TheAirline.Helpers.FlightLog.Clear(); TheAirline.Helpers.FlightLog.Enabled = true;
        for (int d = 0; d < 10; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        TheAirline.Helpers.PassengerHelpers.CargoTrace = null; TheAirline.Helpers.FlightLog.Enabled = false;
        var cargoAirlines = tr.Select(t => t.Airline).Distinct().ToList();
        foreach (var a in cargoAirlines)
        {
            var x = tr.Where(t => t.Airline == a).ToList();
            Console.WriteLine(string.Format("== {0}: {1} cargo flights | load {2:0}% | demand per flight / capacity: median {3:0.00} | saturated {4:0}% | price/market {5:0.00} | capacity median {6:0} | km median {7:0} | destination cargo facility {8:0}",
                a.Profile.Name, x.Count, 100 * x.Sum(t => t.Cargo) / Math.Max(1, x.Sum(t => t.Capacity)), x.Select(t => t.DemandPerFlight / Math.Max(1, t.Capacity)).OrderBy(v => v).ElementAt(x.Count / 2),
                100.0 * x.Count(t => t.DemandPerFlight >= t.TotalCapacity) / x.Count, x.Average(t => t.Price / Math.Max(0.01, t.MarketPrice)), x.Select(t => t.Capacity).OrderBy(v => v).ElementAt(x.Count / 2), x.Select(t => t.Km).OrderBy(v => v).ElementAt(x.Count / 2), x.Average(t => t.Facility)));
            var recs = TheAirline.Helpers.FlightLog.Records().Where(r => r.Airline == a && r.Seats == 0).ToList();
            if (recs.Count == 0) continue;
            double n = recs.Count;
            // the added realistic costs for the same flights
            var fleetTypes = a.Fleet.Where(f => f.Airliner.Type is TheAirline.Models.Airliners.AirlinerCargoType).ToList();
            double added = 0, own = 0;
            foreach (var r in recs)
            {
                var f = fleetTypes.FirstOrDefault(ff => ff.Airliner.Type.Name == r.Type);
                if (f == null) continue;
                added += TheAirline.Helpers.FlightCosts.Calculate(f.Airliner, r.Km, r.Hours, r.Tickets).Total;
                own += TheAirline.Helpers.PricingService.OwnershipPerFlight(f);
            }
            double inc = recs.Sum(r => r.Tickets), cost = recs.Sum(r => r.Fuel + r.Landing + r.Tax) + added + own;
            Console.WriteLine(string.Format("   per flight: income {0:N0} | fuel {1:N0} | landing {2:N0} | tax {3:N0} | added flight costs {4:N0} | ownership {5:N0} | margin {6:0}%",
                inc / n, recs.Sum(r => r.Fuel) / n, recs.Sum(r => r.Landing) / n, recs.Sum(r => r.Tax) / n, added / n, own / n, inc > 0 ? 100 * (inc - cost) / inc : 0));
        }
    }

    static string Med(System.Collections.Generic.IEnumerable<double> v) { var l = v.OrderBy(x => x).ToList(); return l.Count == 0 ? "-" : l[l.Count / 2].ToString("N1"); }

    /// <summary>"vm pax": what fills the flights, per airline (demand, competition, reputation, route score).</summary>
    static void PaxProbe(AirlineViewModel av)
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int d = 0; d < 45; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var list = new System.Collections.Concurrent.ConcurrentBag<TheAirline.Helpers.PassengerHelpers.FlightPaxTrace>();
        TheAirline.Helpers.PassengerHelpers.FlightTrace = t => { if (t.Type == TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass) list.Add(t); };
        for (int d = 0; d < 10; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        TheAirline.Helpers.PassengerHelpers.FlightTrace = null;
        Console.WriteLine("economy class, " + list.Count + " flights in 10 days, " + g.GameTime.ToShortDateString());
        foreach (var grp in list.GroupBy(t => t.Airline).OrderByDescending(x => x.Count()))
        {
            var x = grp.ToList();
            Console.WriteLine(string.Format("== {0}: {1} flights | load {2:0}% | demand per flight / seats {3:0.0} (median {4:0.0}) | saturated {5:0}% | competition {6:0.00} | reputation {7:0.00} | route score factor {8:0.00} | flights/day on pair {9:0.0}",
                grp.Key.Profile.Name, x.Count, 100.0 * x.Sum(t => t.Passengers) / Math.Max(1, x.Sum(t => t.Seats)),
                x.Average(t => t.PerFlight / Math.Max(1, t.Seats)), x.Select(t => t.PerFlight / Math.Max(1, t.Seats)).OrderBy(v => v).ElementAt(x.Count / 2),
                100.0 * x.Count(t => t.CapacityPercent >= 1) / x.Count, x.Average(t => t.Competition), x.Average(t => t.Reputation), x.Average(t => t.Score), x.Average(t => t.FlightsPerDay)));
        }
        Console.WriteLine("median per flight: direct " + Med(list.Select(t => t.Direct)) + ", connecting " + Med(list.Select(t => t.Connecting)) + ", nearby " + Med(list.Select(t => t.Nearby)) + " -> after score, season, price " + Med(list.Select(t => t.DailyDemand)) + " a day, / flights a day " + Med(list.Select(t => t.FlightsPerDay)) + " = " + Med(list.Select(t => t.PerFlight)) + " per flight vs seats " + Med(list.Select(t => (double) t.Seats)) + " | total capacity " + Med(list.Select(t => t.TotalCapacity)));
        foreach (var t in list.Where(t => t.Airline.Profile.IATACode == "US").Take(3).Concat(list.Where(t => t.Airline.Profile.IATACode != "US").Take(3)))
            Console.WriteLine("  " + t.Airline.Profile.IATACode + " " + t.Route.Destination1.Profile.IATACode + "-" + t.Route.Destination2.Profile.IATACode + ": direct " + t.Direct.ToString("N0") + " conn " + t.Connecting.ToString("N0") + " nearby " + t.Nearby.ToString("N0") + " daily " + t.DailyDemand.ToString("N0") + " flights/day " + t.FlightsPerDay.ToString("0.0") + " per flight " + t.PerFlight.ToString("N0") + " seats " + t.Seats + " pax " + t.Passengers);
        Console.WriteLine("per-flight demand vs seats, all airlines: " + string.Join(", ", new[] { 0.5, 1, 2, 5, 10, 1e9 }.Select((hi, i) => { double lo = i == 0 ? 0 : new[] { 0.5, 1, 2, 5, 10 }[i - 1]; return "<" + (hi > 1e8 ? "inf" : hi.ToString()) + ": " + list.Count(t => t.PerFlight / Math.Max(1, t.Seats) >= lo && t.PerFlight / Math.Max(1, t.Seats) < hi); })));
    }

    static void CostProbe(AirlineViewModel av)
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        me.Money += 400000000; g.CostLevel = 1;
        var mk = av.Market;
        int bought = 0;
        foreach (var item in mk.Aircraft.Where(x => x.Airliner.Type is TheAirline.Models.Airliners.AirlinerPassengerType pt && pt.MaxSeatingCapacity >= 100 && pt.MaxSeatingCapacity <= 200 && x.Airliner.Type.Range >= 2500 && !x.Airliner.Type.Name.Contains("Concorde")).OrderBy(x => x.Airliner.Age).Take(40).ToList())
        {
            mk.Selected = item;
            if (mk.Homebase == null) continue;
            mk.BuyCommand.Execute(null);
            if (++bought >= 4) break;
        }
        Console.WriteLine("bought " + bought + ": " + string.Join(", ", me.Fleet.Select(f => f.Airliner.Type.Name + " " + f.Airliner.GetTotalSeatCapacity() + " seats")));
        var ap = av.Airports;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (ap.IsBusy && sw.ElapsedMilliseconds < 120000) System.Threading.Thread.Sleep(200);
        int made = 0;
        foreach (var adv in ap.Advice.Where(x => x.Advice.FleetCanFly).Take(10).ToList())
        {
            ap.SelectedAdvice = adv;
            var cr = ap.Create;
            if (cr == null) continue;
            foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
            cr.CreateCommand.Execute(null);
            if (ap.Status != null && ap.Status.Contains("created")) made++;
            if (made >= 4) break;
        }
        foreach (var f in me.Fleet.ToList())
        {
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) { TheAirline.Helpers.PilotSupply.EnsureFamily(f.Airliner.Type.AirlinerFamily); TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f); }
            if (f.HasRoute) TheAirline.Presentation.FleetOverviewViewModel.TryStart(f);
        }
        Console.WriteLine("routes " + me.Routes.Count + ": " + string.Join(", ", me.Routes.Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode + " " + TheAirline.Helpers.MathHelpers.GetDistance(r.Destination1, r.Destination2).ToString("N0") + "km")));
        for (int d = 0; d < 60; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        TheAirline.Helpers.FlightLog.Enabled = true;
        foreach (int lvl in new[] { 0, 1, 0, 1 })
        {
            g.CostLevel = lvl;
            TheAirline.Helpers.FlightLog.Clear();
            for (int d = 0; d < 15; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            var mine = TheAirline.Helpers.FlightLog.Records().Where(r => r.Airline == me && r.Seats > 0).ToList();
            foreach (var grp in mine.GroupBy(r => r.Km.ToString("0")))
                Console.WriteLine(string.Format("  A/B level {0}: {1} km, {2} flights, {3:0}% full, tickets per flight {4:N0}", lvl, grp.Key, grp.Count(), grp.Sum(r => r.Passengers) * 100.0 / Math.Max(1, grp.Sum(r => r.Seats)), grp.Average(r => r.Tickets)));
        }
        g.CostLevel = 1;
        TheAirline.Helpers.FlightLog.Clear();
        for (int d = 0; d < 30; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var log = TheAirline.Helpers.FlightLog.Records();
        Console.WriteLine("flights logged in 30 days: " + log.Count);
        foreach (var grp in log.GroupBy(r => r.Airline == me ? "YOU " + r.Type : r.Type).OrderByDescending(x => x.Count()).Take(8))
        {
            var r = grp.ToList();
            Console.WriteLine(string.Format("  {0,-28} n={1,5} km {2,6:N0} seats {3,4:N0} pax {4,5:0.0} | tickets {5,8:N0} fees {6,6:N0} meals {7,6:N0} | fuel {8,7:N0} landing {9,6:N0} tax {10,6:N0} cabin {11,6:N0} | per pax {12,5:N0} fare/km {13:0.000} fuel/seat-km {14:0.0000}",
                grp.Key, r.Count, r.Average(x => x.Km), r.Average(x => x.Seats), r.Average(x => x.Passengers),
                r.Average(x => x.Tickets), r.Average(x => x.Fees), r.Average(x => x.Meals), r.Average(x => x.Fuel), r.Average(x => x.Landing), r.Average(x => x.Tax), r.Average(x => x.CabinWages),
                r.Sum(x => x.Tickets) / Math.Max(1, r.Sum(x => x.Passengers)), r.Sum(x => x.Tickets) / Math.Max(1, r.Sum(x => x.Passengers * x.Km)), r.Sum(x => x.Fuel) / Math.Max(1, r.Sum(x => x.Seats * x.Km))));
        }
        foreach (var grp in log.Where(r => r.Seats > 0).GroupBy(r => (r.Airline == me ? "YOU " : "") + r.Type).OrderByDescending(x => x.Count()).Take(6))
        {
            var r0 = grp.First();
            var f0 = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(a => a.Fleet).FirstOrDefault(f => f.Airliner.Type.Name == r0.Type);
            if (f0 == null) continue;
            double km = grp.Average(x => x.Km), h = grp.Average(x => x.Hours), t = grp.Average(x => x.Tickets);
            var pc = TheAirline.Helpers.FlightCosts.Calculate(f0.Airliner, km, h, t);
            double direct = grp.Average(x => x.Fuel + x.Landing + x.Tax + x.CabinWages - x.Meals);
            double seatKm = grp.Average(x => x.Seats * x.Km);
            Console.WriteLine(string.Format("  NEW {0,-26} ground {1,6:N0} maint {2,6:N0} enroute {3,6:N0} crew {4,5:N0} sales {5,6:N0} | old direct {6,7:N0} new total {7,7:N0} vs tickets {8,7:N0} | cost/seat-km old {9:0.000} new {10:0.000}",
                grp.Key, pc.Ground, pc.Maintenance, pc.EnRoute, pc.Crew, pc.Distribution, direct, direct + pc.Total, t, direct / seatKm, (direct + pc.Total) / seatKm));
        }
        foreach (var r in me.Routes.OfType<TheAirline.Models.Routes.PassengerRoute>())
        {
            var same = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(x => x.Routes).Where(x => x.HasAirliner && ((x.Destination1 == r.Destination1 && x.Destination2 == r.Destination2) || (x.Destination1 == r.Destination2 && x.Destination2 == r.Destination1))).ToList();
            var eco = TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass;
            double dem = r.Destination1.GetDestinationPassengersRate(r.Destination2, eco);
            Console.WriteLine(string.Format("  ROUTE {0}-{1}: fare {2:N0} market {3:N0} demand factor {4:0.00} competition {5:0.00} ({6} routes: {7}) | flights/week {8} | base demand {9} | score factor {10:0.00} | fill {11:0%}",
                r.Destination1.Profile.IATACode, r.Destination2.Profile.IATACode, r.GetFarePrice(eco), TheAirline.Helpers.PricingService.MarketFare(r.Destination1, r.Destination2, eco),
                TheAirline.Helpers.PricingService.DemandFactor(r, eco), TheAirline.Helpers.PricingService.CompetitionFactor(r, same, eco), same.Count, string.Join(",", same.Select(x => x.Airline.Profile.IATACode + " " + ((x as TheAirline.Models.Routes.PassengerRoute)?.GetFarePrice(eco) ?? 0).ToString("N0"))),
                same.Sum(x => x.TimeTable.Entries.Count), dem, TheAirline.Helpers.RouteHelpers.GetRouteTotalScore(r) / 5, TheAirline.Helpers.PricingService.RecentLoad(r)));
        }
        var last = g.GameTime.AddMonths(-1);
        Console.WriteLine("month " + last.ToString("MMM yyyy") + ", fuel price " + g.FuelPrice.ToString("0.000"));
        foreach (var a in new[] { me }.Concat(TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(x => !x.IsHuman && x.Routes.Count > 0).OrderByDescending(x => x.Fleet.Count).Take(3)))
        {
            Console.WriteLine("== " + a.Profile.Name + " fleet " + a.Fleet.Count + " routes " + a.Routes.Count);
            double inc = 0, exp = 0;
            foreach (TheAirline.Models.General.Finances.Invoice.InvoiceType t in Enum.GetValues(typeof(TheAirline.Models.General.Finances.Invoice.InvoiceType)))
            {
                if (t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Total) continue;
                double v = a.GetInvoicesAmountMonth(last.Year, last.Month, t);
                if (Math.Abs(v) < 1) continue;
                if (v > 0) inc += v; else exp += v;
                Console.WriteLine("   " + t.ToString().PadRight(18) + v.ToString("N0").PadLeft(16));
            }
            Console.WriteLine("   income " + inc.ToString("N0") + "  costs " + exp.ToString("N0") + "  margin " + (inc > 0 ? ((inc + exp) / inc * 100).ToString("0") + "%" : "-"));
            // unit measures from the aircraft statistics of the month (approximation over the year to date)
            double ask = 0, rpk = 0, flights = 0;
            foreach (var f in a.Fleet.ToList())
            {
                foreach (var r in f.Routes.ToList())
                {
                    double km = TheAirline.Helpers.MathHelpers.GetDistance(r.Destination1, r.Destination2);
                    var arr = TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Arrivals");
                    var pax = TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Passengers");
                    double fl = f.Statistics.GetStatisticsValue(g.GameTime.Year, arr) / Math.Max(1, f.Routes.Count);
                    double px = f.Statistics.GetStatisticsValue(g.GameTime.Year, pax) / Math.Max(1, f.Routes.Count);
                    flights += fl; ask += fl * f.Airliner.GetTotalSeatCapacity() * km; rpk += px * km;
                }
            }
            if (ask > 0)
            {
                double ytdInc = Enumerable.Range(1, g.GameTime.Month).Sum(m => a.GetInvoicesAmountMonth(g.GameTime.Year, m, TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets));
                double ytdCost = Enumerable.Range(1, g.GameTime.Month).Sum(m => Enum.GetValues(typeof(TheAirline.Models.General.Finances.Invoice.InvoiceType)).Cast<TheAirline.Models.General.Finances.Invoice.InvoiceType>().Where(t => t != TheAirline.Models.General.Finances.Invoice.InvoiceType.Total && t != TheAirline.Models.General.Finances.Invoice.InvoiceType.Purchases && t != TheAirline.Models.General.Finances.Invoice.InvoiceType.Loans).Sum(t => Math.Min(0, a.GetInvoicesAmountMonth(g.GameTime.Year, m, t))));
                Console.WriteLine(string.Format("   year to date: flights {0:N0}, load factor {1:0}%, ticket income per passenger-km {2:0.000}, operating cost per seat-km {3:0.000}", flights, rpk / ask * 100, ytdInc / rpk, -ytdCost / ask));
            }
        }
        Environment.Exit(0);
    }

    // short tests of the newest pages: run with "vm quick"
    static void QuickTests(AirlineViewModel av)
    {
        Console.WriteLine("AIRPORT TYPE TEST");
        var atm = av.AirportManager;
        var dom = atm.Airports.FirstOrDefault(x => x.Airport.Profile.Type == TheAirline.Models.Airports.AirportProfile.AirportType.Domestic) ?? atm.Airports.First();
        atm.Selected = dom;
        Console.WriteLine("  " + dom.Airport.Profile.IATACode + " before: " + atm.AirportType);
        atm.AirportType = atm.AirportTypes.First(x => x.Value == TheAirline.Models.Airports.AirportProfile.AirportType.ShortHaulInternational);
        Console.WriteLine("  after: " + dom.Airport.Profile.Type + " | " + atm.Status + " | selected " + atm.Selected?.Airport.Profile.IATACode);
        if (dom.Airport.Profile.Type != TheAirline.Models.Airports.AirportProfile.AirportType.ShortHaulInternational) { Console.WriteLine("  FAILED airport type"); Environment.Exit(1); }

        Console.WriteLine("DEMAND TEST");
        var dm = av.Demand;
        var atl = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "ATL");
        atm.Selected = atm.Airports.FirstOrDefault(x => x.Airport == atl) ?? atm.Selected;
        atm.ShowDemandCommand.Execute(null);
        Console.WriteLine("  tab " + av.AirportsTab + " | airport " + dm.Selected?.Airport.Profile.IATACode + " | rows " + dm.RowCount);
        Console.WriteLine("  " + dm.Summary.Replace(Environment.NewLine, " / "));
        foreach (var r in dm.Rows.Take(3)) Console.WriteLine("  " + r.Name + " | " + r.Numbers + " | " + r.Notes);
        if (dm.RowCount == 0 || av.AirportsTab != 1) { Console.WriteLine("  FAILED demand rows"); Environment.Exit(1); }
        dm.Filter = "International"; int intl = dm.RowCount; dm.Filter = "Domestic"; int domc = dm.RowCount; dm.Filter = "All";
        Console.WriteLine("  international " + intl + ", domestic " + domc + ", all " + dm.RowCount);
        dm.SortBy = "Distance"; Console.WriteLine("  nearest: " + dm.Rows.First().Name);
        dm.DestinationFilter = "LHR"; Console.WriteLine("  filter LHR: " + dm.RowCount + " " + dm.Rows.FirstOrDefault()?.Numbers); dm.DestinationFilter = "";
        dm.SortBy = "Passengers";
        var top = dm.Rows.First(); int before = top.Passengers;
        dm.SelectedRow = top; dm.ScalePercent = 200; dm.ScaleSelectedCommand.Execute(null);
        int engine = atl.GetDestinationPassengersRate(top.Destination, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
        Console.WriteLine("  scaled " + top.Destination.Profile.IATACode + ": " + before + " -> " + top.Passengers + " (game now " + engine + ") | " + dm.Status);
        if (top.Passengers != Math.Min(65535, before * 2) || engine != top.Passengers) { Console.WriteLine("  FAILED scale"); Environment.Exit(1); }
        dm.UndoCommand.Execute(null);
        Console.WriteLine("  undo: " + top.Passengers + " | " + dm.Status);
        if (top.Passengers != before) { Console.WriteLine("  FAILED undo"); Environment.Exit(1); }
        dm.OnlyWithoutRoute = true; dm.SelectedRow = dm.Rows.First(); dm.CreateCommand.Execute(null);
        Console.WriteLine("  create panel: " + dm.HasCreate + " " + dm.Create?.Title);
        if (!dm.HasCreate) { Console.WriteLine("  FAILED create"); Environment.Exit(1); }
        Console.WriteLine("RATINGS TEST");
        {
            var mk = av.Market;
            foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Take(200)) { mk.Selected = item; if (mk.Homebase != null && item.Airliner.Type.TypeAirliner == TheAirline.Models.Airliners.AirlinerType.TypeOfAirliner.Passenger) break; }
            mk.BuyCommand.Execute(null);
            Console.WriteLine("  buy: " + mk.Status);
            var ap = av.Airports;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (ap.IsBusy && sw.ElapsedMilliseconds < 120000) System.Threading.Thread.Sleep(200);
            var adv = ap.Advice.FirstOrDefault(x => x.Advice.FleetCanFly);
            if (adv != null)
            {
                ap.SelectedAdvice = adv;
                var cr = ap.Create;
                foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
                cr.CreateCommand.Execute(null);
                Console.WriteLine("  route: " + ap.Status);
            }
            TheAirline.Helpers.PilotSupply.TopUp(TheAirline.Models.General.GameObject.GetInstance().HumanAirline);
        }
        var rv = av.Ratings;
        rv.Reload();
        Console.WriteLine("  " + rv.Summary + " | " + rv.Rank);
        foreach (var r in rv.Ratings) Console.WriteLine("  " + r.Name + ": " + r.ValueText + " | " + r.Change + " | " + r.Heading + "\n    " + r.Reasons.Replace(Environment.NewLine, "\n    "));
        foreach (var c in rv.Compare.Take(4)) Console.WriteLine("  " + c.Rank + " " + c.Airline + " " + c.Overall + " | " + c.Detail);
        foreach (var st in rv.Stats) Console.WriteLine("  " + st.Name + ": " + st.ThisYear + " / " + st.LastYear + " " + st.Change);
        var me = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var ai = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(x => !x.IsHuman).Select(x => x.Ratings.CustomerHappinessRating).Distinct().Count();
        Console.WriteLine("  distinct AI customer ratings: " + ai + " | my factor " + TheAirline.Helpers.RatingsService.CustomerFactor(me));
        if (rv.Ratings.Count != 5 || !me.Ratings.IsCalculated || ai < 2) { Console.WriteLine("  FAILED ratings"); Environment.Exit(1); }
        // a month later with all wages cut: staff happiness should head down
        foreach (var w in TheAirline.Models.Airlines.FeeTypes.GetTypes(TheAirline.Models.Airlines.FeeType.EFeeType.Wage)) me.Fees.SetValue(w, w.MinValue);
        int emp0 = me.Ratings.EmployeeHappinessRating;
        TheAirline.Helpers.RatingsService.ProcessMonth(me); rv.Reload();
        var er = rv.Ratings.First(x => x.Name == "Employee happiness");
        Console.WriteLine("  wages cut: staff " + emp0 + " -> " + me.Ratings.EmployeeHappinessRating + " | " + er.Change + " | " + er.Heading);
        if (me.Ratings.EmployeeHappinessRating >= emp0) { Console.WriteLine("  FAILED wage effect"); Environment.Exit(1); }
        Console.WriteLine("PROFILE TEST");
        {
            var pv = av.Profile;
            var me2 = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            Console.WriteLine("  " + pv.Info + " | logo " + pv.LogoPath + " | header " + av.LogoPath + " | " + pv.LogoCount);
            if (pv.LogoPath == null || !System.IO.File.Exists(pv.LogoPath)) { Console.WriteLine("  FAILED no logo"); Environment.Exit(1); }
            pv.Name = "Lone Star Air"; pv.CEO = "Jose"; pv.Color = "1565c0"; pv.Narrative = "Texas first.";
            // the kind can't change once the airline flies: the rest saves and the note says why
            pv.Focus = pv.Focuses.First(f => f.Value == TheAirline.Models.Routes.Route.RouteType.Cargo);
            pv.SaveCommand.Execute(null);
            av.Refresh();
            Console.WriteLine("  save: " + pv.Status + " | now " + me2.Profile.Name + ", CEO " + me2.Profile.CEO + ", color " + me2.Profile.Color + ", focus " + me2.AirlineRouteFocus + " | header " + av.AirlineName);
            if (me2.Profile.Name != "Lone Star Air" || me2.Profile.Color != "#1565C0" || av.AirlineName != "Lone Star Air") { Console.WriteLine("  FAILED save"); Environment.Exit(1); }
            if ((me2.Routes.Count > 0 || me2.Fleet.Count > 0) && (me2.AirlineRouteFocus == TheAirline.Models.Routes.Route.RouteType.Cargo || !pv.Status.Contains("only change"))) { Console.WriteLine("  FAILED kind change allowed"); Environment.Exit(1); }
            var other = TheAirline.Models.Airlines.Airlines.GetAllAirlines().First(x => !x.IsHuman);
            pv.Name = other.Profile.Name; pv.SaveCommand.Execute(null); Console.WriteLine("  taken name: " + pv.Status);
            pv.Name = "Lone Star Air"; pv.Color = "not a color!"; pv.SaveCommand.Execute(null); Console.WriteLine("  bad color: " + pv.Status);
            pv.LogoFilter = "AA"; Console.WriteLine("  search AA: " + pv.LogoCount + " first " + pv.Logos.FirstOrDefault()?.Name);
            pv.SelectedLogo = pv.Logos.First(); pv.UseLogoCommand.Execute(null);
            Console.WriteLine("  built-in: " + pv.Status + " | " + me2.Profile.Logo);
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mylogo.png");
            System.IO.File.Copy(pv.Logos.Last().Path, tmp, true);
            pv.UseLogoFile(tmp);
            Console.WriteLine("  custom: " + pv.Status + " | " + me2.Profile.Logo + " exists " + System.IO.File.Exists(me2.Profile.Logo));
            if (!me2.Profile.Logo.Contains("logos") || !System.IO.File.Exists(me2.Profile.Logo)) { Console.WriteLine("  FAILED custom logo"); Environment.Exit(1); }
            pv.UseLogoFile("/nope.png"); Console.WriteLine("  missing file: " + pv.Status);
            pv.ResetLogoCommand.Execute(null); Console.WriteLine("  reset: " + me2.Profile.Logo);
            var al = av.AirlineList; Console.WriteLine("  airlines list logo: " + al.Airlines.First().LogoPath);
        }
        Console.WriteLine("CONTRACTS TEST");
        {
            var gc = av.GateContracts;
            gc.Reload();
            Console.WriteLine("  " + gc.Summary);
            foreach (var r in gc.Rows.Take(3)) Console.WriteLine("  " + r.Title + " | " + r.Numbers + " | " + r.Expiry);
            if (gc.Rows.Count == 0) { Console.WriteLine("  FAILED no contracts"); Environment.Exit(1); }
            gc.Selected = gc.Rows.First();
            var c = gc.Selected.Contract;
            var exp0 = c.ExpireDate; double pay0 = c.YearlyPayment;
            gc.Years = 5; Console.WriteLine("  extend preview: " + gc.ExtendText);
            gc.ExtendCommand.Execute(null); Console.WriteLine("  ask: " + gc.PendingText); gc.ConfirmCommand.Execute(null);
            Console.WriteLine("  extended: " + gc.Status + " | " + exp0.ToShortDateString() + " -> " + c.ExpireDate.ToShortDateString() + " | pay " + pay0.ToString("N0") + " -> " + c.YearlyPayment.ToString("N0"));
            if (c.ExpireDate != exp0.AddYears(5)) { Console.WriteLine("  FAILED extend"); Environment.Exit(1); }
            gc.Selected = gc.Rows.First(r => r.Contract == c);
            gc.AutoRenew = false; Console.WriteLine("  auto renew off: " + c.AutoRenew + " | " + gc.Status);
            gc.Filter = "Not renewed"; Console.WriteLine("  not renewed: " + gc.Rows.Count); gc.Filter = "All";
            gc.RenewAllCommand.Execute(null); Console.WriteLine("  renew all: " + gc.Status + " | " + c.AutoRenew);
            gc.Selected = gc.Rows.First(r => r.Contract == c);
            int g0 = c.NumberOfGates; int free0 = c.Airport.Terminals.GetFreeGates(c.TerminalType);
            gc.Gates = 2; Console.WriteLine("  gates: " + gc.GatesText);
            gc.AddGatesCommand.Execute(null); gc.ConfirmCommand.Execute(null);
            Console.WriteLine("  add: " + gc.Status + " | gates " + g0 + " -> " + c.NumberOfGates + " | free " + free0 + " -> " + c.Airport.Terminals.GetFreeGates(c.TerminalType));
            if (c.NumberOfGates != g0 + 2) { Console.WriteLine("  FAILED add gates"); Environment.Exit(1); }
            gc.Selected = gc.Rows.First(r => r.Contract == c);
            gc.Gates = 1; gc.RemoveGatesCommand.Execute(null); gc.ConfirmCommand.Execute(null);
            Console.WriteLine("  give back: " + gc.Status + " | gates " + c.NumberOfGates);
            gc.Selected = gc.Rows.First(r => r.Contract == c);
            gc.EndCommand.Execute(null); Console.WriteLine("  end: " + (gc.PendingText ?? gc.Status)); gc.CancelCommand.Execute(null);
            gc.OpenAirportCommand.Execute(null); Console.WriteLine("  open airport: tab " + av.AirportsTab + " selected " + av.AirportManager.Selected?.Airport.Profile.IATACode);
            // manage tab: lease with chosen gates and years
            var m = av.AirportManager;
            var target = m.Airports.FirstOrDefault(x => x.Airport.Terminals.GetFreeGates(TheAirline.Models.Airports.Terminal.TerminalType.Passenger) > 5 && !x.Airport.AirlineContracts.Any(k => k.Airline.IsHuman));
            if (target != null)
            {
                m.Selected = target; m.Gates = m.GateChoices.Contains(4) ? 4 : m.GateChoices.Last(); m.LeaseYears = 10;
                Console.WriteLine("  lease price: " + m.PriceText);
                m.LeaseCommand.Execute(null);
                var nc = target.Airport.AirlineContracts.First(k => k.Airline.IsHuman);
                Console.WriteLine("  lease: " + m.Status + " | contract gates " + nc.NumberOfGates + " years " + nc.Length);
                if (nc.Length != 10) { Console.WriteLine("  FAILED lease length"); Environment.Exit(1); }
            }
        }
        Console.WriteLine("CODESHARE TEST");
        {
            var cs = av.Codeshares;
            var me3 = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            cs.Reload();
            Console.WriteLine("  " + cs.Summary + " | candidates " + cs.Candidates.Count + " | offers " + cs.Offers.Count);
            foreach (var c in cs.Candidates.Take(4)) Console.WriteLine("  " + c.Name + " | " + c.Numbers);
            bool agreed = false;
            foreach (var c in cs.Candidates.ToList())
            {
                cs.Selected = cs.Candidates.First(x => x.Airline == c.Airline);
                Console.WriteLine("  ask " + c.Airline.Profile.IATACode + ": " + cs.Evaluation.Replace(Environment.NewLine, " / "));
                cs.AskBothCommand.Execute(null); cs.ConfirmCommand.Execute(null);
                Console.WriteLine("    -> " + cs.Status);
                if (me3.Codeshares.Any()) { agreed = true; break; }
            }
            cs.Selected = cs.Candidates.FirstOrDefault();
            if (cs.Selected != null) { cs.AskBothCommand.Execute(null); Console.WriteLine("  ask again: " + (cs.PendingText ?? cs.Status)); cs.CancelCommand.Execute(null); }
            // an offer from a computer airline
            var asker = TheAirline.Models.Airlines.Airlines.GetAllAirlines().First(x => !x.IsHuman && TheAirline.Helpers.CodeshareService.Find(me3, x) == null);
            TheAirline.Helpers.CodeshareService.AddOffer(me3, asker);
            cs.Reload();
            Console.WriteLine("  offer: " + cs.Offers.FirstOrDefault()?.Name + " | " + cs.Offers.FirstOrDefault()?.Numbers);
            if (cs.Offers.Count != 1) { Console.WriteLine("  FAILED offer"); Environment.Exit(1); }
            cs.Offers[0].AcceptCommand.Execute(null);
            Console.WriteLine("  accept: " + cs.Status + " | partners " + cs.Partners.Count + " | " + cs.Summary);
            if (TheAirline.Helpers.CodeshareService.Find(me3, asker) == null || asker.Codeshares.Count(x => x.Airline1 == me3 || x.Airline2 == me3) != 1) { Console.WriteLine("  FAILED accept"); Environment.Exit(1); }
            foreach (var p in cs.Partners) Console.WriteLine("  partner " + p.Name + " | " + p.Kind + " | " + p.Numbers);
            var row = cs.Partners.First(x => TheAirline.Helpers.CodeshareService.Partner(x.Agreement, me3) == asker);
            row.EndCommand.Execute(null); cs.ConfirmCommand.Execute(null);
            Console.WriteLine("  end: " + cs.Status + " | still partners " + (TheAirline.Helpers.CodeshareService.Find(me3, asker) != null) + " | agreed by asking " + agreed);
            if (TheAirline.Helpers.CodeshareService.Find(me3, asker) != null) { Console.WriteLine("  FAILED end"); Environment.Exit(1); }
        }
        Console.WriteLine("AIRPORT STATS TEST");
        {
            var st = av.AirportStats;
            var mgr = av.AirportManager;
            var atl3 = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "ATL");
            mgr.SelectAirport(atl3);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            mgr.ShowStatsCommand.Execute(null);
            Console.WriteLine("  tab " + av.AirportsTab + " | " + st.Selected?.Airport.Profile.IATACode + " | load " + sw.ElapsedMilliseconds + " ms");
            Console.WriteLine("  " + st.Summary.Replace(Environment.NewLine, " / "));
            foreach (var r in st.AirlineRows.Take(4)) Console.WriteLine("  " + r.Name + " | " + r.Numbers);
            foreach (var d in st.Destinations.Take(3)) Console.WriteLine("  dest " + d);
            foreach (var g in st.GateLines) Console.WriteLine("  gates " + g);
            string busiest = null; int most = -1;
            foreach (var dn in st.Days) { st.Day = dn; int n = st.Movements.Count; if (n > most) { most = n; busiest = dn; } }
            st.Day = busiest;
            Console.WriteLine("  " + st.SchedulePeak);
            foreach (var h in st.Hours.Where(h => h.Count > 0).Take(5)) Console.WriteLine("  " + h.Hour + " " + h.Label + " width " + h.AllWidth.ToString("0"));
            foreach (var mv in st.Movements.Take(6)) Console.WriteLine("  " + mv.Marker + mv.Time + " " + mv.Text);
            st.OnlyMine = true; Console.WriteLine("  only mine: " + st.Movements.Count);
            if (av.AirportsTab != 3 || st.AirlineRows.Count == 0 || st.GateLines.Count == 0) { Console.WriteLine("  FAILED stats"); Environment.Exit(1); }
        }
        Console.WriteLine("FLEET HISTORY TEST");
        {
            var me6 = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            foreach (var f6 in me6.Fleet.ToList())
            {
                if (f6.Pilots.Count < f6.Airliner.Type.CockpitCrew) { TheAirline.Helpers.PilotSupply.EnsureFamily(f6.Airliner.Type.AirlinerFamily); TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f6); }
                if (f6.HasRoute) Console.WriteLine("  start " + f6.Name + ": " + TheAirline.Presentation.FleetOverviewViewModel.TryStart(f6));
            }
            var sw6 = System.Diagnostics.Stopwatch.StartNew();
            for (int d = 0; d < 70; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            Console.WriteLine("  70 days in " + sw6.ElapsedMilliseconds + " ms, now " + TheAirline.Models.General.GameObject.GetInstance().GameTime.ToShortDateString());
            var fp = av.FleetPerformance;
            fp.Reload();
            Console.WriteLine("  " + fp.Summary);
            foreach (var r in fp.Rows) Console.WriteLine("  " + r.Name + " | " + r.Numbers);
            fp.Selected = fp.Rows.FirstOrDefault(r => r.Aircraft.HasRoute) ?? fp.Rows.FirstOrDefault();
            Console.WriteLine("  " + fp.Detail.Replace(Environment.NewLine, " / "));
            foreach (var y in fp.YearLines) Console.WriteLine("  year " + y);
            foreach (var m in fp.Months.Take(3)) Console.WriteLine("  " + m.Month + " in " + m.Income + " out " + m.Costs + " = " + m.Profit + " | " + m.Detail);
            if (fp.Months.Count == 0 || !fp.Months.Any(m => m.Detail.Contains("Airport fees"))) { Console.WriteLine("  FAILED history"); Environment.Exit(1); }
            // the same month of two years must stay apart
            var od = new TheAirline.Models.General.OperatingData();
            od.AddOperatingValue(new TheAirline.Models.General.OperatingValue("Tickets", 2007, 3, 10));
            od.AddOperatingValue(new TheAirline.Models.General.OperatingValue("Tickets", 2008, 3, 5));
            Console.WriteLine("  years kept apart: " + od.Values.Count);
            if (od.Values.Count != 2) { Console.WriteLine("  FAILED year bug"); Environment.Exit(1); }

            var ct = av.CompareTypes;
            var nav = av.NewAircraft;
            nav.Selected = nav.Types.FirstOrDefault(t => t.Type.Name.Contains("737")) ?? nav.Types.FirstOrDefault();
            nav.CompareCommand.Execute(null);
            Console.WriteLine("  compare from new aircraft: tab " + av.AirlinersTab + " columns " + ct.Columns.Count);
            ct.Search = "A320"; ct.SelectedChoice = ct.Choices.FirstOrDefault(); ct.AddCommand.Execute(null);
            ct.Search = "747"; ct.SelectedChoice = ct.Choices.FirstOrDefault(); ct.AddCommand.Execute(null);
            ct.Route = "ATL-LAX";
            Console.WriteLine("  " + ct.DistanceText);
            for (int i = 0; i < ct.Labels.Count; i++) Console.WriteLine("  " + ct.Labels[i].PadRight(22) + string.Join(" | ", ct.Columns.Select(c => c.Values[i].PadRight(24))));
            if (ct.Columns.Count < 2) { Console.WriteLine("  FAILED compare"); Environment.Exit(1); }
            ct.Columns[0].RemoveCommand.Execute(null); Console.WriteLine("  removed one: " + ct.Columns.Count);
        }
        Console.WriteLine("CONNECTIONS TEST");
        {
            var meK = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            Console.WriteLine("  routes: " + string.Join(", ", meK.Routes.Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode + (r.HasAirliner ? "" : " (idle)"))));
            av.Page = Array.IndexOf(AirlineViewModel.PageNames, "Routes");
            var cv = av.Connections;
            Console.WriteLine("  " + cv.Summary);
            foreach (var row in cv.Rows) Console.WriteLine("  " + row.Name + " | " + row.Numbers);
            foreach (var row in cv.Rows.ToList())
            {
                cv.Selected = row;
                Console.WriteLine("  " + row.Name + ": " + cv.RouteSummary);
                foreach (var m in cv.Markets.Take(4)) Console.WriteLine("    " + m.Journey + " | " + m.Details);
                // the page and the flight engine must agree
                double engineK = 0;
                foreach (TheAirline.Models.Airliners.AirlinerClass.ClassType t in Enum.GetValues(typeof(TheAirline.Models.Airliners.AirlinerClass.ClassType)))
                {
                    engineK += TheAirline.Helpers.ConnectionService.FlightDemand(meK, row.Route, row.Route.Destination1, row.Route.Destination2, t);
                    engineK += TheAirline.Helpers.ConnectionService.FlightDemand(meK, row.Route, row.Route.Destination2, row.Route.Destination1, t);
                }
                double page = cv.Markets.Sum(m => m.Market.Added);
                if (Math.Abs(engineK - page) > 0.01) { Console.WriteLine("  FAILED engine " + engineK + " page " + page); Environment.Exit(1); }
            }
            Console.WriteLine("  hubs: " + string.Join(", ", cv.Hubs.Select(h => h.Airport.Profile.IATACode)));
            Console.WriteLine("  " + cv.IdeasSummary);
            foreach (var i in cv.Ideas.Take(5)) Console.WriteLine("    " + i.Name + " | " + i.Numbers);
            if (cv.Ideas.Count == 0) { Console.WriteLine("  FAILED ideas"); Environment.Exit(1); }
            cv.Ideas[0].PlanCommand.Execute(null);
            Console.WriteLine("  plan: tab " + av.RoutesTab + ", form " + (av.Airports.HasCreate ? av.Airports.Create.Title : "none"));
            if (av.RoutesTab != 1 || !av.Airports.HasCreate) { Console.WriteLine("  FAILED plan"); Environment.Exit(1); }
            av.Airports.Create.CancelCommand.Execute(null); av.RoutesTab = 0;
            // a feeder with no aircraft doesn't count yet
            var lgaK = meK.Routes[0].Destination1; // the feeder starts at one end of the route
            var ordK = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "ORD");
            var idle = new TheAirline.Models.Routes.PassengerRoute("idleK", lgaK, ordK, TheAirline.Models.General.GameObject.GetInstance().GameTime, 100);
            meK.AddRoute(idle);
            cv.Reload(); cv.Selected = cv.Rows.First(x => x.Route == meK.Routes[0]);
            Console.WriteLine("  with idle LGA-ORD: " + cv.RouteSummary + " | " + string.Join(" / ", cv.Markets.Select(m => m.Journey + ": " + m.Details)));
            if (!cv.Markets.Any(m => !m.Counted && m.Details.Contains("no aircraft"))) { Console.WriteLine("  FAILED idle feeder"); Environment.Exit(1); }
            meK.RemoveRoute(idle);
            // counting rolls over each month
            var gK = TheAirline.Models.General.GameObject.GetInstance();
            var t0 = gK.GameTime;
            TheAirline.Helpers.ConnectionService.Record(idle, 100, 30);
            TheAirline.Helpers.ConnectionService.Record(idle, 50, 10);
            gK.GameTime = t0.AddMonths(1);
            int lc, la; TheAirline.Helpers.ConnectionService.LastMonth(idle, out lc, out la);
            TheAirline.Helpers.ConnectionService.Record(idle, 20, 5);
            int lc2, la2; TheAirline.Helpers.ConnectionService.LastMonth(idle, out lc2, out la2);
            int tc, ta; TheAirline.Helpers.ConnectionService.ThisMonth(idle, out tc, out ta);
            gK.GameTime = t0.AddMonths(3);
            int lc3, la3; TheAirline.Helpers.ConnectionService.LastMonth(idle, out lc3, out la3);
            gK.GameTime = t0;
            Console.WriteLine("  counter: last " + lc + "/" + la + ", after a flight " + lc2 + "/" + la2 + ", this " + tc + "/" + ta + ", two months on " + lc3 + "/" + la3);
            if (lc != 40 || la != 150 || lc2 != 40 || tc != 5 || ta != 20 || la3 != 0) { Console.WriteLine("  FAILED counter"); Environment.Exit(1); }
            int conn = meK.Routes.Sum(r => r.ConnectPassengers + r.ConnectLastPassengers), all = meK.Routes.Sum(r => r.ConnectAllPassengers + r.ConnectLastAllPassengers);
            Console.WriteLine("  counted: " + conn + " connecting of " + all);
            if (all == 0) { Console.WriteLine("  FAILED counting"); Environment.Exit(1); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) TheAirline.Helpers.ConnectionService.FlightDemand(meK, meK.Routes[0], meK.Routes[0].Destination1, meK.Routes[0].Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
            Console.WriteLine("  2000 flight lookups in " + sw.ElapsedMilliseconds + " ms");
        }
        Console.WriteLine("AI PRICING TEST");
        {
            var M = TheAirline.Models.Airlines.Airline.AirlineMentality.Moderate;
            var S = TheAirline.Models.Airlines.Airline.AirlineMentality.Safe;
            var A = TheAirline.Models.Airlines.Airline.AirlineMentality.Aggressive;
            Func<double, double, double?, TheAirline.Models.Airlines.Airline.AirlineMentality, double, double> D = TheAirline.Helpers.AIPricingService.Decide;
            Console.WriteLine("  full 95%: " + D(1.0, 0.95, null, M, 0.5).ToString("0.000") + " | 85%: " + D(1.0, 0.85, null, M, 0.5).ToString("0.000") + " | 70%: " + D(1.0, 0.7, null, M, 0.5).ToString("0.000") + " | 50%: " + D(1.0, 0.5, null, M, 0.5).ToString("0.000") + " | 30%: " + D(1.0, 0.3, null, M, 0.5).ToString("0.000"));
            Console.WriteLine("  break-even 0.97 at 30% full: " + D(1.0, 0.3, null, M, 0.97).ToString("0.000") + " | break-even above the band: " + D(1.4, 0.3, null, M, 2.0).ToString("0.000"));
            Console.WriteLine("  rival at 0.80, 75% full: careful " + D(1.0, 0.75, 0.8, S, 0.5).ToString("0.000") + ", balanced " + D(1.0, 0.75, 0.8, M, 0.5).ToString("0.000") + ", fast-growing " + D(1.0, 0.75, 0.8, A, 0.5).ToString("0.000") + " | rival at 0.80 but we're 95% full: " + D(1.0, 0.95, 0.8, M, 0.5).ToString("0.000"));
            Console.WriteLine("  rival at 1.30, 85% full: " + D(1.0, 0.85, 1.3, M, 0.5).ToString("0.000") + " | top of band at 99%: careful " + D(1.44, 0.99, null, S, 0.5).ToString("0.000") + ", balanced " + D(1.44, 0.99, null, M, 0.5).ToString("0.000") + ", fast-growing " + D(1.44, 0.99, null, A, 0.5).ToString("0.000"));
            Func<TheAirline.Models.Airlines.Airline.AirlineMentality, double> six = m => { double r = 1.0; for (int i = 0; i < 6; i++) r = D(r, 0.75, 0.8, m, 0.5); return r; };
            Console.WriteLine("  after six reviews: careful " + six(S).ToString("0.000") + ", balanced " + six(M).ToString("0.000") + ", fast-growing " + six(A).ToString("0.000"));
            if (!(six(A) < six(M) && six(M) < six(S) && six(S) >= 0.85 - 1e-9)) { Console.WriteLine("  FAILED mentality"); Environment.Exit(1); }
            if (!(D(1.0, 0.95, null, M, 0.5) > 1.0 && D(1.0, 0.3, null, M, 0.5) < 1.0 && D(1.0, 0.3, null, M, 0.97) >= 0.97 && D(1.0, 0.3, null, M, 0.5) >= 0.9 - 1e-9
                  && D(1.0, 0.75, 0.8, M, 0.5) < 1.0
                  && D(1.0, 0.95, 0.8, M, 0.5) >= 1.0 && D(1.0, 0.85, 1.3, M, 0.5) > 1.03 && D(1.44, 0.99, null, M, 0.5) <= 1.35 + 1e-9))
            { Console.WriteLine("  FAILED decide"); Environment.Exit(1); }

            // what the computer airlines did in the weeks simulated so far
            var gP = TheAirline.Models.General.GameObject.GetInstance();
            var aiR = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes).OfType<TheAirline.Models.Routes.PassengerRoute>().Where(r => r.HasAirliner).ToList();
            var ratios = aiR.Select(r => TheAirline.Helpers.AIPricingService.EconomyFare(r) / TheAirline.Helpers.PricingService.MarketFare(r.Destination1, r.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass)).ToList();
            Console.WriteLine("  " + aiR.Count + " flying computer routes; fare / market: below 0.9 " + ratios.Count(x => x < 0.9) + ", 0.9-1.1 " + ratios.Count(x => x >= 0.9 && x <= 1.1) + ", above 1.1 " + ratios.Count(x => x > 1.1) + " | average " + (ratios.Count > 0 ? ratios.Average() : 0).ToString("0.00"));
            var loads = aiR.Select(r => TheAirline.Helpers.ConnectionService.MonthLoad(r)).Where(x => x > 0).ToList();
            Console.WriteLine("  their recent load: average " + (loads.Count > 0 ? loads.Average() * 100 : 0).ToString("0") + "% over " + loads.Count + " routes");

            // a rival undercuts: the other airline answers at its next review
            var byPair = aiR.GroupBy(r => string.Join("|", new[] { r.Destination1.Profile.IATACode, r.Destination2.Profile.IATACode }.OrderBy(x => x))).FirstOrDefault(g => g.Select(r => r.Airline).Distinct().Count() >= 2);
            if (byPair == null) Console.WriteLine("  no shared computer route to test the rival answer");
            else
            {
                var list = byPair.ToList();
                var cheap = list[0]; var other = list.First(r => r.Airline != cheap.Airline);
                double market = TheAirline.Helpers.PricingService.MarketFare(other.Destination1, other.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
                foreach (var c in other.Classes) c.FarePrice = market * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
                foreach (var c in cheap.Classes) c.FarePrice = market * 0.75 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
                // 70% full last month
                int key = gP.GameTime.Year * 12 + gP.GameTime.Month - 1;
                other.ConnectMonth = key; other.ConnectLastAllPassengers = 700; other.ConnectLastSeats = 1000;
                TheAirline.Helpers.AIPricingService.Invalidate(); TheAirline.Helpers.AIPricingService.ForgetReviews();
                Console.WriteLine("  " + other.Airline.Profile.Name + " (" + other.Airline.Mentality + ") vs " + cheap.Airline.Profile.Name + " at 75% of market: " + TheAirline.Helpers.AIPricingService.Review(other.Airline, other));
                Console.WriteLine("  again at once: " + (TheAirline.Helpers.AIPricingService.Review(other.Airline, other) ?? "no review (a week has not passed)"));
                if (TheAirline.Helpers.AIPricingService.EconomyFare(other) >= market * 0.995) { Console.WriteLine("  FAILED rival answer"); Environment.Exit(1); }
            }

            // the route page lists the rivals
            var mine = gP.HumanAirline.Routes.OfType<TheAirline.Models.Routes.PassengerRoute>().First();
            var rdP = new TheAirline.Presentation.RouteDetailViewModel(mine, () => { });
            Console.WriteLine("  route page: " + rdP.PricingText.Replace(Environment.NewLine, " / "));

            // a rival cuts its fare on the player's route: the player hears about it, once a month at most
            // (two computer airlines share a route; for this check one of them stands in for the player)
            if (byPair != null)
            {
                var player = gP.HumanAirline;
                var list2 = byPair.ToList();
                var mineN = list2[0]; var rivalP = list2.First(r => r.Airline != mineN.Airline);
                gP.SetHumanAirline(mineN.Airline);
                double mk = TheAirline.Helpers.PricingService.MarketFare(mineN.Destination1, mineN.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
                foreach (var c in mineN.Classes) c.FarePrice = mk * 0.75 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
                foreach (var c in rivalP.Classes) c.FarePrice = mk * 1.1 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
                int keyP = gP.GameTime.Year * 12 + gP.GameTime.Month - 1;
                rivalP.ConnectMonth = keyP; rivalP.ConnectLastAllPassengers = 550; rivalP.ConnectLastSeats = 1000;
                TheAirline.Helpers.AIPricingService.ForgetReviews();
                int newsBefore = gP.NewsBox.GetNews().Count;
                Console.WriteLine("  rival on your route: " + TheAirline.Helpers.AIPricingService.Review(rivalP.Airline, rivalP));
                var n = gP.NewsBox.GetNews().LastOrDefault();
                Console.WriteLine("  news: " + (n == null ? "none" : n.Subject + ": " + n.Body));
                bool okNews = gP.NewsBox.GetNews().Count == newsBefore + 1 && n.Subject.Contains("cuts fares");
                TheAirline.Helpers.AIPricingService.ForgetReviews();
                TheAirline.Helpers.AIPricingService.Review(rivalP.Airline, rivalP);
                Console.WriteLine("  second cut the same month: news " + (gP.NewsBox.GetNews().Count - newsBefore));
                bool okFlood = gP.NewsBox.GetNews().Count == newsBefore + 1;
                gP.SetHumanAirline(player);
                if (!okNews) { Console.WriteLine("  FAILED fare news"); Environment.Exit(1); }
                if (!okFlood) { Console.WriteLine("  FAILED news flood"); Environment.Exit(1); }
            }
        }
        Console.WriteLine("CARGO PRICING TEST");
        {
            var gK = TheAirline.Models.General.GameObject.GetInstance();
            foreach (double km in new[] { 500.0, 1500, 3000, 6000 })
            {
                gK.CostLevel = 0; double origRate = TheAirline.Helpers.PricingService.CargoMarketRate(km); gK.CostLevel = 1;
                Console.WriteLine("  market cargo rate " + km + " km: " + TheAirline.Helpers.PricingService.CargoMarketRate(km).ToString("N2") + " (original " + origRate.ToString("N2") + ")");
            }
            foreach (double r in new[] { 0.7, 1.0, 1.3, 2.0 }) Console.Write("  rate x" + r + ": demand " + TheAirline.Helpers.PricingService.CargoDemandFactor(r * 100, 100).ToString("0.00"));
            Console.WriteLine();
            if (TheAirline.Helpers.PricingService.CargoDemandFactor(70, 100) <= 1 || TheAirline.Helpers.PricingService.CargoDemandFactor(130, 100) >= 1) { Console.WriteLine("  FAILED cargo demand"); Environment.Exit(1); }
            var cr = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes).OfType<TheAirline.Models.Routes.CargoRoute>().FirstOrDefault(r => r.HasAirliner);
            if (cr == null) Console.WriteLine("  no flying computer cargo route to test");
            else
            {
                double mk = TheAirline.Helpers.PricingService.CargoMarketRate(cr.Destination1, cr.Destination2);
                Console.WriteLine("  " + cr.Airline.Profile.Name + " " + cr.Destination1.Profile.IATACode + "-" + cr.Destination2.Profile.IATACode + ": rate " + cr.PricePerUnit.ToString("N2") + ", market " + mk.ToString("N2") + ", recent load " + (TheAirline.Helpers.ConnectionService.MonthLoad(cr) * 100).ToString("0") + "%, break-even at 75% " + TheAirline.Helpers.PricingService.CargoBreakEvenRate(cr, 0.75).ToString("N2"));
                if (TheAirline.Helpers.ConnectionService.MonthLoad(cr) <= 0) { Console.WriteLine("  FAILED cargo load counting"); Environment.Exit(1); }
                // half empty at twice the market rate: the airline cuts
                cr.PricePerUnit = mk * 2;
                int key = gK.GameTime.Year * 12 + gK.GameTime.Month - 1;
                cr.ConnectMonth = key; cr.ConnectLastAllPassengers = 400; cr.ConnectLastSeats = 1000;
                TheAirline.Helpers.AIPricingService.ForgetReviews();
                Console.WriteLine("  40% full at 2x market: " + TheAirline.Helpers.AIPricingService.ReviewCargo(cr.Airline, cr));
                if (cr.PricePerUnit >= mk * 2) { Console.WriteLine("  FAILED cargo review"); Environment.Exit(1); }
                var rdK = new TheAirline.Presentation.RouteDetailViewModel(cr, () => { });
                Console.WriteLine("  route page: " + rdK.CargoMarketText + " | " + rdK.CargoEffectText);
                rdK.CargoMarketCommand.Execute(null); Console.WriteLine("  market button: " + rdK.CargoPrice + " | " + rdK.CargoEffectText);
                Console.WriteLine("  " + rdK.PricingText.Replace(Environment.NewLine, " / "));
                if (string.IsNullOrEmpty(rdK.PricingText) || !rdK.PricingText.Contains("Break-even")) { Console.WriteLine("  FAILED cargo route page"); Environment.Exit(1); }
            }
        }
        Console.WriteLine("PRICING TEST");
        {
            var g10 = TheAirline.Models.General.GameObject.GetInstance();
            var me10 = g10.HumanAirline;
            foreach (double km in new[] { 400.0, 1200, 3000, 7000 }) Console.WriteLine("  market fare " + km + " km: " + TheAirline.Helpers.PricingService.MarketFare(km).ToString("N0"));
            foreach (double r in new[] { 0.7, 1.0, 1.3, 2.0, 5.0 })
                Console.WriteLine("  fare x" + r + ": economy " + TheAirline.Helpers.PricingService.DemandFactor(r * 100, 100, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass).ToString("0.00") + ", business " + TheAirline.Helpers.PricingService.DemandFactor(r * 100, 100, TheAirline.Models.Airliners.AirlinerClass.ClassType.BusinessClass).ToString("0.00") + ", first " + TheAirline.Helpers.PricingService.DemandFactor(r * 100, 100, TheAirline.Models.Airliners.AirlinerClass.ClassType.FirstClass).ToString("0.00"));
            var route = me10.Routes.OfType<TheAirline.Models.Routes.PassengerRoute>().First();
            var rd = new TheAirline.Presentation.RouteDetailViewModel(route, () => { });
            Console.WriteLine("  route " + rd.Code + ":");
            Console.WriteLine("    " + rd.PricingText.Replace(Environment.NewLine, Environment.NewLine + "    "));
            foreach (var fi in rd.Fares) Console.WriteLine("    " + fi.ClassName + " " + fi.Price + " | " + fi.MarketText + " | " + fi.EffectText);
            var eco = rd.Fares.First();
            eco.UpCommand.Execute(null); Console.WriteLine("    +10%: " + eco.Price + " | " + eco.EffectText);
            eco.MarketCommand.Execute(null); Console.WriteLine("    market: " + eco.Price + " | " + eco.EffectText);
            eco.Price = (eco.Market * 3).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); Console.WriteLine("    x3: " + eco.EffectText);
            if (TheAirline.Helpers.PricingService.DemandFactor(300, 100, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass) > 0.3) { Console.WriteLine("  FAILED elasticity"); Environment.Exit(1); }
            g10.CostLevel = 0;
            Console.WriteLine("  original costs: fare 1200 km " + TheAirline.Helpers.PricingService.MarketFare(1200).ToString("N0") + ", demand at x3 " + TheAirline.Helpers.PricingService.DemandFactor(300, 100, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass));
            g10.CostLevel = 1;
        }
        Console.WriteLine("SHARES TEST");
        {
            var me7 = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            me7.Money += 300000000;
            var sh = av.Shares;
            sh.Reload();
            Console.WriteLine("  " + sh.MyText);
            sh.IssueCount = 2000; Console.WriteLine("  " + sh.IssueText);
            double m0 = me7.Money; sh.IssueCommand.Execute(null); sh.ConfirmCommand.Execute(null);
            Console.WriteLine("  issue: " + sh.Status + " | cash +" + (me7.Money - m0).ToString("N0"));
            sh.IssueCount = 10000; Console.WriteLine("  too many: " + sh.IssueText);
            sh.BuybackCount = 1000; sh.BuyBackCommand.Execute(null); sh.ConfirmCommand.Execute(null); Console.WriteLine("  buy back: " + sh.Status);
            foreach (var r in sh.Market.Take(4)) Console.WriteLine("  market " + r.Name + " | " + r.Numbers);
            var cheap = sh.Market.OrderBy(r => TheAirline.Helpers.SharesService.Value(r.Airline)).ToList();
            sh.Selected = cheap[0];
            var t1 = sh.Selected.Airline;
            sh.BuyCount = 1000; Console.WriteLine("  " + sh.SelectedText.Replace(Environment.NewLine, " / "));
            sh.BuyCommand.Execute(null); sh.ConfirmCommand.Execute(null); Console.WriteLine("  buy: " + sh.Status);
            Console.WriteLine("  " + sh.PortfolioText + " | " + string.Join(" ; ", sh.PortfolioRows.Select(r => r.Text)));
            sh.SelectedHolding = sh.PortfolioRows.First(); sh.SellCount = 500; sh.SellCommand.Execute(null); sh.ConfirmCommand.Execute(null);
            Console.WriteLine("  sell: " + sh.Status + " | own " + TheAirline.Helpers.SharesService.Owned(t1, me7));
            if (TheAirline.Helpers.SharesService.Owned(t1, me7) != 500) { Console.WriteLine("  FAILED shares"); Environment.Exit(1); }

            // takeover as subsidiary, and a merge of another airline
            int fleetBefore = me7.Fleet.Count, routesBefore = me7.Routes.Count;
            var t2 = cheap[1].Airline;
            sh.Selected = sh.Market.First(r => r.Airline == t2);
            sh.MergeCommand.Execute(null); Console.WriteLine("  ask: " + sh.PendingText); sh.ConfirmCommand.Execute(null);
            Console.WriteLine("  merge: " + sh.Status + " | fleet " + fleetBefore + " -> " + me7.Fleet.Count + ", routes " + routesBefore + " -> " + me7.Routes.Count + " | still listed " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Contains(t2));
            if (TheAirline.Models.Airlines.Airlines.GetAllAirlines().Contains(t2)) { Console.WriteLine("  FAILED merge"); Environment.Exit(1); }
            sh.Selected = sh.Market.First(r => r.Airline == t1);
            sh.SubsidiaryCommand.Execute(null); sh.ConfirmCommand.Execute(null);
            Console.WriteLine("  as subsidiary: " + sh.Status + " | subs " + me7.Subsidiaries.Count + " | sub is human " + me7.Subsidiaries[0].IsHuman);

            var sb = av.Subsidiaries;
            sb.Reload();
            Console.WriteLine("  " + sb.Summary + " | " + string.Join(" ; ", sb.Rows.Select(r => r.Name + " " + r.Numbers)));
            sb.NewName = "Lone Star Express"; sb.NewCode = "LZ"; sb.NewMoney = "40000000";
            sb.CreateCommand.Execute(null); Console.WriteLine("  ask: " + sb.PendingText); sb.ConfirmCommand.Execute(null);
            Console.WriteLine("  create: " + sb.Status);
            sb.NewName = "Other"; sb.NewCode = "LZ"; sb.CreateCommand.Execute(null); Console.WriteLine("  same code: " + sb.Status);
            var lz = me7.Subsidiaries.FirstOrDefault(x => x.Profile.IATACode == "LZ");
            if (lz == null) { Console.WriteLine("  FAILED create"); Environment.Exit(1); }
            sb.Selected = sb.Rows.First(r => r.Airline == lz); sb.Transfer = "5000000";
            sb.FromSubCommand.Execute(null); sb.ConfirmCommand.Execute(null); Console.WriteLine("  take out: " + sb.Status + " | sub cash " + lz.Money.ToString("N0"));
            for (int d = 0; d < 60; d++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            sb.Reload();
            foreach (var r in sb.Rows) Console.WriteLine("  after 60 days: " + r.Name + " | " + r.Numbers);
            var t1sub = me7.Subsidiaries.First(x => x != lz);
            sb.Selected = sb.Rows.First(r => r.Airline == t1sub);
            sb.FoldCommand.Execute(null); sb.ConfirmCommand.Execute(null); Console.WriteLine("  fold: " + sb.Status + " | subs " + me7.Subsidiaries.Count);
        }
        Console.WriteLine("FLIGHT COSTS TEST");
        {
            var g9 = TheAirline.Models.General.GameObject.GetInstance();
            var me9 = g9.HumanAirline;
            var t9 = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(x => x.Name == "Airbus A320-200");
            var a9 = new TheAirline.Models.Airliners.Airliner("t9", t9, "N-T9", g9.GameTime) { Airline = me9 };
            var opt = av.Options;
            double tickets = 180 * 0.8 * TheAirline.Helpers.FlightCosts.UsualFare(1200);
            var totals = new System.Collections.Generic.List<double>();
            foreach (var lvl in opt.CostLevels)
            {
                opt.CostLevel = lvl;
                var pc = TheAirline.Helpers.FlightCosts.Calculate(a9, 1200, 2.0, tickets);
                totals.Add(pc.Total);
                Console.WriteLine("  " + lvl + ": " + pc.Total.ToString("N0") + " (ground " + pc.Ground.ToString("N0") + ", maintenance " + pc.Maintenance.ToString("N0") + ", en-route " + pc.EnRoute.ToString("N0") + ", crew " + pc.Crew.ToString("N0") + ", sales " + pc.Distribution.ToString("N0") + ", overhead " + pc.Overhead.ToString("N0") + ") on tickets " + tickets.ToString("N0") + " | " + opt.CostLevelText);
            }
            opt.CostLevel = "Realistic";
            if (totals[0] != 0 || !(totals[2] > totals[1] && totals[1] > 0)) { Console.WriteLine("  FAILED cost levels"); Environment.Exit(1); }
            // a month's report adds up every day now
            var inv = new TheAirline.Models.General.Finances.Invoices();
            inv.AddInvoice(TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, 2007, 3, 1, 100);
            inv.AddInvoice(TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, 2007, 3, 2, 50);
            Console.WriteLine("  month total from two days: " + inv.GetAmount(TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, 2007, 3));
            if (inv.GetAmount(TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, 2007, 3) != 150) { Console.WriteLine("  FAILED month total"); Environment.Exit(1); }
        }
        Console.WriteLine("CABIN LAYOUT TEST");
        {
            var gC = TheAirline.Models.General.GameObject.GetInstance();
            var meC = gC.HumanAirline;
            var types = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().OfType<TheAirline.Models.Airliners.AirlinerPassengerType>().ToList();
            var a320 = types.First(x => x.Name == "Airbus A320-200");
            var big = types.Where(x => x.MaxAirlinerClasses >= 2 && x.MaxSeatingCapacity > 250).OrderBy(x => x.MaxSeatingCapacity).First();
            var small = types.Where(x => x.MaxSeatingCapacity < 30).OrderBy(x => x.MaxSeatingCapacity).First();
            var ed = new CabinEditorViewModel(a320);
            ed.AddCommand.Execute(null); ed.Classes.Last().Seats = 12;
            ed.SaveName = "x"; ed.SaveCommand.Execute(null); Console.WriteLine("  short name: " + ed.LayoutStatus);
            ed.SaveName = "Test two class"; ed.SaveCommand.Execute(null); Console.WriteLine("  save: " + ed.LayoutStatus + " | selected " + ed.Preset.Name);
            if (TheAirline.Helpers.CabinLayoutService.Saved().Count != 1 || !ed.Preset.IsSaved) { Console.WriteLine("  FAILED save"); Environment.Exit(1); }
            ed.Classes.Last().Seats = 16; ed.SaveCommand.Execute(null); Console.WriteLine("  again: " + ed.LayoutStatus);
            if (TheAirline.Helpers.CabinLayoutService.Saved().Count != 1) { Console.WriteLine("  FAILED replace"); Environment.Exit(1); }
            var std = TheAirline.Infrastructure.Configurations.GetConfigurations(TheAirline.Infrastructure.Configuration.ConfigurationType.Airliner).First(c => c.Standard).Name;
            ed.SaveName = std; ed.SaveCommand.Execute(null); Console.WriteLine("  game name: " + ed.LayoutStatus);
            // a bigger aircraft gets the same premium seats and more economy
            var edBig = new CabinEditorViewModel(big);
            var mine = edBig.Presets.First(p => p.IsSaved);
            edBig.Preset = mine; Console.WriteLine("  on " + big.Name + ": " + mine.Name + " -> " + edBig.Summary);
            if (!edBig.Layout.Contains("16C")) { Console.WriteLine("  FAILED apply"); Environment.Exit(1); }
            var edSmall = new CabinEditorViewModel(small);
            Console.WriteLine("  on " + small.Name + " (" + small.MaxSeatingCapacity + " seats, " + small.MaxAirlinerClasses + " classes): offered " + edSmall.Presets.Any(p => p.IsSaved));
            // refit every stopped aircraft of a type
            var home = meC.Airports.First();
            var fleet = new System.Collections.Generic.List<TheAirline.Models.Airliners.FleetAirliner>();
            for (int i = 0; i < 3; i++)
            {
                var al = new TheAirline.Models.Airliners.Airliner("cab" + i, a320, "N-CAB" + i, gC.GameTime) { Airline = meC };
                TheAirline.Models.Airliners.Airliners.AddAirliner(al);
                fleet.Add(TheAirline.Helpers.AirlineHelpers.AddAirliner(meC, al, home, false));
            }
            fleet[2].Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.ToRouteStart;
            var before2 = fleet[2].Airliner.Classes.Count;
            var refit = new CabinRefitViewModel(fleet[0], () => { });
            refit.Editor.Preset = refit.Editor.Presets.First(p => p.IsSaved);
            Console.WriteLine("  " + refit.AllText);
            double money = meC.Money; meC.Money += 100000000; money = meC.Money;
            refit.ApplyAllCommand.Execute(null);
            Console.WriteLine("  refit all: " + string.Join(" ; ", fleet.Select(f => f.Name + " " + string.Join("/", f.Airliner.Classes.Select(c => c.Type.ToString().Substring(0, 1) + c.SeatingCapacity)))) + " | paid " + (money - meC.Money).ToString("N0") + " " + refit.Status);
            if (fleet[1].Airliner.Classes.Count != 2 || fleet[2].Airliner.Classes.Count != before2) { Console.WriteLine("  FAILED refit all"); Environment.Exit(1); }
            foreach (var f in fleet) { meC.RemoveAirliner(f); }
            // keep one layout for the save test, add one to delete
            ed.SaveName = "Delete me"; ed.SaveCommand.Execute(null);
            ed.DeleteCommand.Execute(null); Console.WriteLine("  ask: " + ed.PendingText); ed.ConfirmCommand.Execute(null);
            Console.WriteLine("  delete: " + ed.LayoutStatus + " | saved " + string.Join(", ", TheAirline.Helpers.CabinLayoutService.Saved().Select(c => c.Name)));
            if (TheAirline.Helpers.CabinLayoutService.Saved().Count != 1) { Console.WriteLine("  FAILED delete"); Environment.Exit(1); }
        }
        Console.WriteLine("SET MONEY TEST");
        {
            var gM = TheAirline.Models.General.GameObject.GetInstance();
            var o = av.Options;
            double keepA = gM.HumanAirline.Money, keepH = gM.HumanMoney;
            Console.WriteLine("  " + o.MoneyText);
            if (keepA != keepH) { Console.WriteLine("  FAILED human money drifted"); Environment.Exit(1); }
            int notes = 0; gM.PropertyChanged += (s, e) => { if (e.PropertyName == "HumanMoney") notes++; };
            o.AirlineMoney = "abc"; o.SetMoneyCommand.Execute(null); Console.WriteLine("  bad: " + o.MoneyStatus);
            o.AirlineMoney = "$1,234,567"; o.SetMoneyCommand.Execute(null); Console.WriteLine("  set: " + o.MoneyStatus + " | top bar " + av.Money + " | human " + gM.HumanMoney);
            if (gM.HumanAirline.Money != 1234567 || gM.HumanMoney != 1234567) { Console.WriteLine("  FAILED set money"); Environment.Exit(1); }
            o.AirlineMoney = "-750k"; o.SetMoneyCommand.Execute(null); Console.WriteLine("  set: " + o.MoneyStatus + " | " + o.MoneyText);
            // every way of changing money moves both, once
            gM.AddHumanMoney(1000); TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(gM.HumanAirline, gM.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Purchases, -250); gM.HumanAirline.Money += 50;
            Console.WriteLine("  after +1000, -250 invoice, +50 direct: " + gM.HumanAirline.Money + " / " + gM.HumanMoney + " | notifications " + notes);
            if (gM.HumanAirline.Money != -749200 || gM.HumanMoney != -749200 || notes < 5) { Console.WriteLine("  FAILED follow"); Environment.Exit(1); }
            gM.HumanAirline.Money = keepA;
        }
        Console.WriteLine("MONEY AUDIT TEST");
        {
            var gA = TheAirline.Models.General.GameObject.GetInstance();
            var meA = gA.HumanAirline;
            var aiA = TheAirline.Models.Airlines.Airlines.GetAllAirlines().First(a => !a.IsHuman);
            // payments from many threads at once all arrive
            double m0 = meA.Money, a0 = aiA.Money;
            System.Threading.Tasks.Parallel.For(0, 200000, i =>
            {
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(meA, gA.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees, 1);
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(aiA, gA.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees, 1);
                if (i % 2 == 0) gA.AddHumanMoney(1);
            });
            Console.WriteLine("  200,000 parallel payments: player +" + (meA.Money - m0).ToString("N0") + " (human money " + (gA.HumanMoney - m0).ToString("N0") + "), computer airline +" + (aiA.Money - a0).ToString("N0"));
            if (meA.Money - m0 != 300000 || aiA.Money - a0 != 200000 || gA.HumanMoney != meA.Money) { Console.WriteLine("  FAILED lost payments"); Environment.Exit(1); }
            meA.Money = m0; aiA.Money = a0;

            // an advertising campaign pays exactly the months booked
            var t0 = gA.GameTime;
            gA.GameTime = new DateTime(t0.Year, t0.Month, 15);
            double mc = meA.Money; string msgA;
            TheAirline.Helpers.AdvertisingService.StartCampaign(meA, false, meA.Profile.Country.Uid, 1, 1, out msgA);
            TheAirline.Helpers.AdvertisingService.StartCampaign(meA, true, meA.Airports.First().Profile.IATACode, 1, 3, out msgA);
            var camps = TheAirline.Helpers.AdvertisingService.Campaigns(meA).ToList();
            double c1 = camps[camps.Count - 2].MonthlyCost, c3 = camps[camps.Count - 1].MonthlyCost;
            int paidMonths = 0;
            for (int k = 1; k <= 4; k++)
            {
                gA.GameTime = new DateTime(t0.Year, t0.Month, 1).AddMonths(k);
                double beforeA = meA.Money; TheAirline.Helpers.AdvertisingService.ProcessMonth(meA);
                Console.WriteLine("  month " + k + ": paid " + (beforeA - meA.Money).ToString("N0"));
            }
            double paid = mc - meA.Money;
            Console.WriteLine("  campaigns of 1 and 3 months cost " + c1.ToString("N0") + " and " + c3.ToString("N0") + " a month; paid " + paid.ToString("N0") + " in all (expected " + (c1 + 3 * c3).ToString("N0") + ")");
            gA.GameTime = t0; meA.Money = mc + paid;
            if (Math.Abs(paid - (c1 + 3 * c3)) > 0.01) { Console.WriteLine("  FAILED campaign months"); Environment.Exit(1); }
        }
        Console.WriteLine("OWNERSHIP TEST");
        {
            var gO = TheAirline.Models.General.GameObject.GetInstance();
            var meO = gO.HumanAirline;
            foreach (var f in meO.Fleet) Console.WriteLine("  " + f.Name + " " + f.Airliner.Type.Name + " " + f.Purchased + ": value " + f.Airliner.GetPrice().ToString("N0") + ", ownership " + TheAirline.Helpers.OwnershipService.MonthlyCost(f).ToString("N0") + " a month, lease would be " + f.Airliner.LeasingPrice.ToString("N0"));
            double expected = meO.Fleet.Sum(f => TheAirline.Helpers.OwnershipService.MonthlyCost(f));
            double m0 = meO.Money;
            double charged = TheAirline.Helpers.OwnershipService.ProcessMonth(meO);
            Console.WriteLine("  month: charged " + charged.ToString("N0") + ", money -" + (m0 - meO.Money).ToString("N0"));
            if (Math.Abs(charged - expected) > 0.01 || Math.Abs((m0 - meO.Money) - charged) > 0.01) { Console.WriteLine("  FAILED ownership charge"); Environment.Exit(1); }
            if (meO.Fleet.Any(f => f.Purchased == TheAirline.Models.Airliners.FleetAirliner.PurchasedType.Leased && TheAirline.Helpers.OwnershipService.MonthlyCost(f) > 0)) { Console.WriteLine("  FAILED leased charged"); Environment.Exit(1); }
            gO.CostLevel = 0;
            double orig = meO.Fleet.Sum(f => TheAirline.Helpers.OwnershipService.MonthlyCost(f));
            gO.CostLevel = 2;
            double tough = meO.Fleet.Sum(f => TheAirline.Helpers.OwnershipService.MonthlyCost(f));
            gO.CostLevel = 1;
            Console.WriteLine("  original costs " + orig.ToString("N0") + ", tough " + tough.ToString("N0"));
            if (orig != 0 || (expected > 0 && Math.Abs(tough - expected * 1.4) > 0.01)) { Console.WriteLine("  FAILED levels"); Environment.Exit(1); }
            meO.Money = m0;

            // a computer airline sells idle aircraft beyond its reserve
            var aiO = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman && a.Fleet.Count > 0 && a.Airports.Count > 0).OrderBy(a => a.Fleet.Count).First();
            var t320 = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(x => x.Name == "Airbus A320-200");
            var mineO = new System.Collections.Generic.List<TheAirline.Models.Airliners.Airliner>();
            for (int i = 0; i < 6; i++)
            {
                var al = new TheAirline.Models.Airliners.Airliner("own" + i, t320, "N-OWN" + i, gO.GameTime.AddYears(-i - 1)) { Airline = aiO };
                TheAirline.Models.Airliners.Airliners.AddAirliner(al);
                TheAirline.Helpers.AirlineHelpers.AddAirliner(aiO, al, aiO.Airports.First(), false);
                mineO.Add(al);
            }
            int idle0 = aiO.Fleet.Count(f => !f.HasRoute && TheAirline.Helpers.OwnershipService.IsOwned(f) && f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped);
            double cash0 = aiO.Money; int sold = 0;
            var tO = gO.GameTime;
            // one a week: a second sale the same day is refused, the next week's goes through
            if (TheAirline.Helpers.OwnershipService.SellSurplus(aiO) != null) sold++;
            bool sameDay = TheAirline.Helpers.OwnershipService.SellSurplus(aiO) != null;
            Console.WriteLine("  first sale " + (sold == 1 ? "done" : "missing") + ", second the same day " + (sameDay ? "ALLOWED" : "refused"));
            if (sold != 1 || sameDay) { Console.WriteLine("  FAILED weekly limit"); Environment.Exit(1); }
            for (int w = 1; w <= 10; w++)
            {
                gO.GameTime = tO.AddDays(7 * w);
                if (TheAirline.Helpers.OwnershipService.SellSurplus(aiO) != null) sold++;
            }
            gO.GameTime = tO;
            int idle1 = aiO.Fleet.Count(f => !f.HasRoute && TheAirline.Helpers.OwnershipService.IsOwned(f) && f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped);
            Console.WriteLine("  " + aiO.Profile.Name + ": " + idle0 + " idle owned, reserve " + TheAirline.Helpers.OwnershipService.Reserve(aiO) + " -> sold " + sold + " for " + (aiO.Money - cash0).ToString("N0") + ", " + idle1 + " idle left; on the used market: " + TheAirline.Models.Airliners.Airliners.GetAirlinersForSale().Count(a => mineO.Contains(a)));
            if (idle1 > TheAirline.Helpers.OwnershipService.Reserve(aiO) || sold == 0 || aiO.Money <= cash0 || TheAirline.Models.Airliners.Airliners.GetAirlinersForSale().Count(a => mineO.Contains(a)) != sold) { Console.WriteLine("  FAILED surplus sale"); Environment.Exit(1); }
        }
        Console.WriteLine("AIRLINE KIND TEST");
        {
            var meT = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
            var cargoAl = TheAirline.Models.Airlines.Airlines.GetAllAirlines().First(a => a.AirlineRouteFocus == TheAirline.Models.Routes.Route.RouteType.Cargo);
            var freighter = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(t => t is TheAirline.Models.Airliners.AirlinerCargoType);
            var a320T = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(t => t.Name == "Airbus A320-200");
            Console.WriteLine("  " + meT.Profile.Name + ": " + TheAirline.Helpers.AirlineTypeRules.Kind(meT) + " | " + cargoAl.Profile.Name + ": " + TheAirline.Helpers.AirlineTypeRules.Kind(cargoAl));
            Console.WriteLine("  cargo route: " + TheAirline.Helpers.AirlineTypeRules.RouteBlocker(meT, TheAirline.Models.Routes.Route.RouteType.Cargo));
            Console.WriteLine("  freighter: " + TheAirline.Helpers.AirlineTypeRules.TypeBlocker(meT, freighter));
            Console.WriteLine("  merge: " + TheAirline.Helpers.AirlineTypeRules.MergeBlocker(meT, cargoAl));
            string m;
            var req = new TheAirline.Helpers.NewRouteRequest { Origin = meT.Airports.First(), Destination = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "MEM"), RouteType = TheAirline.Models.Routes.Route.RouteType.Cargo };
            var made = TheAirline.Helpers.RouteCreator.Create(req, out m);
            Console.WriteLine("  create cargo route: " + (made == null ? "refused: " + m : "CREATED"));
            av.NewAircraft.Reload(); av.Market.Reload();
            int fNew = av.NewAircraft.Types.Count(t => t.Type is TheAirline.Models.Airliners.AirlinerCargoType), fUsed = av.Market.Aircraft.Count(x => x.Airliner.Type is TheAirline.Models.Airliners.AirlinerCargoType);
            Console.WriteLine("  freighters offered: new " + fNew + ", used " + fUsed + " | create route page kind: " + av.Airports.KindText + ", can choose " + av.Airports.CanChooseKind);
            if (made != null || fNew != 0 || fUsed != 0 || TheAirline.Helpers.AirlineTypeRules.CanUseType(meT, freighter) || !TheAirline.Helpers.AirlineTypeRules.CanUseType(cargoAl, freighter)
                || TheAirline.Helpers.AirlineTypeRules.CanUseType(cargoAl, a320T) || TheAirline.Helpers.AirlineTypeRules.MergeBlocker(meT, cargoAl) == null || av.Airports.CanChooseKind)
            { Console.WriteLine("  FAILED airline kinds"); Environment.Exit(1); }
        }
        Console.WriteLine("RUNWAY TEST");
        {
            var g = TheAirline.Models.General.GameObject.GetInstance();
            var m = av.AirportManager;
            var all = TheAirline.Models.Airports.Airports.GetAllAirports();
            var me4 = g.HumanAirline;
            // an airport nobody uses, with one runway
            var kiv = all.FirstOrDefault(x => x.Profile.IATACode == "KIV") ?? all.First(x => !me4.Airports.Contains(x) && x.Runways.Count == 1);
            m.SelectAirport(kiv);
            Console.WriteLine("  " + kiv.Profile.IATACode + " runways: " + string.Join(" ; ", m.Runways.Select(r => r.Text)) + " | max " + kiv.GetMaxRunwayLength());
            m.SelectedRunway = m.Runways.First();
            long rwBefore = kiv.GetMaxRunwayLength();
            m.ExtendLength = (m.SelectedRunway.Runway.Length + 600).ToString();
            Console.WriteLine("  extend preview: " + m.ExtendText);
            double money0 = me4.Money;
            m.ExtendRunwayCommand.Execute(null); Console.WriteLine("  ask: " + m.PendingText); m.ConfirmPendingCommand.Execute(null);
            Console.WriteLine("  extend: " + m.Status + " | paid " + (money0 - me4.Money).ToString("N0") + " | max now " + kiv.GetMaxRunwayLength());
            me4.Money += 100000000;
            m.NewRunwayLength = "4200"; m.NewRunwaySurface = TheAirline.Models.Airports.Runway.SurfaceType.Concrete; m.NewRunwayName = "10R/28L";
            Console.WriteLine("  new preview: " + m.NewRunwayText);
            m.BuildRunwayCommand.Execute(null); Console.WriteLine("  ask: " + m.PendingText); m.ConfirmPendingCommand.Execute(null);
            Console.WriteLine("  build: " + m.Status + " | max now " + kiv.GetMaxRunwayLength());
            m.NewRunwayLength = "200"; Console.WriteLine("  too short: " + m.NewRunwayText);
            m.NewRunwayName = "10R/28L"; m.NewRunwayLength = "3000"; Console.WriteLine("  same name: " + m.NewRunwayText); m.NewRunwayName = "";
            if (kiv.GetMaxRunwayLength() != rwBefore) { Console.WriteLine("  FAILED runway changed too early"); Environment.Exit(1); }
            // save and load: the airport is not used by any airline, the runways must stay
            TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("runwaytest");
            TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("runwaytest");
            g = TheAirline.Models.General.GameObject.GetInstance();
            kiv = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == kiv.Profile.IATACode);
            Console.WriteLine("  after load: " + string.Join(" ; ", kiv.Runways.Select(r => r.Name + " " + r.Length + " ext " + r.ExtendTo + " " + TheAirline.Helpers.RunwayService.Status(r))));
            if (kiv.Runways.Count < 2 || !kiv.Runways.Any(r => r.ExtendTo > 0)) { Console.WriteLine("  FAILED runway save"); Environment.Exit(1); }
            var ext = kiv.Runways.First(r => r.ExtendTo > 0);
            g.GameTime = ext.ExtendDate; TheAirline.Helpers.RunwayService.ProcessDaily();
            Console.WriteLine("  after extension: max " + kiv.GetMaxRunwayLength() + " | " + TheAirline.Helpers.RunwayService.Status(ext));
            if (kiv.GetMaxRunwayLength() != rwBefore + 600) { Console.WriteLine("  FAILED extension"); Environment.Exit(1); }
            var nr = kiv.Runways.First(r => r.Name == "10R/28L");
            g.GameTime = nr.BuiltDate; TheAirline.Helpers.RunwayService.ProcessDaily();
            Console.WriteLine("  after build: max " + kiv.GetMaxRunwayLength() + " | " + g.NewsBox.GetNews().Where(n => n.Subject.Contains("unway")).Select(n => n.Subject + ": " + n.Body).LastOrDefault());
            if (kiv.GetMaxRunwayLength() != 4200) { Console.WriteLine("  FAILED new runway"); Environment.Exit(1); }
        }
        Console.WriteLine("CABIN LAYOUT AFTER LOAD");
        {
            var saved = TheAirline.Helpers.CabinLayoutService.Saved();
            Console.WriteLine("  " + string.Join(", ", saved.Select(c => c.Name + " (" + TheAirline.Helpers.CabinLayoutService.Describe(c) + ", " + c.Classes.Sum(k => k.Facilities.Count) + " facilities)")));
            if (saved.Count != 1 || saved[0].Name != "Test two class" || saved[0].Classes.Count != 2) { Console.WriteLine("  FAILED layout save"); Environment.Exit(1); }
        }
        Console.WriteLine("SHARES AFTER LOAD");
        {
            var g8 = TheAirline.Models.General.GameObject.GetInstance();
            var me8 = g8.HumanAirline;
            Console.WriteLine("  own " + TheAirline.Helpers.SharesService.Owned(me8, me8) + " of " + TheAirline.Helpers.SharesService.Total(me8) + " | subs " + string.Join(",", me8.Subsidiaries.Select(x => x.Profile.IATACode + " auto " + x.AutoManaged)));
            if (TheAirline.Helpers.SharesService.Total(me8) <= 10000 || !me8.Subsidiaries.Any(x => x.Profile.IATACode == "LZ")) { Console.WriteLine("  FAILED shares save"); Environment.Exit(1); }
        }
        Console.WriteLine("ADVERTISING TEST");
        {
            var adv = av.Advertising;
            var g2 = TheAirline.Models.General.GameObject.GetInstance();
            var me5 = g2.HumanAirline;
            adv.Reload();
            Console.WriteLine("  " + adv.ReputationText + " | " + adv.CostText);
            foreach (var ch in adv.Channels) Console.WriteLine("  " + ch.Name + ": " + ch.Selected + " | options " + string.Join(", ", ch.Levels));
            foreach (var ch in adv.Channels) ch.Selected = ch.Levels.Last();
            Console.WriteLine("  all global: " + adv.ReputationText + " | " + adv.CostText);
            int rep0 = me5.Reputation; double m0 = me5.Money;
            TheAirline.Helpers.AdvertisingService.ProcessMonth(me5);
            Console.WriteLine("  a month later: reputation " + rep0 + " -> " + me5.Reputation);
            if (me5.Reputation <= rep0) { Console.WriteLine("  FAILED reputation"); Environment.Exit(1); }
            adv.CountryTarget = true; adv.Search = "United States";
            Console.WriteLine("  targets: " + string.Join(" ; ", adv.Targets.Take(3)));
            adv.Size = "Large"; adv.Months = 6; Console.WriteLine("  " + adv.CampaignText);
            adv.StartCommand.Execute(null); adv.ConfirmCommand.Execute(null);
            Console.WriteLine("  start: " + adv.Status + " | running " + adv.Campaigns.Count);
            var atl2 = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "ATL");
            // an airport outside the United States that is in this game (London when the game has it)
            var lhr2 = TheAirline.Models.Airports.Airports.GetAllAirports().FirstOrDefault(x => x.Profile.IATACode == "LHR")
                       ?? TheAirline.Models.Airports.Airports.GetAllAirports().Where(x => x.Profile.Country != atl2.Profile.Country && x.Profile.IATACode?.Length == 3).OrderByDescending(x => x.Profile.Pax).First();
            Console.WriteLine("  other airport: " + lhr2.Profile.IATACode + " " + lhr2.Profile.Name);
            Console.WriteLine("  factor ATL " + TheAirline.Helpers.AdvertisingService.AirportFactor(me5, atl2).ToString("0.000") + " | LHR " + TheAirline.Helpers.AdvertisingService.AirportFactor(me5, lhr2).ToString("0.000"));
            if (TheAirline.Helpers.AdvertisingService.CampaignFactor(me5, atl2) < 1.3) { Console.WriteLine("  FAILED campaign"); Environment.Exit(1); }
            adv.AirportTarget = true; adv.Search = lhr2.Profile.IATACode; adv.Size = "Small"; adv.Months = 1; Console.WriteLine("  airport: " + adv.CampaignText);
            adv.StartCommand.Execute(null); adv.ConfirmCommand.Execute(null); Console.WriteLine("  start: " + adv.Status);
            g2.GameTime = g2.GameTime.AddMonths(2); TheAirline.Helpers.AdvertisingService.ProcessMonth(me5); adv.Reload();
            Console.WriteLine("  two months later: running " + adv.Campaigns.Count + " | LHR factor " + TheAirline.Helpers.AdvertisingService.CampaignFactor(me5, lhr2));
            adv.Campaigns[0].EndCommand.Execute(null); adv.ConfirmCommand.Execute(null); Console.WriteLine("  stop: " + adv.Status + " | running " + adv.Campaigns.Count);
            foreach (var ch in adv.Channels) ch.Selected = ch.Levels.First();
            int rep1 = me5.Reputation; TheAirline.Helpers.AdvertisingService.ProcessMonth(me5);
            Console.WriteLine("  ads off: reputation " + rep1 + " -> " + me5.Reputation + " | " + adv.CostText);
        }
        Console.WriteLine("QUICK TEST OK");
        Environment.Exit(0);
    }

    public static async Task Run()
    {
        try
        {
            var shell = new ShellViewModel(a => a(), () => Console.WriteLine("exit"));
            if (Environment.GetCommandLineArgs().Contains("scenario")) { await ScenarioTest(shell); return; }
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
            // tests keep the small world they were written for; TAP_OPPONENTS sets more for the economy probes
            int opp; ng.Opponents = int.TryParse(Environment.GetEnvironmentVariable("TAP_OPPONENTS"), out opp) ? opp : 5;
            Console.WriteLine("opponents: " + ng.SelectedOpponentOption.Name + " | " + ng.OpponentsText);
            Console.WriteLine("kind: " + ng.SelectedAirlineKind);
            ng.CreateCommand.Execute(null);
            await Wait(() => shell.Current is AirlineViewModel, "game created");
            var av = (AirlineViewModel)shell.Current;
            Console.WriteLine("AIRLINE SCREEN: " + av.AirlineName + " " + av.Date + " " + av.Money + " fleet=" + av.Fleet.Count + " routes=" + av.Routes.Count + " running=" + av.IsRunning);
            if (Environment.GetCommandLineArgs().Contains("quick")) { QuickTests(av); return; }
            if (Environment.GetCommandLineArgs().Contains("cargo")) { CargoProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("airlines")) { AirlinesProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("year")) { YearProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("perf")) { PerfProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("savetime")) { SaveProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("demand")) { DemandProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("bloat")) { await BloatTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("replace")) { await ReplaceTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("pax")) { PaxProbe(av); return; }
            if (Environment.GetCommandLineArgs().Contains("costs")) { CostProbe(av); return; }
            if (Environment.GetCommandLineArgs().Contains("cargounit")) { CargoUnitTable(); return; }
            if (Environment.GetCommandLineArgs().Contains("unit")) { UnitTable(); return; }
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
                {
                    var ttAir = tt.SelectedAircraft.Airliner;
                    int before = tt.Rows.Count;
                    tt.RemoveAllFlightsCommand.Execute(null);
                    Console.WriteLine("  remove all asks: " + tt.RemoveAllText);
                    tt.CancelRemoveAllCommand.Execute(null);
                    if (tt.HasRemoveAll || tt.Rows.Count != before) { Console.WriteLine("  FAILED cancel remove all"); Environment.Exit(1); }
                    tt.RemoveAllFlightsCommand.Execute(null);
                    tt.ConfirmRemoveAllCommand.Execute(null);
                    Console.WriteLine("  remove all: " + tt.Status + " rows=" + tt.Rows.Count + " routes on aircraft=" + ttAir.Routes.Count);
                    if (tt.Rows.Count != 0 || ttAir.Routes.Count != 0) { Console.WriteLine("  FAILED remove all flights"); Environment.Exit(1); }
                }
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
            for (int w = 0; w < 600 && !(shell.Current is MainMenuViewModel); w++) Thread.Sleep(100);
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
                // a passenger airline can't buy freighters or plan cargo routes
                var meKind = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
                var cna = cav.NewAircraft;
                cna.Reload();
                int paxFreighters = cna.Types.Count(t => t.Type is TheAirline.Models.Airliners.AirlinerCargoType);
                cav.Airports.CargoMode = true;
                Console.WriteLine("  passenger airline: freighters for sale " + paxFreighters + ", cargo mode " + cav.Airports.CargoMode + " (" + cav.Airports.Status + ")");
                if (paxFreighters != 0 || cav.Airports.CargoMode) { Console.WriteLine("  FAILED passenger airline went cargo"); Environment.Exit(1); }
                // for the cargo checks the test airline becomes a cargo airline (in a game only possible before it flies)
                meKind.AirlineRouteFocus = TheAirline.Models.Routes.Route.RouteType.Cargo;
                cav.Airports.ReloadAdvice();
                // buy a cargo aircraft
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
                if (croute == null) { Console.WriteLine("  FAILED no cargo route for the cargo airline"); Environment.Exit(1); }
                meKind.AirlineRouteFocus = TheAirline.Models.Routes.Route.RouteType.Passenger;
                cav.Airports.ReloadAdvice();
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
                foreach (var pl in map.Places.Take(2)) Console.WriteLine("  tooltip " + pl.Label + ":\n    " + (map.AirportTip(pl) ?? "NULL").Replace("\n", "\n    "));
                if (map.Aircraft.Count > 0) Console.WriteLine("  tooltip aircraft:\n    " + (map.AircraftTip(map.Aircraft[0]) ?? "NULL").Replace("\n", "\n    "));
                if (map.Places.Count > 0 && (map.AirportTip(map.Places[0]) ?? "").Split('\n').Length < 6) { Console.WriteLine("  FAILED tooltip"); Environment.Exit(1); }
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
                var spare0 = TheAirline.Helpers.MaintenanceService.FindSpare(ac);
                bool needsSpare = TheAirline.Helpers.MaintenanceService.NeedsSpare(ac, TheAirline.Helpers.MaintenanceCheck.C);
                Console.WriteLine("  C check needs a spare: " + needsSpare + " (" + ac.Routes.Count + " routes), spare found: " + (spare0 != null ? spare0.Name : "none"));
                if (needsSpare && spare0 != null)
                {
                    spare0.GroundedToDate = gm.GameTime.AddDays(5);
                    bool blocked = TheAirline.Helpers.MaintenanceService.PerformCheck(ac, TheAirline.Helpers.MaintenanceCheck.C, out msg);
                    Console.WriteLine("  spare grounded -> C check: " + blocked + " | " + msg);
                    spare0.GroundedToDate = DateTime.MinValue;
                    if (blocked || !msg.Contains("spare")) { Console.WriteLine("  FAILED C check should wait for a spare"); Environment.Exit(1); }
                }
                else if (needsSpare)
                {
                    bool blocked = TheAirline.Helpers.MaintenanceService.PerformCheck(ac, TheAirline.Helpers.MaintenanceCheck.C, out msg);
                    Console.WriteLine("  no spare -> C check: " + blocked + " | " + msg);
                    if (blocked) { Console.WriteLine("  FAILED C check without a spare"); Environment.Exit(1); }
                }
                bool okC = true;
                if (!needsSpare || spare0 != null)
                {
                    okC = TheAirline.Helpers.MaintenanceService.PerformCheck(ac, TheAirline.Helpers.MaintenanceCheck.C, out msg);
                    Console.WriteLine("  C check: " + msg + " grounded=" + TheAirline.Helpers.MaintenanceService.IsGrounded(ac));
                    if (needsSpare && (!TheAirline.Helpers.MaintenanceService.IsCovering(spare0) || !TheAirline.Helpers.MaintenanceService.IsCovered(ac) || spare0.CoverName != ac.Name || ac.CoverName != spare0.Name))
                    {
                        Console.WriteLine("  FAILED cover not set"); Environment.Exit(1);
                    }
                    mvm.Reload();
                    foreach (var it in mvm.Items.Where(i => i.Aircraft == ac || i.Aircraft == spare0)) Console.WriteLine("  status line: " + it.Title + " -> " + it.Grounded);
                }
                else
                {
                    //no spare in this game: do the check the way the interface would for an aircraft without routes
                    Console.WriteLine("  (no spare in this game, the C check test is skipped)");
                    ac.GroundedToDate = gm.GameTime.AddDays(7);
                }
                bool again = TheAirline.Helpers.MaintenanceService.PerformCheck(ac, TheAirline.Helpers.MaintenanceCheck.A, out msg);
                Console.WriteLine("  while grounded: " + again + " " + msg);
                if (!okC || again || !TheAirline.Helpers.MaintenanceService.IsGrounded(ac)) { Console.WriteLine("  FAILED C check"); Environment.Exit(1); }
                // spare aircraft for a heavy check on an aircraft that flies routes
                {
                    foreach (var o1 in hum.Fleet) { o1.CoverUntil = DateTime.MinValue; o1.CoveredUntil = DateTime.MinValue; o1.CoverName = null; o1.GroundedToDate = DateTime.MinValue; }
                    var fr = hum.Fleet.FirstOrDefault(f => f.Routes.Count > 0 && f.Airliner.BuiltDate <= gm.GameTime);
                    if (fr == null) Console.WriteLine("  (no aircraft with routes in this game, spare test skipped)");
                    else
                    {
                        var sp1 = TheAirline.Helpers.MaintenanceService.FindSpare(fr);
                        Console.WriteLine("  routed aircraft " + fr.Name + " (" + fr.Routes.Count + " routes, longest " + TheAirline.Helpers.MaintenanceService.GetSpareRange(fr).ToString("0") + " km), spare: " + (sp1 != null ? sp1.Name : "none"));
                        string m1;
                        if (sp1 != null)
                        {
                            sp1.GroundedToDate = gm.GameTime.AddDays(5);
                            bool b1 = TheAirline.Helpers.MaintenanceService.PerformCheck(fr, TheAirline.Helpers.MaintenanceCheck.D, out m1);
                            Console.WriteLine("  spare grounded -> D check: " + b1 + " | " + m1);
                            sp1.GroundedToDate = DateTime.MinValue;
                            if (b1 || !m1.Contains("spare")) { Console.WriteLine("  FAILED waits for spare"); Environment.Exit(1); }
                            bool c1 = TheAirline.Helpers.MaintenanceService.PerformCheck(fr, TheAirline.Helpers.MaintenanceCheck.D, out m1);
                            Console.WriteLine("  with spare -> D check: " + c1 + " | " + m1);
                            if (!c1 || !TheAirline.Helpers.MaintenanceService.IsGrounded(fr) || !TheAirline.Helpers.MaintenanceService.IsCovered(fr) || !TheAirline.Helpers.MaintenanceService.IsCovering(sp1) || sp1.CoverName != fr.Name || fr.CoverName != sp1.Name)
                            {
                                Console.WriteLine("  FAILED cover"); Environment.Exit(1);
                            }
                            //the spare is busy now, a second heavy check on another routed aircraft needs a different spare
                            var other = TheAirline.Helpers.MaintenanceService.FindSpare(fr);
                            Console.WriteLine("  second spare while " + sp1.Name + " covers: " + (other != null ? other.Name : "none"));
                            if (other == sp1) { Console.WriteLine("  FAILED spare used twice"); Environment.Exit(1); }
                            mvm.Reload();
                            foreach (var it in mvm.Items.Where(i => i.Aircraft == fr || i.Aircraft == sp1)) Console.WriteLine("  status line: " + it.Title + " -> " + it.Grounded);
                        }
                        else
                        {
                            bool b2 = TheAirline.Helpers.MaintenanceService.PerformCheck(fr, TheAirline.Helpers.MaintenanceCheck.D, out m1);
                            Console.WriteLine("  no spare -> D check: " + b2 + " | " + m1);
                            if (b2 || !m1.Contains("spare")) { Console.WriteLine("  FAILED no spare"); Environment.Exit(1); }
                        }
                    }
                    foreach (var o2 in hum.Fleet) { o2.CoverUntil = DateTime.MinValue; o2.CoveredUntil = DateTime.MinValue; o2.CoverName = null; o2.GroundedToDate = DateTime.MinValue; }
                    ac.GroundedToDate = gm.GameTime.AddDays(7);
                }
                worst.AText = "120"; worst.BText = "300"; worst.CText = "12"; worst.DText = "60";
                worst.ApplyIntervalsCommand.Execute(null);
                Console.WriteLine("  intervals: " + ac.AMaintenanceInterval + "/" + ac.BMaintenanceInterval + "/" + ac.CMaintenanceInterval + "/" + ac.DMaintenanceInterval);

                // automatic maintenance
                {
                    ac.GroundedToDate = DateTime.MinValue;
                    foreach (var o0 in hum.Fleet) { o0.CoverUntil = DateTime.MinValue; o0.CoveredUntil = DateTime.MinValue; o0.CoverName = null; o0.GroundedToDate = DateTime.MinValue; }
                    bool cWillRun = !TheAirline.Helpers.MaintenanceService.NeedsSpare(ac, TheAirline.Helpers.MaintenanceCheck.C) || TheAirline.Helpers.MaintenanceService.FindSpare(ac) != null;
                    var past = gm.GameTime.AddDays(-2000);
                    ac.PurchasedDate = past; ac.LastAMaintenance = past; ac.LastBMaintenance = past; ac.LastCMaintenance = past; ac.LastDMaintenance = past;
                    var others = hum.Fleet.Where(f => f != ac).ToList();
                    foreach (var o in others) o.AutoMaintenance = false;
                    var noAuto = TheAirline.Helpers.MaintenanceService.ProcessAuto(hum);
                    worst.Auto = true;
                    Console.WriteLine("  auto on: " + mvm.Status + " | summary: " + mvm.Summary.Substring(0, Math.Min(120, mvm.Summary.Length)));
                    double mAuto = gm.HumanMoney;
                    var doneAuto = TheAirline.Helpers.MaintenanceService.ProcessAuto(hum);
                    foreach (var d in doneAuto) Console.WriteLine("  auto: " + d);
                    //B grounds the aircraft for a day, so the heavier checks wait: one grounding check at a time
                    bool abOk = ac.LastAMaintenance == gm.GameTime && ac.LastBMaintenance == gm.GameTime && ac.LastCMaintenance != gm.GameTime;
                    Console.WriteLine("  without the switch: " + noAuto.Count + " checks, with it: " + doneAuto.Count + ", money " + (gm.HumanMoney - mAuto).ToString("0") + ", grounded=" + TheAirline.Helpers.MaintenanceService.IsGrounded(ac));
                    if (noAuto.Count != 0 || doneAuto.Count != 2 || !abOk || !TheAirline.Helpers.MaintenanceService.IsGrounded(ac) || !(gm.HumanMoney < mAuto)) { Console.WriteLine("  FAILED auto maintenance"); Environment.Exit(1); }
                    ac.GroundedToDate = DateTime.MinValue;
                    var next = TheAirline.Helpers.MaintenanceService.ProcessAuto(hum);
                    foreach (var d in next) Console.WriteLine("  auto next day: " + d);
                    bool cOk = !cWillRun ? next.Count == 0 : next.Count == 1 && ac.LastCMaintenance == gm.GameTime && TheAirline.Helpers.MaintenanceService.IsGrounded(ac);
                    if (!cOk) { Console.WriteLine("  FAILED auto C check"); Environment.Exit(1); }
                    var again2 = TheAirline.Helpers.MaintenanceService.ProcessAuto(hum);
                    if (again2.Count != 0) { Console.WriteLine("  FAILED auto repeats"); Environment.Exit(1); }
                    mvm.AutoNoneCommand.Execute(null);
                    Console.WriteLine("  all manual: " + mvm.Status + " | still auto: " + hum.Fleet.Count(f => f.AutoMaintenance));
                    if (hum.Fleet.Any(f => f.AutoMaintenance)) { Console.WriteLine("  FAILED manual for all"); Environment.Exit(1); }
                    ac.GroundedToDate = DateTime.MinValue;
                }

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
                Console.WriteLine("ROUTE SERVICE TEST");
                var sh = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
                var sroute = sh.Routes.FirstOrDefault(r => r is TheAirline.Models.Routes.PassengerRoute && r.HasAirliner);
                if (sroute == null) { Console.WriteLine("  FAILED no passenger route"); Environment.Exit(1); }
                var srd = new RouteDetailViewModel(sroute, () => { });
                var svc = srd.Service;
                foreach (var c in svc.Classes) Console.WriteLine("  " + c.ClassName + ": " + string.Join(" | ", c.Rows.Select(r => r.TypeName + "=" + r.Selected.Facility.Name + " (" + r.Options.Count + " options)")));
                Console.WriteLine("  " + svc.ScoreTitle + ": " + string.Join(", ", svc.Scores.Select(x => x.Name + " " + x.Value)));
                Console.WriteLine("  " + svc.CostText);
                svc.PremiumCommand.Execute(null);
                svc.SaveCommand.Execute(null);
                var eco = ((TheAirline.Models.Routes.PassengerRoute)sroute).Classes[0];
                Console.WriteLine("  premium saved: " + svc.Status + " food=" + eco.GetFacility(TheAirline.Models.Routes.RouteFacility.FacilityType.Food)?.Name + " | " + svc.CostText);
                var premiumFood = eco.GetFacility(TheAirline.Models.Routes.RouteFacility.FacilityType.Food);
                svc.BasicCommand.Execute(null);
                svc.SaveCommand.Execute(null);
                var basicFood = eco.GetFacility(TheAirline.Models.Routes.RouteFacility.FacilityType.Food);
                Console.WriteLine("  basic saved: food=" + basicFood?.Name + " | " + svc.CostText + " | scores: " + string.Join(", ", svc.Scores.Select(x => x.Name + " " + x.Value)));
                if (premiumFood == null || basicFood == null || premiumFood.ServiceLevel <= basicFood.ServiceLevel) { Console.WriteLine("  FAILED service presets"); Environment.Exit(1); }
            }
            {
                Console.WriteLine("FACILITIES AND CONTRACTS TEST");
                var gx = TheAirline.Models.General.GameObject.GetInstance();
                var hx = gx.HumanAirline;
                var fvm = new AirlineServicesViewModel(() => { });
                Console.WriteLine("  " + fvm.Summary + " | listed " + fvm.Facilities.Count + " | training choices " + fvm.TrainingChoices.Count);
                foreach (var fxi in fvm.Facilities.Take(4)) Console.WriteLine("  " + fxi.Text + " | " + fxi.Detail);
                var news = fvm.Facilities.FirstOrDefault(f => f.Facility.Uid == "102");
                if (news == null) { Console.WriteLine("  FAILED newspaper facility not listed"); Environment.Exit(1); }
                double fm = gx.HumanMoney;
                news.Command.Execute(null);
                Console.WriteLine("  pending: " + fvm.PendingText);
                fvm.ConfirmCommand.Execute(null);
                Console.WriteLine("  bought: " + fvm.Status + " owned=" + hx.Facilities.Any(f => f.Uid == "102") + " money " + (gx.HumanMoney - fm).ToString("0"));
                if (!hx.Facilities.Any(f => f.Uid == "102") || !(gx.HumanMoney < fm)) { Console.WriteLine("  FAILED buy facility"); Environment.Exit(1); }
                var froute = hx.Routes.First(r => r is TheAirline.Models.Routes.PassengerRoute && r.HasAirliner);
                var fsvc = new RouteDetailViewModel(froute, () => { }).Service;
                var newsRow = fsvc.Classes[0].Rows.First(r => r.Type == TheAirline.Models.Routes.RouteFacility.FacilityType.Newspapers);
                Console.WriteLine("  newspaper options now: " + string.Join(" / ", newsRow.Options.Select(o => o.Facility.Name)));
                newsRow.Selected = newsRow.Options.Last();
                fsvc.SaveCommand.Execute(null);
                var usedNews = ((TheAirline.Models.Routes.PassengerRoute)froute).Classes[0].GetFacility(TheAirline.Models.Routes.RouteFacility.FacilityType.Newspapers);
                Console.WriteLine("  route newspaper: " + usedNews?.Name);
                fvm.Reload();
                fvm.Owned.First(f => f.Facility.Uid == "102").Command.Execute(null);
                Console.WriteLine("  pending: " + fvm.PendingText);
                fvm.ConfirmCommand.Execute(null);
                var afterNews = ((TheAirline.Models.Routes.PassengerRoute)froute).Classes[0].GetFacility(TheAirline.Models.Routes.RouteFacility.FacilityType.Newspapers);
                Console.WriteLine("  sold: " + fvm.Status + " owned=" + hx.Facilities.Any(f => f.Uid == "102") + " route newspaper now: " + afterNews?.Name);
                if (hx.Facilities.Any(f => f.Uid == "102") || (usedNews != null && usedNews.Requires != null && afterNews == usedNews)) { Console.WriteLine("  FAILED sell facility"); Environment.Exit(1); }
                if (fvm.TrainingChoices.Count > 0)
                {
                    int before = hx.Facilities.Count;
                    fvm.SelectedTraining = fvm.TrainingChoices[0];
                    fvm.BuyTrainingCommand.Execute(null);
                    fvm.ConfirmCommand.Execute(null);
                    Console.WriteLine("  training: " + fvm.Status + " facilities " + before + " -> " + hx.Facilities.Count);
                }

                var ctype = TheAirline.Models.General.SpecialContractTypes.GetTypes().FirstOrDefault(t => !t.IsFixedDate);
                if (ctype == null) { Console.WriteLine("  FAILED no contract type loaded"); Environment.Exit(1); }
                ctype.LastDate = gx.GameTime;
                if (!gx.Contracts.Contains(ctype)) gx.Contracts.Add(ctype);
                var cvm = new ContractsViewModel(() => { });
                Console.WriteLine("  " + cvm.OffersTitle + " | " + cvm.Offers[0].Name + " | " + cvm.Offers[0].Terms);
                foreach (var l in cvm.Offers[0].Lines) Console.WriteLine("    " + l);
                cvm.Offers[0].AcceptCommand.Execute(null);
                Console.WriteLine("  pending: " + cvm.PendingText);
                cvm.ConfirmCommand.Execute(null);
                var sc = hx.SpecialContracts.First();
                Console.WriteLine("  accepted: " + cvm.Status + " active=" + cvm.Active.Count + " judged on " + TheAirline.Helpers.ContractService.GetDeadline(sc).ToShortDateString());
                foreach (var l in cvm.Active[0].Lines) Console.WriteLine("    " + l);
                var scr = ctype.Routes[0];
                var proute = new TheAirline.Models.Routes.PassengerRoute(Guid.NewGuid().ToString(), scr.Departure, scr.Destination, gx.GameTime, 100);
                hx.AddRoute(proute);
                var linked = TheAirline.Helpers.ContractService.LinkRoute(hx, proute);
                Console.WriteLine("  linked route: " + string.Join(",", linked) + " in contract=" + sc.Routes.Contains(proute));
                cvm.Reload();
                Console.WriteLine("  " + cvm.Active[0].Lines[0]);
                hx.RemoveRoute(proute); sc.Routes.Remove(proute);
                if (!sc.Routes.Contains(proute) && linked.Count != 1) { Console.WriteLine("  FAILED link"); Environment.Exit(1); }
                bool still = DateTime.Compare(TheAirline.Helpers.ContractService.GetDeadline(sc), gx.GameTime) > 0;
                if (!still || cvm.Active.Count != 1 || gx.Contracts.Contains(ctype)) { Console.WriteLine("  FAILED contract state: still=" + still + " active=" + cvm.Active.Count + " offered=" + gx.Contracts.Contains(ctype) + " now=" + gx.GameTime + " deadline=" + TheAirline.Helpers.ContractService.GetDeadline(sc)); Environment.Exit(1); }
                hx.SpecialContracts.Remove(sc);
            }
            {
                Console.WriteLine("DATE FORMAT AND ON ORDER TEST");
                bool oldUs = DateFormatter.UseUs;
                var dd = new DateTime(2007, 3, 25, 14, 30, 0);
                DateFormatter.UseUs = true;
                string us = DateFormatter.Date(dd); string usT = DateFormatter.DateTime(dd);
                DateFormatter.UseUs = false;
                string intl = DateFormatter.Date(dd);
                Console.WriteLine("  us=" + us + " (" + usT + ") international=" + intl);
                var opt = av2.Options;
                opt.UseUsDate = true;
                string headerUs = av2.Date;
                av2.Refresh();
                headerUs = av2.Date;
                opt.UseInternationalDate = true;
                av2.Refresh();
                Console.WriteLine("  top bar date: us=" + headerUs + " international=" + av2.Date);
                DateFormatter.UseUs = oldUs;
                if (us != "03-25-2007" || intl != "25-03-2007") { Console.WriteLine("  FAILED date format"); Environment.Exit(1); }
                var gz = TheAirline.Models.General.GameObject.GetInstance();
                int onOrder = av2.Fleet.Count(f => f.IsOnOrder);
                int expected = gz.HumanAirline.Fleet.Count(f => f.Airliner.BuiltDate > gz.GameTime);
                Console.WriteLine("  fleet items on order: " + onOrder + " (expected " + expected + "), idle " + av2.Fleet.Count(f => f.IsIdle));
                var ov = new FleetOverviewViewModel(() => { }, f => { }, f => { });
                Console.WriteLine("  overview: " + ov.Title + " | rows on order " + ov.Rows.Count(r => r.IsOnOrder));
                if (onOrder != expected) { Console.WriteLine("  FAILED on order"); Environment.Exit(1); }
            }
            {
                Console.WriteLine("DISTANCE UNIT TEST");
                bool oldMi = DistanceFormatter.UseMiles;
                DistanceFormatter.UseMiles = false;
                string k1 = DistanceFormatter.Km(1000), k2 = DistanceFormatter.Speed(800), k3 = DistanceFormatter.Metres(2500);
                av2.Options.UseMiles = true;
                string m1 = DistanceFormatter.Km(1000), m2 = DistanceFormatter.Speed(800), m3 = DistanceFormatter.Metres(2500);
                Console.WriteLine("  km: " + k1 + " | " + k2 + " | " + k3 + "   miles: " + m1 + " | " + m2 + " | " + m3 + "   " + av2.Options.DistanceExample);
                DistanceFormatter.UseMiles = oldMi;
                if (k1 != "1,000 km" || k2 != "800 km/h" || k3 != "2,500 m" || m1 != "621 mi" || m2 != "497 mph" || m3 != "8,202 ft") { Console.WriteLine("  FAILED distance"); Environment.Exit(1); }
            }
            {
                Console.WriteLine("PILOT SUPPLY AND FLIGHT SCHOOL TEST");
                var gs = TheAirline.Models.General.GameObject.GetInstance();
                var hs = gs.HumanAirline;
                // pilots never run out
                int made = TheAirline.Helpers.PilotSupply.EnsureFamily("Zeppelin NT");
                int madeAgain = TheAirline.Helpers.PilotSupply.EnsureFamily("Zeppelin NT");
                TheAirline.Helpers.PilotSupply.EnsureAnyPilots();
                int pool = TheAirline.Models.Pilots.Pilots.GetPilots().Count(p0 => p0.Airline == null);
                Console.WriteLine("  new family: created " + made + ", again " + madeAgain + ", pool " + pool + ", for the family " + TheAirline.Helpers.PilotSupply.CountFor("Zeppelin NT"));
                if (made != TheAirline.Helpers.PilotSupply.MinPerFamily || madeAgain != 0 || pool < TheAirline.Helpers.PilotSupply.MinPilots) { Console.WriteLine("  FAILED pilot supply"); Environment.Exit(1); }
                foreach (var p1 in TheAirline.Models.Pilots.Pilots.GetPilots().Where(p1 => p1.Airline == null && p1.Aircrafts != null && p1.Aircrafts.Contains("Zeppelin NT")).ToList()) p1.Airline = hs; //all taken
                int gone = TheAirline.Helpers.PilotSupply.CountFor("Zeppelin NT");
                var refill = TheAirline.Helpers.PilotSupply.EnsureFamily("Zeppelin NT");
                Console.WriteLine("  all taken: " + gone + " left, refilled with " + refill);
                foreach (var p1 in TheAirline.Models.Pilots.Pilots.GetPilots().Where(p1 => p1.Airline == hs && p1.Aircrafts != null && p1.Aircrafts.Contains("Zeppelin NT") && !hs.Pilots.Contains(p1)).ToList()) p1.Airline = null;
                if (gone != 0 || refill != TheAirline.Helpers.PilotSupply.MinPerFamily) { Console.WriteLine("  FAILED refill"); Environment.Exit(1); }

                // flight school
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(hs, gs.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.AirlineExpenses, 50000000);
                var fsv = new FlightSchoolsViewModel(() => { });
                Console.WriteLine("  " + fsv.Summary + " | airports to build at: " + fsv.BuildAirports.Count);
                if (fsv.CanBuild)
                {
                    fsv.BuildCommand.Execute(null);
                    Console.WriteLine("  pending: " + fsv.PendingText);
                    fsv.ConfirmCommand.Execute(null);
                }
                else
                {
                    Console.WriteLine("  (no airport can take a school, building one directly)");
                    hs.AddFlightSchool(new TheAirline.Models.Pilots.FlightSchool(hs.Airports[0]));
                    fsv.Reload();
                }
                Console.WriteLine("  built: " + fsv.Status + " | schools " + fsv.Schools.Count);
                if (fsv.Schools.Count != 1 || fsv.Selected == null) { Console.WriteLine("  FAILED build"); Environment.Exit(1); }
                var school = fsv.Selected.School;
                string blocked0 = TheAirline.Helpers.FlightSchoolService.GetStudentBlocker(hs, school);
                Console.WriteLine("  no instructors: " + blocked0);
                if (blocked0 == null) { Console.WriteLine("  FAILED blocker"); Environment.Exit(1); }
                Console.WriteLine("  instructors for hire: " + fsv.Market.Count);
                for (int ii = 0; ii < 2; ii++)
                {
                    fsv.Market[0].Command.Execute(null);
                    fsv.ConfirmCommand.Execute(null);
                }
                Console.WriteLine("  hired: " + fsv.Status + " | instructors " + fsv.Instructors.Count + " | " + fsv.Instructors[0].Text + " | " + fsv.Instructors[0].Detail);
                string blocked1 = TheAirline.Helpers.FlightSchoolService.GetStudentBlocker(hs, school);
                Console.WriteLine("  no aircraft: " + blocked1);
                if (fsv.Instructors.Count != 2 || blocked1 == null) { Console.WriteLine("  FAILED hire"); Environment.Exit(1); }
                fsv.BuyType = fsv.AircraftTypes[0];
                double mAir = gs.HumanMoney;
                fsv.BuyAircraftCommand.Execute(null);
                Console.WriteLine("  pending: " + fsv.PendingText);
                fsv.ConfirmCommand.Execute(null);
                Console.WriteLine("  bought: " + fsv.Status + " | aircraft " + fsv.Aircraft.Count + " | " + fsv.Aircraft[0].Detail + " | money " + (gs.HumanMoney - mAir).ToString("0"));
                if (fsv.Aircraft.Count != 1 || !(gs.HumanMoney < mAir)) { Console.WriteLine("  FAILED buy aircraft"); Environment.Exit(1); }
                Console.WriteLine("  families: " + fsv.Families.Count + " selected " + fsv.Family + " | " + fsv.StudentHint);
                double mStud = gs.HumanMoney;
                for (int ii = 0; ii < 3; ii++)
                {
                    fsv.StartStudentCommand.Execute(null);
                    if (fsv.HasPending) fsv.ConfirmCommand.Execute(null);
                    Console.WriteLine("  student " + (ii + 1) + ": " + fsv.Status);
                }
                Console.WriteLine("  students " + fsv.Students.Count + " | " + fsv.Students[0].Text + " | " + fsv.Students[0].Detail + " | money " + (gs.HumanMoney - mStud).ToString("0"));
                if (fsv.Students.Count != 2 || !(gs.HumanMoney < mStud)) { Console.WriteLine("  FAILED students (the Cessna has 2 seats)"); Environment.Exit(1); }
                string m5;
                bool sold = TheAirline.Helpers.FlightSchoolService.SellAircraft(hs, school, school.TrainingAircrafts[0], out m5);
                bool dism = TheAirline.Helpers.FlightSchoolService.DismissInstructor(school, school.Instructors.First(i0 => i0.Students.Count > 0), out string m6);
                Console.WriteLine("  sell with students: " + sold + " | " + m5 + " | dismiss with students: " + dism + " | " + m6);
                if (sold || dism) { Console.WriteLine("  FAILED guards"); Environment.Exit(1); }

                // graduation
                var grad = school.Students[0];
                string fam = grad.AirlinerFamily;
                grad.EndDate = gs.GameTime;
                int pilotsBefore = hs.Pilots.Count;
                typeof(TheAirline.Helpers.GameObjectHelpers).GetMethod("DoDailyUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
                var graduate = hs.Pilots.FirstOrDefault(p2 => p2.Profile == grad.Profile);
                Console.WriteLine("  graduation: pilots " + pilotsBefore + " -> " + hs.Pilots.Count + " | students left " + school.Students.Count + " | pilot " + (graduate != null ? graduate.Profile.Name + " flies " + string.Join(",", graduate.Aircrafts) + " on " + (graduate.Airliner != null ? graduate.Airliner.Name : "no aircraft yet") : "MISSING"));
                if (graduate == null || !graduate.Aircrafts.Contains(fam) || school.Students.Count != 1 || grad.Instructor != null) { Console.WriteLine("  FAILED graduation"); Environment.Exit(1); }

                fsv.Reload();
                double mClose = gs.HumanMoney;
                fsv.CloseCommand.Execute(null);
                fsv.ConfirmCommand.Execute(null);
                Console.WriteLine("  closed: " + fsv.Status + " | schools " + fsv.Schools.Count + " | money " + (gs.HumanMoney - mClose).ToString("0") + " | instructors back on the market: " + TheAirline.Models.Pilots.Instructors.GetUnassignedInstructors().Count(i1 => school.Instructors.Contains(i1)));
                if (fsv.Schools.Count != 0 || !(gs.HumanMoney > mClose)) { Console.WriteLine("  FAILED close"); Environment.Exit(1); }

                // the pilots tab: a family with nobody for hire is refilled
                var pvm = new PilotsViewModel(() => { });
                int ni = pvm.Available.Count;
                Console.WriteLine("  pilots tab: " + ni + " pilots for hire");
                if (ni == 0) { Console.WriteLine("  FAILED pilots for hire"); Environment.Exit(1); }

                // type training for a pilot without an aircraft
                pvm.Reload();
                Console.WriteLine("  your pilots: " + pvm.Hired.Count + ", marked without aircraft: " + pvm.Hired.Count(h => h.IsIdle));
                if (!pvm.Hired.Any(h => h.IsIdle && h.Pilot == graduate)) { Console.WriteLine("  FAILED idle marker"); Environment.Exit(1); }
                pvm.TrainPilot = pvm.TrainPilots.First(c => c.Value == graduate);
                Console.WriteLine("  train: " + pvm.TrainPilots.Count + " pilot(s) without aircraft, " + pvm.TrainFamilies.Count + " families, first: " + pvm.TrainFamilies[0].Name + " | " + pvm.TrainHint);
                string tf = pvm.TrainFamilies[0].Value;
                double mTrain = gs.HumanMoney;
                pvm.TrainCommand.Execute(null);
                Console.WriteLine("  pending: " + pvm.PendingText);
                pvm.ConfirmCommand.Execute(null);
                Console.WriteLine("  trained: " + pvm.Status + " | money " + (gs.HumanMoney - mTrain).ToString("0") + " | " + pvm.Hired.First(h => h.Pilot == graduate).Assignment);
                if (graduate.Training == null || graduate.Training.AirlinerFamily != tf || !(gs.HumanMoney < mTrain) || pvm.TrainPilots.Any(c => c.Value == graduate)) { Console.WriteLine("  FAILED training start"); Environment.Exit(1); }
                string mt;
                bool again2 = TheAirline.Helpers.FlightSchoolService.TrainPilot(hs, graduate, tf, out mt);
                Console.WriteLine("  twice: " + again2 + " | " + mt);
                graduate.Training.EndDate = gs.GameTime;
                typeof(TheAirline.Helpers.GameObjectHelpers).GetMethod("DoDailyUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
                Console.WriteLine("  after training: flies " + string.Join(",", graduate.Aircrafts) + " | training " + (graduate.Training == null ? "done" : "still on"));
                if (again2 || graduate.Training != null || !graduate.Aircrafts.Contains(tf)) { Console.WriteLine("  FAILED training end"); Environment.Exit(1); }

                // assigning pilots
                var manned = hs.Fleet.First(f0 => f0.Pilots.Count > 0);
                var q = manned.Pilots[0];
                pvm.AutoAssign = false;
                string ma;
                bool un = TheAirline.Helpers.FlightSchoolService.UnassignPilot(hs, q, out ma);
                Console.WriteLine("  unassign: " + un + " | " + ma);
                pvm.Reload();
                pvm.AssignPilot = pvm.AssignPilots.First(c => c.Value == q);
                Console.WriteLine("  assign: " + pvm.AssignPilots.Count + " pilots, first: " + pvm.AssignPilots[0].Name + " | targets " + string.Join(";", pvm.AssignTargets.Select(c => c.Name)) + " | " + pvm.AssignHint);
                if (!un || q.Airliner != null || !pvm.AssignTargets.Any(c => c.Value == manned)) { Console.WriteLine("  FAILED assign targets"); Environment.Exit(1); }
                pvm.AssignTarget = pvm.AssignTargets.First(c => c.Value == manned);
                pvm.AssignCommand.Execute(null);
                Console.WriteLine("  assigned: " + pvm.Status);
                if (q.Airliner != manned || !manned.Pilots.Contains(q)) { Console.WriteLine("  FAILED assign"); Environment.Exit(1); }
                string mb;
                bool wrong = TheAirline.Helpers.FlightSchoolService.AssignPilot(hs, graduate, manned, out mb);
                Console.WriteLine("  wrong type or full: " + wrong + " | " + mb);
                if (wrong) { Console.WriteLine("  FAILED assign guard"); Environment.Exit(1); }
                //automatic: switching it on puts idle pilots on the aircraft that need them
                TheAirline.Helpers.FlightSchoolService.UnassignPilot(hs, q, out ma);
                pvm.AutoAssign = true;
                Console.WriteLine("  automatic on: " + pvm.Status + " | " + q.Profile.Name + " -> " + (q.Airliner != null ? q.Airliner.Name : "no aircraft"));
                if (!hs.AutoAssignPilots || q.Airliner != manned) { Console.WriteLine("  FAILED auto assign"); Environment.Exit(1); }
            }
            {
                Console.WriteLine("CHARTER TEST");
                var cg = TheAirline.Models.General.GameObject.GetInstance();
                var ca = cg.HumanAirline;
                TheAirline.Helpers.CharterService.Clear();
                TheAirline.Helpers.CharterService.Refresh(true);
                Console.WriteLine("  random offers: " + TheAirline.Helpers.CharterService.Offers.Count);
                if (TheAirline.Helpers.CharterService.Offers.Count == 0) { Console.WriteLine("  FAILED no offers"); Environment.Exit(1); }
                var cf = ca.Fleet.FirstOrDefault(x => x.Airliner.BuiltDate <= cg.GameTime && x.Airliner.Type is TheAirline.Models.Airliners.AirlinerPassengerType && x.Pilots.Count >= x.Airliner.Type.CockpitCrew && x.CurrentPosition != null);
                if (cf == null) { Console.WriteLine("  (no crewed passenger aircraft, skipped)"); }
                else
                {
                    var savedRoutes = cf.Routes;
                    var withRoutes = savedRoutes.Count;
                    var dest = TheAirline.Models.Airports.Airports.GetAllActiveAirports().First(x => x != cf.CurrentPosition && TheAirline.Helpers.MathHelpers.GetDistance(cf.CurrentPosition, x) > 300 && TheAirline.Helpers.MathHelpers.GetDistance(cf.CurrentPosition, x) < cf.Airliner.Type.Range * 0.8 && x.GetMaxRunwayLength() >= cf.Airliner.Type.MinRunwaylength);
                    var offer = new TheAirline.Helpers.CharterOffer { IsCargo = false, Amount = Math.Min(20, TheAirline.Helpers.CharterService.CapacityOf(cf)), From = cf.CurrentPosition, To = dest, Distance = TheAirline.Helpers.MathHelpers.GetDistance(cf.CurrentPosition, dest), Fee = 400000, Deadline = cg.GameTime.AddDays(10), Customer = "a test" };
                    TheAirline.Helpers.CharterService.AddOffer(offer);
                    if (withRoutes > 0)
                    {
                        Console.WriteLine("  with routes: " + TheAirline.Helpers.CharterService.GetBlocker(cf, offer));
                        if (TheAirline.Helpers.CharterService.GetBlocker(cf, offer) != "has routes") { Console.WriteLine("  FAILED routes guard"); Environment.Exit(1); }
                        cf.Routes = new System.Collections.Generic.List<TheAirline.Models.Routes.Route>();
                    }
                    var quote = TheAirline.Helpers.CharterService.Quote(cf, offer);
                    Console.WriteLine("  quote: " + quote.Days + " days, cost " + quote.Cost.ToString("N0") + ", profit " + quote.Profit.ToString("N0"));
                    var cvm = new TheAirline.Presentation.CharterViewModel(() => { });
                    var item = cvm.Offers.FirstOrDefault(x => x.Name == offer.Title && x.Aircraft.Count > 0);
                    if (item != null) { var named = ca.Fleet.FirstOrDefault(x => item.Aircraft[0].Text.StartsWith(x.Name + " (")); if (named != null && named != cf) { if (withRoutes > 0) cf.Routes = savedRoutes; cf = named; savedRoutes = cf.Routes; withRoutes = savedRoutes.Count; } }
                    Console.WriteLine("  blocker for " + cf.Name + ": " + (TheAirline.Helpers.CharterService.GetBlocker(cf, offer) ?? "none") + " | candidates " + TheAirline.Helpers.CharterService.Candidates(ca, offer).Count);
                    Console.WriteLine("  view model offers: " + cvm.Offers.Count + ", ours listed: " + (item != null));
                    if (item == null) { Console.WriteLine("  FAILED offer not in view model"); Environment.Exit(1); }
                    item.Aircraft[0].SendCommand.Execute(null);
                    Console.WriteLine("  pending: " + cvm.PendingText);
                    cvm.ConfirmCommand.Execute(null);
                    Console.WriteLine("  sent: " + cvm.Status + " | active: " + string.Join(";", cvm.Active));
                    if (!TheAirline.Helpers.CharterService.IsOnCharter(cf) || cf.Status != TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnCharter) { Console.WriteLine("  FAILED not on charter"); Environment.Exit(1); }
                    string cmsg;
                    bool chk = TheAirline.Helpers.MaintenanceService.PerformCheck(cf, TheAirline.Helpers.MaintenanceCheck.A, out cmsg);
                    Console.WriteLine("  A check while away: " + chk + " | " + cmsg);
                    if (chk) { Console.WriteLine("  FAILED check during charter"); Environment.Exit(1); }
                    double before = ca.Money;
                    cf.CharterUntil = cg.GameTime.AddDays(-1);
                    var finished = TheAirline.Helpers.CharterService.ProcessDaily(ca);
                    Console.WriteLine("  done: " + string.Join(";", finished) + " | now at " + cf.CurrentPosition.Profile.IATACode + " | money " + (ca.Money - before).ToString("N0"));
                    if (finished.Count != 1 || cf.CurrentPosition != dest || cf.CharterDestination != null || cf.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnCharter) { Console.WriteLine("  FAILED charter end"); Environment.Exit(1); }
                    if (withRoutes > 0) cf.Routes = savedRoutes;
                }
            }
            {
                Console.WriteLine("AIRCRAFT DEALS TEST");
                var dg = TheAirline.Models.General.GameObject.GetInstance();
                var da = dg.HumanAirline;
                var df = da.Fleet.FirstOrDefault(x => x.Purchased == TheAirline.Models.Airliners.FleetAirliner.PurchasedType.Bought && x.Airliner.BuiltDate <= dg.GameTime && x.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped);
                if (df == null) { Console.WriteLine("  (no stopped owned aircraft, skipped)"); }
                else
                {
                    var dRoutes = df.Routes; df.Routes = new System.Collections.Generic.List<TheAirline.Models.Routes.Route>();
                    var dvm = new TheAirline.Presentation.AircraftDealsViewModel(() => { });
                    Console.WriteLine("  available: " + dvm.Available.Count + " | " + dvm.AvailableTitle);
                    var ditem = dvm.Available.FirstOrDefault(x => x.Name.StartsWith(df.Name + " ("));
                    if (ditem == null) { Console.WriteLine("  FAILED aircraft not offered"); Environment.Exit(1); }
                    Console.WriteLine("  " + ditem.Info + " | " + ditem.LeaseButtons[0].Text + " | " + ditem.SaleButtons[0].Text);
                    ditem.LeaseButtons[0].Command.Execute(null);
                    Console.WriteLine("  pending: " + dvm.PendingText);
                    double dm0 = da.Money;
                    dvm.ConfirmCommand.Execute(null);
                    Console.WriteLine("  lease: " + dvm.Status + " | leased rows " + dvm.LeasedOut.Count);
                    if (!TheAirline.Helpers.AircraftDealService.IsLeasedOut(df) || dvm.LeasedOut.Count != 1) { Console.WriteLine("  FAILED lease out"); Environment.Exit(1); }
                    string dmsg;
                    if (TheAirline.Helpers.MaintenanceService.PerformCheck(df, TheAirline.Helpers.MaintenanceCheck.A, out dmsg)) { Console.WriteLine("  FAILED check while leased out"); Environment.Exit(1); }
                    Console.WriteLine("  check while away: " + dmsg);
                    if (TheAirline.Helpers.CharterService.GetBlocker(df, new TheAirline.Helpers.CharterOffer { From = df.CurrentPosition, To = df.CurrentPosition }) != "leased out") { Console.WriteLine("  FAILED charter guard"); Environment.Exit(1); }
                    //rent on the 1st
                    var saveTime = dg.GameTime; var day1 = new DateTime(dg.GameTime.Year, dg.GameTime.Month, 1).AddMonths(1);
                    dg.GameTime = day1;
                    double before = da.Money;
                    TheAirline.Helpers.AircraftDealService.ProcessDaily(da);
                    Console.WriteLine("  rent on the 1st: " + (da.Money - before).ToString("N0") + " expected " + df.LeaseOutRent.ToString("N0"));
                    if (Math.Abs((da.Money - before) - df.LeaseOutRent) > 1) { Console.WriteLine("  FAILED rent"); Environment.Exit(1); }
                    var rentBefore = df.LeaseOutRent;
                    double cond0 = df.Airliner.Condition;
                    dvm.Reload();
                    dvm.LeasedOut[0].Command.Execute(null);
                    dvm.ConfirmCommand.Execute(null);
                    Console.WriteLine("  end early: " + dvm.Status + " | condition " + cond0.ToString("0.0") + " -> " + df.Airliner.Condition.ToString("0.0"));
                    if (TheAirline.Helpers.AircraftDealService.IsLeasedOut(df) || df.LeaseOutTo != null) { Console.WriteLine("  FAILED end lease"); Environment.Exit(1); }
                    dg.GameTime = saveTime;
                    //sale
                    dvm.Reload();
                    ditem = dvm.Available.First(x => x.Name.StartsWith(df.Name + " ("));
                    ditem.SaleButtons[0].Command.Execute(null);
                    dvm.ConfirmCommand.Execute(null);
                    Console.WriteLine("  list: " + dvm.Status + " | listed rows " + dvm.Listed.Count + " | " + (dvm.Listed.Count > 0 ? dvm.Listed[0].Text : ""));
                    if (!TheAirline.Helpers.AircraftDealService.IsListed(df)) { Console.WriteLine("  FAILED list"); Environment.Exit(1); }
                    dvm.Listed[0].Command.Execute(null);
                    if (TheAirline.Helpers.AircraftDealService.IsListed(df)) { Console.WriteLine("  FAILED withdraw"); Environment.Exit(1); }
                    string lm; TheAirline.Helpers.AircraftDealService.List(df, 0.9, out lm);
                    double sm = da.Money; int sold = 0;
                    for (int d = 0; d < 400 && da.Fleet.Contains(df); d++) { TheAirline.Helpers.AircraftDealService.ProcessDaily(da); sold++; }
                    Console.WriteLine("  sold after " + sold + " tries: in fleet " + da.Fleet.Contains(df) + " | money " + (da.Money - sm).ToString("N0"));
                    if (da.Fleet.Contains(df) || da.Money <= sm) { Console.WriteLine("  FAILED sale"); Environment.Exit(1); }
                    if (!da.Fleet.Contains(df)) { /* gone */ }
                }
            }
            {
                Console.WriteLine("PILOT PAY AND MORALE TEST");
                var pg = TheAirline.Models.General.GameObject.GetInstance();
                var pa = pg.HumanAirline;
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(pa, pg.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 50000000);
                var pmv = new TheAirline.Presentation.PilotsViewModel(() => { });
                Console.WriteLine("  " + pmv.MoraleText + " | " + pmv.PayText);
                if (pa.Pilots.Count == 0) { Console.WriteLine("  FAILED no pilots"); Environment.Exit(1); }
                double pay100 = TheAirline.Helpers.PilotMoraleService.Payroll(pa, 100), pay130 = TheAirline.Helpers.PilotMoraleService.Payroll(pa, 130);
                Console.WriteLine("  payroll 100%: " + pay100.ToString("N0") + " 130%: " + pay130.ToString("N0"));
                if (pay130 <= pay100 * 1.25) { Console.WriteLine("  FAILED payroll scales"); Environment.Exit(1); }
                pmv.PayLevel = pmv.PayLevels.First(c => c.Value == 130);
                Console.WriteLine("  set pay: " + pmv.Status + " | percent " + pa.PilotPayPercent);
                if (pa.PilotPayPercent != 130) { Console.WriteLine("  FAILED set pay"); Environment.Exit(1); }
                var anyPilot = pa.Pilots[0];
                double before = TheAirline.Helpers.AirlineHelpers.GetPilotSalary(pa, anyPilot);
                pa.PilotPayPercent = 100;
                double after = TheAirline.Helpers.AirlineHelpers.GetPilotSalary(pa, anyPilot);
                Console.WriteLine("  one pilot salary 130%: " + before.ToString("N2") + " 100%: " + after.ToString("N2"));
                if (Math.Abs(before / after - 1.3) > 0.001) { Console.WriteLine("  FAILED salary factor"); Environment.Exit(1); }
                //low pay -> morale falls and pilots quit
                pa.PilotPayPercent = 70;
                foreach (var pp in pa.Pilots.ToList()) pp.Morale = 40;
                int before0 = pa.Pilots.Count; int quits = 0;
                // only idle pilots fall low enough to quit at 70% pay (pilots on aircraft settle near 38)
                bool anyIdle = pa.Pilots.Any(pp => pp.Airliner == null && pp.Training == null);
                for (int mth = 0; mth < 12; mth++) quits += TheAirline.Helpers.PilotMoraleService.ProcessMonth(pa).Count;
                Console.WriteLine("  12 months at 70%: avg morale " + TheAirline.Helpers.PilotMoraleService.Average(pa).ToString("0.0") + ", pilots " + before0 + " -> " + pa.Pilots.Count + ", quit " + quits);
                if ((anyIdle && quits == 0) || pa.Pilots.Count != before0 - quits) { Console.WriteLine("  FAILED nobody quit"); Environment.Exit(1); }
                //high pay -> morale rises
                pa.PilotPayPercent = 130;
                foreach (var pp in pa.Pilots.ToList()) pp.Morale = 40;
                for (int mth = 0; mth < 12; mth++) TheAirline.Helpers.PilotMoraleService.ProcessMonth(pa);
                double hi = TheAirline.Helpers.PilotMoraleService.Average(pa);
                Console.WriteLine("  12 months at 130%: avg morale " + hi.ToString("0.0") + " (" + TheAirline.Helpers.PilotMoraleService.MoodText(hi) + ")");
                if (hi <= 55) { Console.WriteLine("  FAILED morale should rise"); Environment.Exit(1); }
                //bonus
                pa.PilotPayPercent = 100; pa.LastPilotBonus = DateTime.MinValue;
                foreach (var pp in pa.Pilots.ToList()) pp.Morale = 50;
                pmv.Reload();
                double pm0 = pa.Money;
                pmv.BonusCommand.Execute(null);
                Console.WriteLine("  bonus asks: " + pmv.PendingText);
                pmv.ConfirmCommand.Execute(null);
                Console.WriteLine("  bonus: " + pmv.Status + " | cost " + (pm0 - pa.Money).ToString("N0") + " | avg morale " + TheAirline.Helpers.PilotMoraleService.Average(pa).ToString("0"));
                if (pa.Money >= pm0 || TheAirline.Helpers.PilotMoraleService.Average(pa) < 64) { Console.WriteLine("  FAILED bonus"); Environment.Exit(1); }
                pmv.BonusCommand.Execute(null);
                Console.WriteLine("  second bonus at once: " + pmv.Status);
                if (pmv.HasPending) { Console.WriteLine("  FAILED bonus cooldown"); Environment.Exit(1); }
                pa.PilotPayPercent = 100;
            }
            {
                Console.WriteLine("WIFI TEST");
                var wg = TheAirline.Models.General.GameObject.GetInstance();
                var wa = wg.HumanAirline;
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(wa, wg.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 50000000);
                wa.Facilities.RemoveAll(x => x.Uid == "107");
                var WiFi = TheAirline.Models.Routes.RouteFacility.FacilityType.WiFi;
                int none = TheAirline.Helpers.AirlineHelpers.GetRouteFacilities(wa, WiFi).Count;
                var svc = new TheAirline.Presentation.AirlineServicesViewModel(() => { });
                var wifiItem = svc.Facilities.FirstOrDefault(x => x.Facility.Uid == "107");
                Console.WriteLine("  year " + wg.GameTime.Year + ", route Wi-Fi options before: " + none + ", facility offered: " + (wifiItem != null));
                if (wifiItem == null) { Console.WriteLine("  FAILED facility not offered"); Environment.Exit(1); }
                wifiItem.Command.Execute(null);
                svc.ConfirmCommand.Execute(null);
                int bought = TheAirline.Helpers.AirlineHelpers.GetRouteFacilities(wa, WiFi).Count;
                Console.WriteLine("  after buying: " + svc.Status + " | options " + bought);
                if (bought <= none) { Console.WriteLine("  FAILED Wi-Fi not unlocked after buying"); Environment.Exit(1); }
                //as after loading a save: the airline holds its own copy of the facility
                var orig = wa.Facilities.First(x => x.Uid == "107");
                wa.Facilities.Remove(orig);
                wa.Facilities.Add(new TheAirline.Models.Airlines.AirlineFacility(TheAirline.Models.Airlines.AirlineFacility.Section, orig.Uid, orig.Price, orig.MonthlyCost, orig.FromYear, orig.ServiceLevel, orig.LuxuryLevel));
                int copy = TheAirline.Helpers.AirlineHelpers.GetRouteFacilities(wa, WiFi).Count;
                Console.WriteLine("  with a copy of the facility (as in a loaded save): options " + copy);
                if (copy != bought) { Console.WriteLine("  FAILED Wi-Fi lost with a copy"); Environment.Exit(1); }
                var wr = wa.Routes.OfType<TheAirline.Models.Routes.PassengerRoute>().FirstOrDefault();
                if (wr != null)
                {
                    var rsv = new TheAirline.Presentation.RouteServiceViewModel(wr);
                    var row = rsv.Classes.SelectMany(c => c.Rows).FirstOrDefault(r => r.Type == WiFi);
                    Console.WriteLine("  route service Wi-Fi row: " + (row == null ? "none" : string.Join(" / ", row.Options.Select(o => o.Label))));
                    if (row == null || row.Options.Count < 3) { Console.WriteLine("  FAILED route Wi-Fi row"); Environment.Exit(1); }
                }
            }
            {
                Console.WriteLine("AUTO FERRY TEST");
                var fg = TheAirline.Models.General.GameObject.GetInstance();
                var fa = fg.HumanAirline;
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(fa, fg.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 50000000);
                var ff = fa.Fleet.FirstOrDefault(x => x.Routes.Count > 0 && x.Routes.Any(r => r.TimeTable.Entries.Any(e => e.Airliner == x)));
                if (ff == null) { Console.WriteLine("  (no aircraft with a schedule, skipped)"); }
                else
                {
                    var fEntries = ff.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == ff)).ToList();
                    var first = fEntries.OrderBy(e => e.Time).First();
                    var dep = first.DepartureAirport;
                    var all = TheAirline.Models.Airports.Airports.GetAllActiveAirports();
                    var near = all.First(x => x != dep && TheAirline.Helpers.MathHelpers.GetDistance(x, dep) > 400 && TheAirline.Helpers.MathHelpers.GetDistance(x, dep) < ff.Airliner.Type.Range * 0.7 && x.GetMaxRunwayLength() >= ff.Airliner.Type.MinRunwaylength);
                    ff.CurrentPosition = near; ff.LastFlightDate = DateTime.MinValue; ff.FerryTo = null;
                    double fm = fa.Money;
                    bool ok = TheAirline.Helpers.FerryService.BeforeFlights(ff, fEntries);
                    Console.WriteLine("  from " + near.Profile.IATACode + " to " + dep.Profile.IATACode + ": may fly today " + ok + " | now at " + ff.CurrentPosition.Profile.IATACode + " | ferrying to " + (ff.FerryTo?.Profile.IATACode ?? "-") + " | cost " + (fm - fa.Money).ToString("N0") + " | entry at " + first.Time);
                    if (fa.Money >= fm || (ff.CurrentPosition != dep && ff.FerryTo != dep)) { Console.WriteLine("  FAILED ferry not made"); Environment.Exit(1); }
                    if (ff.FerryTo != null)
                    {
                        if (ok || !TheAirline.Helpers.FerryService.IsFerrying(ff)) { Console.WriteLine("  FAILED should be ferrying"); Environment.Exit(1); }
                        var savedTime = fg.GameTime;
                        fg.GameTime = ff.FerryUntil.AddHours(1);
                        bool later = TheAirline.Helpers.FerryService.BeforeFlights(ff, fEntries);
                        Console.WriteLine("  next day: may fly " + later + ", at " + ff.CurrentPosition.Profile.IATACode);
                        fg.GameTime = savedTime;
                        if (!later || ff.CurrentPosition != dep) { Console.WriteLine("  FAILED ferry arrival"); Environment.Exit(1); }
                    }
                    //flew yesterday: no ferry
                    ff.CurrentPosition = near; ff.LastFlightDate = fg.GameTime.Date.AddDays(-1); ff.FerryTo = null; ff.FerryUntil = DateTime.MinValue;
                    fm = fa.Money;
                    bool carry = TheAirline.Helpers.FerryService.BeforeFlights(ff, fEntries);
                    Console.WriteLine("  flew yesterday: may fly " + carry + ", still at " + ff.CurrentPosition.Profile.IATACode + ", cost " + (fm - fa.Money).ToString("N0"));
                    if (!carry || ff.CurrentPosition != near || fa.Money != fm) { Console.WriteLine("  FAILED old behaviour"); Environment.Exit(1); }
                    //too far for the aircraft
                    var far = all.OrderByDescending(x => TheAirline.Helpers.MathHelpers.GetDistance(x, dep)).First();
                    ff.CurrentPosition = far; ff.LastFlightDate = DateTime.MinValue;
                    bool stuck = TheAirline.Helpers.FerryService.BeforeFlights(ff, fEntries);
                    Console.WriteLine("  too far (" + far.Profile.IATACode + ", " + TheAirline.Helpers.MathHelpers.GetDistance(far, dep).ToString("N0") + " km, range " + ff.Airliner.Type.Range.ToString("N0") + "): may fly " + stuck + " | " + TheAirline.Helpers.FerryService.GetBlocker(ff, far, dep));
                    if (stuck) { Console.WriteLine("  FAILED should not fly"); Environment.Exit(1); }
                    ff.CurrentPosition = dep; ff.LastFlightDate = DateTime.MinValue;
                }
            }
            {
                Console.WriteLine("MANUAL FERRY TEST");
                var mg = TheAirline.Models.General.GameObject.GetInstance();
                var ma = mg.HumanAirline;
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(ma, mg.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 50000000);
                var mf = ma.Fleet.FirstOrDefault(x => x.Airliner.BuiltDate <= mg.GameTime);
                if (mf != null && mf.Pilots.Count < mf.Airliner.Type.CockpitCrew) { TheAirline.Helpers.PilotSupply.EnsureFamily(mf.Airliner.Type.AirlinerFamily); TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(mf); }
                var kiv = TheAirline.Models.Airports.Airports.GetAllAirports().FirstOrDefault(x => x.Profile.IATACode == "KIV");
                if (mf == null || kiv == null) { Console.WriteLine("  (aircraft " + (mf != null) + " or KIV " + (kiv != null) + " missing, skipped; fleet " + ma.Fleet.Count + ", crewed " + ma.Fleet.Count(x => x.Pilots.Count >= x.Airliner.Type.CockpitCrew) + ")"); }
                else
                {
                    var savedRoutes = mf.Routes; var savedStatus = mf.Status;
                    mf.Routes = new System.Collections.Generic.List<TheAirline.Models.Routes.Route>();
                    mf.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped;
                    mf.CurrentPosition = kiv; mf.FerryTo = null;
                    var home = ma.Airports.FirstOrDefault(x => x != kiv && TheAirline.Helpers.MathHelpers.GetDistance(kiv, x) > 5000) ?? mf.Homebase;
                    var plan = TheAirline.Helpers.FerryService.Plan(mf, kiv, home);
                    Console.WriteLine("  " + mf.Name + " (" + mf.Airliner.Type.Name + ", range " + mf.Airliner.Type.Range.ToString("N0") + ") KIV -> " + home.Profile.IATACode + " " + TheAirline.Helpers.MathHelpers.GetDistance(kiv, home).ToString("N0") + " km: plan " + (plan == null ? "none" : string.Join(" > ", plan.Select(x => x.Profile.IATACode))));
                    var fv = new TheAirline.Presentation.FleetOverviewViewModel(() => { }, f0 => { });
                    fv.Selected = fv.Rows.First(r => r.Aircraft == mf);
                    Console.WriteLine("  diagnose: " + fv.Details.First(d => d.StartsWith("Position")));
                    Console.WriteLine("  targets: " + fv.FerryTargets.Count + " | hint: " + fv.FerryHint);
                    if (plan != null)
                    {
                        fv.FerryTarget = fv.FerryTargets.First(c => c.Value == home);
                        fv.FerryCommand.Execute(null);
                        Console.WriteLine("  asks: " + fv.PendingText);
                        double fm2 = ma.Money;
                        fv.ConfirmCommand.Execute(null);
                        Console.WriteLine("  started: " + fv.Status + " | cost " + (fm2 - ma.Money).ToString("N0") + " | ferrying " + TheAirline.Helpers.FerryService.IsFerrying(mf));
                        if (!TheAirline.Helpers.FerryService.IsFerrying(mf) || ma.Money >= fm2) { Console.WriteLine("  FAILED ferry not started"); Environment.Exit(1); }
                        var t0 = mg.GameTime;
                        mg.GameTime = mf.FerryUntil.AddHours(1);
                        TheAirline.Helpers.FerryService.ProcessDaily(ma);
                        mg.GameTime = t0;
                        Console.WriteLine("  after arrival: at " + mf.CurrentPosition.Profile.IATACode);
                        if (mf.CurrentPosition != home) { Console.WriteLine("  FAILED ferry arrival"); Environment.Exit(1); }
                    }
                    mf.Routes = savedRoutes; mf.Status = savedStatus;
                }
            }
            {
                Console.WriteLine("LICENSE TEST");
                var lcg = TheAirline.Models.General.GameObject.GetInstance();
                var la = lcg.HumanAirline;
                var lall = TheAirline.Models.Airports.Airports.GetAllAirports();
                var atl = lall.First(x => x.Profile.IATACode == "ATL"); var lhr = lall.FirstOrDefault(x => x.Profile.IATACode == "SYD");
                if (lhr == null)
                {
                    // a North America game has no airport on another continent: read Sydney from the data files
                    // without adding it to the game
                    var keepAirports = lall.ToList();
                    TheAirline.Infrastructure.Setup.LoadAirports();
                    lhr = TheAirline.Models.Airports.Airports.GetAllAirports().First(x => x.Profile.IATACode == "SYD");
                    TheAirline.Models.Airports.Airports.Clear();
                    foreach (var ka in keepAirports) TheAirline.Models.Airports.Airports.AddAirport(ka);
                    Console.WriteLine("  (Sydney read from the data files: " + TheAirline.Models.Airports.Airports.GetAllAirports().Count + " airports in the game)");
                }
                TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(la, lcg.GameTime, TheAirline.Models.General.Finances.Invoice.InvoiceType.Rents, 100000000);
                var keepLicense = la.License;
                la.License = TheAirline.Models.Airlines.Airline.AirlineLicense.Regional;
                var lvm = new TheAirline.Presentation.LicenseViewModel(() => { });
                Console.WriteLine("  " + lvm.Current + " | " + lvm.NextText + " | ATL-SYD allowed: " + TheAirline.Helpers.AirlineHelpers.HasAirlineLicens(la, atl, lhr));
                if (TheAirline.Helpers.AirlineHelpers.HasAirlineLicens(la, atl, lhr)) { Console.WriteLine("  FAILED regional should not fly ATL-SYD"); Environment.Exit(1); }
                for (int step = 0; step < 2; step++) { lvm.UpgradeCommand.Execute(null); lvm.ConfirmCommand.Execute(null); Console.WriteLine("  " + lvm.Status); }
                Console.WriteLine("  now " + lvm.Current + " | ATL-SYD allowed: " + TheAirline.Helpers.AirlineHelpers.HasAirlineLicens(la, atl, lhr) + " | can upgrade " + lvm.CanUpgrade);
                if (!TheAirline.Helpers.AirlineHelpers.HasAirlineLicens(la, atl, lhr) || lvm.CanUpgrade || la.License != TheAirline.Models.Airlines.Airline.AirlineLicense.LongHaul) { Console.WriteLine("  FAILED long haul license"); Environment.Exit(1); }
                la.License = keepLicense;
            }
            {
                Console.WriteLine("AIRPORT TYPE TEST");
                var atm = new TheAirline.Presentation.AirportManagerViewModel(() => { });
                var cgh = atm.Airports.First(x => x.Airport.Profile.Type == TheAirline.Models.Airports.AirportProfile.AirportType.Domestic);
                atm.Selected = cgh;
                Console.WriteLine("  before: " + atm.AirportType + " | row " + cgh.Airport.Profile.Type);
                atm.AirportType = atm.AirportTypes.First(x => x.Value == TheAirline.Models.Airports.AirportProfile.AirportType.ShortHaulInternational);
                var cgh2 = atm.Airports.FirstOrDefault(x => x.Airport == cgh.Airport);
                Console.WriteLine("  after: " + cgh.Airport.Profile.Type + " | " + atm.Status + " | selected " + atm.Selected?.Airport.Profile.IATACode + " | row type " + (cgh2 as TheAirline.Presentation.AirportRow)?.Type);
                if (cgh.Airport.Profile.Type != TheAirline.Models.Airports.AirportProfile.AirportType.ShortHaulInternational) { Console.WriteLine("  FAILED airport type"); Environment.Exit(1); }
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
