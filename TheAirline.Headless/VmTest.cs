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
        // TAP_MINLOAD: the emptiest a route may be forecast and still be opened (0 lets every route through)
        double minLoad;
        if (double.TryParse(Environment.GetEnvironmentVariable("TAP_MINLOAD"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minLoad))
            TheAirline.Helpers.AIRouteChoice.MinLoad = minLoad;
        Console.WriteLine("  routes are opened only when forecast at least " + (TheAirline.Helpers.AIRouteChoice.MinLoad * 100).ToString("0") + "% full");
        int yearMonths = int.Parse(Environment.GetEnvironmentVariable("TAP_MONTHS") ?? "12");
        for (int m = 0; m < yearMonths; m++)
        {
            int startMonth = g.GameTime.Month;
            var swY = System.Diagnostics.Stopwatch.StartNew();
            while (g.GameTime.Month == startMonth) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            var last = g.GameTime.AddMonths(-1);
            var margins = new System.Collections.Generic.List<double>();
            double totalProfit = 0;
            int idle = 0;
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
                totalProfit += i2 + c2;
                idle += a.Fleet.Count(f => !f.HasRoute);
            }
            margins.Sort();
            var shared = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(a => a.Routes).OfType<TheAirline.Models.Routes.PassengerRoute>().Where(r => r.HasAirliner).ToList();
            int sharedCount = shared.Count(r => TheAirline.Helpers.AIPricingService.Rivals(r).Count > 0);
            // how the airlines are growing, and how often a search found nowhere worth flying
            var aiY = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).ToList();
            Console.WriteLine("   fleet " + aiY.Sum(a => a.Fleet.Count) + ", routes " + aiY.Sum(a => a.Routes.Count)
                              + ", airlines with routes " + aiY.Count(a => a.Routes.Count > 0)
                              + " | picked " + TheAirline.Helpers.AIRouteChoice.Picked
                              + ", looked further " + TheAirline.Helpers.AIRouteChoice.Widened
                              + ", nowhere with passengers " + TheAirline.Helpers.AIRouteChoice.TooEmpty
                              + ", given up " + TheAirline.Helpers.AIRouteChoice.Dropped);
            Console.WriteLine("-- " + last.ToString("MMM yyyy") + " (" + (swY.ElapsedMilliseconds / 1000.0).ToString("0") + " s) | " + margins.Count + " airlines earning | margin median " + (margins.Count > 0 ? margins[margins.Count / 2] : 0).ToString("0") + "%, " + margins.Count(x => x < 0) + " losing money, all together " + totalProfit.ToString("N0") + ", " + idle + " aircraft idle | " + shared.Count + " passenger routes, " + sharedCount + " shared with a rival | AI forecasts: " + TheAirline.Helpers.AIRouteChoice.Picked + " picks (" + TheAirline.Helpers.AIRouteChoice.PickedOther + " not the most demand, " + TheAirline.Helpers.AIRouteChoice.AllLosing + " all forecast to lose), " + TheAirline.Helpers.AIRouteChoice.Dropped + " dropped");
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
        // the cheapest aircraft that has a home base, and a route for it
        foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        mk.BuyCommand.Execute(null);
        av.Refresh();
        var ap = av.Airports;
        await Wait(() => !ap.IsBusy, "advice");
        ap.SelectedAdvice = ap.Advice.First(x => x.Advice.FleetCanFly);
        var cr = ap.Create;
        foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
        cr.CreateCommand.Execute(null);
        foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
        if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
        var route = h.Routes.First(r => r.HasAirliner);
        var a = route.GetAirliners()[0];
        if (a.Pilots.Count < a.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(a);
        if (a.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) a.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;

        // a second aircraft of the same kind that can fly the route: the cheapest one, of the same type when there is one
        double dist = route.GetDistance();
        var fits = mk.Aircraft.Where(x => x.Airliner.Type.TypeAirliner == a.Airliner.Type.TypeAirliner && x.Airliner.Range >= dist && x.Airliner.Type.CruisingSpeed >= a.Airliner.Type.CruisingSpeed).ToList();
        mk.Selected = fits.Where(x => x.Airliner.Type == a.Airliner.Type).OrderBy(x => x.Airliner.GetPrice()).FirstOrDefault() ?? fits.OrderBy(x => x.Airliner.GetPrice()).FirstOrDefault();
        if (mk.Selected != null) mk.BuyCommand.Execute(null);
        av.Refresh();
        Console.WriteLine("  fleet: " + string.Join(", ", h.Fleet.Select(f => f.Name + " " + f.Airliner.Type.Name)));
        var b = h.Fleet.FirstOrDefault(f => f != a);
        if (b == null) { Console.WriteLine("  FAILED could not buy a second aircraft"); Environment.Exit(1); }
        if (b.Pilots.Count < b.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(b);
        int flights = route.TimeTable.Entries.Count(e => e.Airliner == a);
        Console.WriteLine("  route " + route.Destination1.Profile.IATACode + "-" + route.Destination2.Profile.IATACode + " flown by " + a.Name + " (" + flights + " weekly flights), spare " + b.Name);
        int seats0 = route.ConnectSeats;
        if (Environment.GetEnvironmentVariable("TAP_TRACE") == "1")
        {
            int shown = 0;
            var pr = (TheAirline.Models.Routes.PassengerRoute) route;
            var eco = TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass;
            Console.WriteLine("    score " + TheAirline.Helpers.RouteHelpers.GetRouteTotalScore(route).ToString("0.00") + " | fare " + pr.GetFarePrice(eco).ToString("0.00") + " market " + TheAirline.Helpers.PricingService.MarketFare(route.Destination1, route.Destination2, eco).ToString("0.00") + " demand factor " + TheAirline.Helpers.PricingService.DemandFactor(pr, eco).ToString("0.000")
                + " | parts: age " + TheAirline.Helpers.RouteHelpers.GetPlaneAgeScore(route) + " inflight " + TheAirline.Helpers.RouteHelpers.GetRouteInflightScore(route) + " meal " + TheAirline.Helpers.RouteHelpers.GetRouteMealScore(route) + " type " + TheAirline.Helpers.RouteHelpers.GetRoutePlaneTypeScore(route) + " price " + TheAirline.Helpers.RouteHelpers.GetRoutePriceScore(route) + " seats " + TheAirline.Helpers.RouteHelpers.GetRouteSeatsScore(route) + " luggage " + TheAirline.Helpers.RouteHelpers.GetRouteLuggageScore(route));
            TheAirline.Helpers.PassengerHelpers.FlightTrace = t =>
            {
                if (t.Route != route || shown++ > 6) return;
                Console.WriteLine("    trace " + t.Type + ": base " + t.BaseDemand.ToString("0.0") + " direct " + t.Direct.ToString("0.0") + " conn " + t.Connecting.ToString("0.0") + " nearby " + t.Nearby.ToString("0.0") + " daily " + t.DailyDemand.ToString("0.0") + " flights/day " + t.FlightsPerDay.ToString("0.0") + " per flight " + t.PerFlight.ToString("0.00") + " cap " + t.TotalCapacity + " cap% " + t.CapacityPercent.ToString("0.000") + " comp " + t.Competition.ToString("0.000") + " rep " + t.Reputation.ToString("0.00") + " seats " + t.Seats + " -> " + t.Passengers);
            };
        }
        for (int i = 0; i < 3; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  " + a.Name + " flew " + (route.ConnectSeats - seats0) + " seats in 3 days (" + route.ConnectAllPassengers + " passengers counted for the load, " + route.Statistics.GetTotalValue(TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Passengers")) + " in the route statistics), status " + a.Status);
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

        int seats1 = route.ConnectSeats;
        for (int i = 0; i < 4; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        int flown = route.ConnectSeats - seats1;
        Console.WriteLine("  four days later: " + b.Name + " at " + b.CurrentPosition?.Profile.IATACode + ", status " + b.Status + " | seats flown on the route " + flown);
        if (flown <= 0) { Console.WriteLine("  FAILED the new aircraft doesn't fly the route"); Environment.Exit(1); }

        // and back again: the first aircraft is free now
        rd.OpenReplaceCommand.Execute(null);
        rd.ReplaceTo = rd.ReplaceToChoices.FirstOrDefault(c => c.Value.Airliner == a);
        Console.WriteLine("  back: " + rd.ReplaceText);
        rd.ReplaceCommand.Execute(null);
        Console.WriteLine("  " + rd.Status);
        if (!a.Routes.Contains(route) || b.Routes.Contains(route)) { Console.WriteLine("  FAILED replace back"); Environment.Exit(1); }

        // a slower aircraft: the flights as they are don't fit, so shift the times (or keep the round trips that fit)
        var slowType = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes()
            .Where(t => t.TypeAirliner == a.Airliner.Type.TypeAirliner && t.Range >= route.GetDistance() && t.CruisingSpeed < a.Airliner.Type.CruisingSpeed * 0.95
                        && t.MinRunwaylength <= Math.Min(route.Destination1.GetMaxRunwayLength(), route.Destination2.GetMaxRunwayLength()))
            .OrderBy(t => t.CruisingSpeed).FirstOrDefault();
        if (slowType == null) Console.WriteLine("  (no slower aircraft type for this route, shift part skipped)");
        else
        {
            // pack the first aircraft's flights back to back, so a slower aircraft can't fly them at these times
            var packed = route.TimeTable.Entries.Where(e => e.Airliner == a).OrderBy(e => (int) e.Day*1440 + e.Time.TotalMinutes).ToList();
            int t0 = (int) packed[0].Day*1440 + (int) packed[0].Time.TotalMinutes;
            int needA = (int) Math.Ceiling(route.GetFlightTime(a.Airliner.Type).TotalMinutes + TheAirline.Helpers.FleetAirlinerHelpers.GetMinTimeBetweenFlights(a).TotalMinutes);
            for (int k = 0; k < packed.Count; k++)
            {
                var e = packed[k];
                int m = t0 % (7*1440);
                e.Day = (DayOfWeek) (m/1440); e.Time = new TimeSpan(0, m%1440, 0);
                // room inside each round trip, none between them: a slower aircraft can keep every other trip, or shift
                t0 += k % 2 == 0 ? needA*2 : needA + 5;
            }
            Console.WriteLine("  packed " + packed.Count + " flights of " + a.Name + " back to back, clash " + (TheAirline.Helpers.AircraftReplaceService.FirstClash(a, packed) ?? "none"));
            var slow = TheAirline.Helpers.AirlineHelpers.CreateAirliner(h, slowType);
            h.AddAirliner(slow);
            TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(slow);
            rd.OpenReplaceCommand.Execute(null);
            var choice = rd.ReplaceToChoices.FirstOrDefault(c => c.Value.Airliner == slow);
            Console.WriteLine("  slow " + slow.Name + " (" + slowType.Name + ", " + slowType.CruisingSpeed + " km/h vs " + a.Airliner.Type.CruisingSpeed + "): " + choice?.Name);
            rd.ReplaceTo = choice;
            Console.WriteLine("  " + rd.ReplaceText + " | shift: " + rd.ShiftText + " | keep: " + rd.KeepText + " | same allowed " + rd.CanReplaceSame);
            if (choice == null || rd.CanReplaceSame || (!rd.CanShift && !rd.CanKeep)) { Console.WriteLine("  FAILED slow aircraft options"); Environment.Exit(1); }
            int total = route.TimeTable.Entries.Count(e => e.Airliner == a);
            if (rd.CanKeep)
            {
                var keepPlan = choice.Value.Keep;
                var kept = route.TimeTable.Entries.Where(e => e.Airliner == a && !keepPlan.Drop.Contains(e)).ToList();
                string kc = TheAirline.Helpers.AircraftReplaceService.FirstClash(slow, kept);
                Console.WriteLine("  keep plan: " + kept.Count + " of " + total + " kept, clash " + (kc ?? "none"));
                if (kc != null) { Console.WriteLine("  FAILED keep plan clashes"); Environment.Exit(1); }
            }
            if (rd.CanShift)
            {
                rd.ShiftReplaceCommand.Execute(null);
                Console.WriteLine("  shifted: " + rd.Status);
                var now = route.TimeTable.Entries.Where(e => e.Airliner == slow).ToList();
                string clash = TheAirline.Helpers.AircraftReplaceService.FirstClash(slow, now);
                Console.WriteLine("  " + slow.Name + " has " + now.Count + " of " + total + " flights, clash " + (clash ?? "none") + ", status " + slow.Status);
                if (now.Count != total || clash != null || a.Routes.Contains(route)) { Console.WriteLine("  FAILED shift"); Environment.Exit(1); }
                // a whole week: the packed timetable has flights on a few days only
                int seats2 = route.ConnectSeats + route.ConnectLastSeats;
                int month2 = route.ConnectMonth;
                for (int i = 0; i < 7; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
                int flownWeek = route.ConnectMonth == month2 ? route.ConnectSeats + route.ConnectLastSeats - seats2 : route.ConnectSeats + route.ConnectLastSeats;
                Console.WriteLine("  a week later: seats flown " + flownWeek);
                if (flownWeek <= 0) { Console.WriteLine("  FAILED the slow aircraft doesn't fly"); Environment.Exit(1); }
                // put the first aircraft back for the last check
                string back;
                TheAirline.Helpers.AircraftReplaceService.Replace(route, slow, a, out back);
                Console.WriteLine("  back: " + back);
                // pack again and keep only the round trips that fit
                var again = route.TimeTable.Entries.Where(e => e.Airliner == a).OrderBy(e => (int) e.Day*1440 + e.Time.TotalMinutes).ToList();
                int t1 = (int) again[0].Day*1440 + (int) again[0].Time.TotalMinutes;
                for (int k = 0; k < again.Count; k++)
                {
                    int m = t1 % (7*1440);
                    again[k].Day = (DayOfWeek) (m/1440); again[k].Time = new TimeSpan(0, m%1440, 0);
                    t1 += k % 2 == 0 ? needA*2 : needA + 5;
                }
                string keptMsg;
                bool keptOk = TheAirline.Helpers.AircraftReplaceService.Replace(route, a, slow, TheAirline.Helpers.ReplaceMode.KeepFitting, out keptMsg);
                var left = route.TimeTable.Entries.Where(e => e.Airliner == slow).ToList();
                Console.WriteLine("  keep: " + keptOk + " | " + keptMsg + " | " + left.Count + " flights, clash " + (TheAirline.Helpers.AircraftReplaceService.FirstClash(slow, left) ?? "none") + ", route has " + route.TimeTable.Entries.Count + " flights");
                if (!keptOk || left.Count == 0 || left.Count >= total || TheAirline.Helpers.AircraftReplaceService.FirstClash(slow, left) != null || route.TimeTable.Entries.Count != left.Count)
                { Console.WriteLine("  FAILED keep"); Environment.Exit(1); }
                TheAirline.Helpers.AircraftReplaceService.Replace(route, slow, a, out back);
            }
        }

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

    /// <summary>"vm renewal": a route flown by an old type and an idle one of that type, renewed to another type from the market.</summary>
    static async Task RenewalTest(AirlineViewModel av)
    {
        Console.WriteLine("RENEWAL TEST");
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var mk = av.Market;
        foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        mk.BuyCommand.Execute(null);
        av.Refresh();
        var ap = av.Airports;
        await Wait(() => !ap.IsBusy, "advice");
        ap.SelectedAdvice = ap.Advice.First(x => x.Advice.FleetCanFly);
        var cr = ap.Create;
        foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
        cr.CreateCommand.Execute(null);
        foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
        if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
        var route = h.Routes.First(r => r.HasAirliner);
        var a = route.GetAirliners()[0];
        if (a.Pilots.Count < a.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(a);
        if (a.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) a.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;
        var oldType = a.Airliner.Type;
        // an idle one of the same type
        var idleOld = TheAirline.Helpers.AirlineHelpers.CreateAirliner(h, oldType);
        h.AddAirliner(idleOld);
        for (int i = 0; i < 2; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var oldTimes = route.TimeTable.Entries.Where(e => e.Airliner == a).Select(e => e.Day + " " + e.Time).OrderBy(x => x).ToList();
        Console.WriteLine("  route " + route.Destination1.Profile.IATACode + "-" + route.Destination2.Profile.IATACode + " flown by " + a.Name + " (" + oldType.Name + ", " + oldTimes.Count + " weekly flights); idle " + idleOld.Name);

        var rn = av.Renewal;
        rn.Reload();
        Console.WriteLine("  old types: " + string.Join(" / ", rn.OldTypes.Select(t => t.Name)));
        rn.OldType = rn.OldTypes.First(t => t.Value == oldType);
        Console.WriteLine("  new types: " + rn.NewTypes.Count + ", e.g. " + string.Join(" / ", rn.NewTypes.Take(4).Select(t => t.Name)));
        // a type from the market that can fly the route, as fast or faster (same times)
        var pick = rn.NewTypes.Where(t => t.Value.Range >= route.GetDistance() && t.Value.CruisingSpeed >= oldType.CruisingSpeed && t.Value.MinRunwaylength <= Math.Min(route.Destination1.GetMaxRunwayLength(), route.Destination2.GetMaxRunwayLength()))
                              .OrderBy(t => TheAirline.Models.Airliners.Airliners.GetAirlinersForSale(x => x.Type == t.Value).Select(x => (double) x.GetPrice()).DefaultIfEmpty(double.MaxValue).Min()).FirstOrDefault();
        if (pick == null) { Console.WriteLine("  FAILED no new type to pick"); Environment.Exit(1); }
        rn.NewType = pick;
        Console.WriteLine("  picked " + pick.Name);
        Console.WriteLine("  " + rn.Summary);
        foreach (var r in rn.Rows) Console.WriteLine("    " + r);
        double money0 = h.Money;
        rn.RenewCommand.Execute(null);
        Console.WriteLine("  asks: " + rn.PendingText);
        rn.ConfirmCommand.Execute(null);
        await Wait(() => rn.Status == "Done." || (rn.Status ?? "").StartsWith("That failed"), "renewal");
        foreach (var r in rn.Report) Console.WriteLine("    done: " + r);
        var now = route.GetAirliners();
        var nw = now.FirstOrDefault();
        var newTimes = route.TimeTable.Entries.Where(e => e.Airliner == nw).Select(e => e.Day + " " + e.Time).OrderBy(x => x).ToList();
        Console.WriteLine("  route now flown by " + string.Join(", ", now.Select(f => f.Name + " (" + f.Airliner.Type.Name + ", pilots " + f.Pilots.Count + "/" + f.Airliner.Type.CockpitCrew + ", " + f.Status + ")"))
                          + " | same times " + oldTimes.SequenceEqual(newTimes) + " | old ones in the fleet " + h.Fleet.Count(f => f.Airliner.Type == oldType) + " | money " + money0.ToString("N0") + " -> " + h.Money.ToString("N0"));
        if (nw == null || nw.Airliner.Type != pick.Value || !oldTimes.SequenceEqual(newTimes) || h.Fleet.Any(f => f.Airliner.Type == oldType) || nw.Pilots.Count < nw.Airliner.Type.CockpitCrew)
        { Console.WriteLine("  FAILED renewal"); Environment.Exit(1); }
        int seats = route.ConnectSeats;
        for (int i = 0; i < 3; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  three days later: seats flown " + (route.ConnectSeats - seats));
        if (route.ConnectSeats - seats <= 0) { Console.WriteLine("  FAILED the new aircraft doesn't fly"); Environment.Exit(1); }
        Console.WriteLine("  after: " + string.Join(" / ", rn.OldTypes.Select(t => t.Name)));
        Console.WriteLine("RENEWAL TEST OK");
    }

    /// <summary>"vm routeprofit": two routes flown for six weeks, then the route profit report.</summary>
    static async Task RouteProfitTest(AirlineViewModel av)
    {
        Console.WriteLine("ROUTE PROFIT TEST");
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var mk = av.Market;
        var ap = av.Airports;
        for (int n = 0; n < 2; n++)
        {
            foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Skip(n).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
            mk.BuyCommand.Execute(null);
            av.Refresh();
            ap.ReloadAdvice();
            await Wait(() => !ap.IsBusy, "advice");
            var advice = ap.Advice.Where(x => x.Advice.FleetCanFly).FirstOrDefault(x => !h.Routes.Any(r => r.Destination1 == x.Advice.Destination || r.Destination2 == x.Advice.Destination))
                         ?? ap.Advice.FirstOrDefault(x => !h.Routes.Any(r => r.Destination1 == x.Advice.Destination || r.Destination2 == x.Advice.Destination));
            if (advice == null) { Console.WriteLine("  no advice for route " + (n + 1) + " (" + ap.Advice.Count + " shown)"); break; }
            ap.SelectedAdvice = advice;
            var cr = ap.Create;
            foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
            cr.CreateCommand.Execute(null);
            foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
            if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
        }
        foreach (var f in h.Fleet)
        {
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f);
            if (f.Routes.Count > 0 && f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) f.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;
        }
        Console.WriteLine("  routes: " + string.Join(", ", h.Routes.Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode + " (" + string.Join(",", r.GetAirliners().Select(x => x.Name)) + ")")));
        if (h.Routes.Count < 2) { Console.WriteLine("  FAILED could not make two routes"); Environment.Exit(1); }
        for (int i = 0; i < 45; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();

        var rp = av.RouteProfits;
        rp.Reload();
        Console.WriteLine("  " + rp.Summary);
        foreach (var r in rp.Rows) Console.WriteLine("    " + r.Name + " | " + r.Profit + " | " + r.Numbers + " | " + r.Advice);
        if (rp.Rows.Count != h.Routes.Count) { Console.WriteLine("  FAILED rows"); Environment.Exit(1); }
        // the report adds up what the route's own invoices say
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var last = g.GameTime.AddMonths(-1);
        foreach (var route in h.Routes)
        {
            double report = TheAirline.Helpers.RouteProfitService.Month(route, last.Year, last.Month);
            double invoices = route.GetInvoices().GetAmount(last.Year, last.Month);
            if (Math.Abs(report - invoices) > 1) { Console.WriteLine("  FAILED month figure " + report + " vs " + invoices); Environment.Exit(1); }
        }
        double reportTotal = rp.Rows.Sum(r => r.Figures.LastMonth);
        if (Math.Abs(reportTotal) < 1) { Console.WriteLine("  FAILED no money on the routes last month"); Environment.Exit(1); }
        rp.Selected = rp.Rows.First(r => r.Figures.Route.HasAirliner);
        Console.WriteLine("  detail: " + rp.Detail);
        foreach (var m in rp.Months) Console.WriteLine("    " + m);
        foreach (var b in rp.Breakdown) Console.WriteLine("    " + b);
        if (rp.Months.Count == 0 || rp.Breakdown.Count == 0) { Console.WriteLine("  FAILED detail"); Environment.Exit(1); }
        // sorting and filters
        rp.SortBy = "Loss first";
        var lossFirst = rp.Rows.Select(r => r.Figures.Last12).ToList();
        if (!lossFirst.SequenceEqual(lossFirst.OrderBy(x => x))) { Console.WriteLine("  FAILED sort"); Environment.Exit(1); }
        rp.Show = "Losing money";
        Console.WriteLine("  losing: " + rp.Rows.Count + (rp.Rows.All(r => r.IsLoss) ? "" : " FAILED"));
        if (!rp.Rows.All(r => r.IsLoss)) Environment.Exit(1);
        rp.Show = "All routes";
        // open a route on the routes page
        av.Refresh();
        av.RoutesTab = 3;
        rp.Selected = rp.Rows.Last();
        rp.OpenCommand.Execute(null);
        Console.WriteLine("  open: tab " + av.RoutesTab + ", selected " + av.SelectedRoute?.Name);
        if (av.RoutesTab != 0 || av.SelectedRoute?.Route != rp.Selected.Figures.Route) { Console.WriteLine("  FAILED open"); Environment.Exit(1); }
        Console.WriteLine("ROUTE PROFIT TEST OK");
    }

    /// <summary>
    ///     "vm forecast": the route planner suggests a number of round trips from a forecast; two routes are made (one at
    ///     the suggestion, one at the most the aircraft can fly), flown for a month, and the forecast is compared.
    /// </summary>
    // the world map's aircraft glide between reads of the game: the clock and the positions along a flight
    static void MapAnimTest(AirlineViewModel av)
    {
        Console.WriteLine("MAP ANIMATION TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };

        // 1. the clock: game time runs on at the game's speed between reads, never backwards, and stops when paused
        var clock = new TheAirline.Presentation.MapClock();
        var t0 = new DateTime(2007, 1, 1, 8, 0, 0);
        clock.Sample(0, t0, false);
        clock.Sample(0.5, t0.AddMinutes(30), false);
        clock.Sample(1.0, t0.AddMinutes(60), false);
        Console.WriteLine("  rate " + clock.Rate.ToString("0") + " game seconds a second");
        if (Math.Abs(clock.Rate - 3600) > 1) fail("rate " + clock.Rate);
        DateTime prev = clock.Now(1.0);
        for (double w = 1.033; w < 1.6; w += 0.033)
        {
            var n = clock.Now(w);
            if (n < prev) fail("the clock ran backwards at " + w);
            prev = n;
        }
        if (prev <= t0.AddMinutes(60)) fail("the clock didn't run on between reads: " + prev);
        if (prev > t0.AddMinutes(60 + 60 + 1)) fail("the clock ran too far ahead of the game: " + prev);
        Console.WriteLine("  between reads it reached " + prev.ToString("HH:mm:ss") + " (last read 09:00)");
        clock.Sample(1.6, t0.AddMinutes(80), true);
        var paused = clock.Now(1.7);
        if (clock.Running || clock.Now(3.0) != paused) fail("the clock moved while paused");
        if (paused < t0.AddMinutes(80)) fail("paused before the game's time");
        clock.Sample(4.0, t0.AddDays(-10), false);
        if (clock.Now(4.03) > t0.AddDays(-9)) fail("a loaded (earlier) game kept the old time");

        // 2. an aircraft in the air: where it is at the read, along its flight, and on the ground after landing
        // (a headless game day ends at midnight: the aircraft is placed half way through its next flight)
        var g = TheAirline.Models.General.GameObject.GetInstance();
        TheAirline.Presentation.MapAircraft m = null;
        DateTime at = default(DateTime);
        for (int i = 0; i < 60 && m == null; i++)
        {
            TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            foreach (var f in TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(a => a.Fleet.ToList()))
            {
                var fl = f.CurrentFlight;
                if (fl?.Entry == null || f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) continue;
                var landing = fl.GetExpectedLandingTime();
                if ((landing - fl.FlightTime).TotalMinutes < 60) continue;
                at = fl.FlightTime + TimeSpan.FromTicks((landing - fl.FlightTime).Ticks / 2);
                var x = TheAirline.Presentation.WorldMapViewModel.Place(f, at);
                if (x != null && x.InFlight && x.HasFlight) { m = x; break; }
            }
        }
        if (m == null) fail("no aircraft with a flight");
        Console.WriteLine("  " + m.Name + ": " + m.Info);
        double u, v, h;
        if (!m.At(at, out u, out v, out h)) fail("not flying at the read");
        if (Math.Abs(u - m.U) > 1e-9 || Math.Abs(v - m.V) > 1e-9) fail("not where the read put it");
        var span = m.Landing - m.Takeoff;
        double u1, v1, u2, v2;
        m.At(m.Takeoff + TimeSpan.FromTicks(span.Ticks / 4), out u1, out v1, out h);
        m.At(m.Takeoff + TimeSpan.FromTicks(span.Ticks * 3 / 4), out u2, out v2, out h);
        var start = TheAirline.Presentation.WorldMapViewModel.Project(m.Lat1, m.Lon1);
        var end = TheAirline.Presentation.WorldMapViewModel.Project(m.Lat2, m.Lon2);
        Func<double, double, double, double, double> dist = (a1, b1, a2, b2) => { double du = Math.Abs(a1 - a2); if (du > 0.5) du = 1 - du; return Math.Sqrt(du * du + (b1 - b2) * (b1 - b2)); };
        if (!(dist(u1, v1, start[0], start[1]) < dist(u2, v2, start[0], start[1]))) fail("a quarter of the way is not nearer the start than three quarters");
        if (!(dist(u2, v2, end[0], end[1]) < dist(u1, v1, end[0], end[1]))) fail("three quarters of the way is not nearer the end");
        if (double.IsNaN(h)) fail("no heading");
        if (m.At(m.Landing.AddMinutes(1), out u, out v, out h)) fail("still flying after landing");
        if (dist(u, v, end[0], end[1]) > 1e-9) fail("not at the destination after landing");
        if (m.At(m.Takeoff.AddMinutes(-1), out u, out v, out h)) fail("flying before takeoff");
        var trail = m.Trail(at, 0.1, 6);
        if (trail.Count != 7 || dist(trail[6][0], trail[6][1], m.U, m.V) > 1e-9) fail("the trail doesn't end at the aircraft");
        if (m.Path == null || m.Path.Pieces.Sum(p => p.Count) < 10) fail("no path for the selected aircraft");
        Console.WriteLine(string.Format("  at the read {0:0.0000},{1:0.0000}; a quarter of the way {2:0.0000},{3:0.0000}; three quarters {4:0.0000},{5:0.0000}", m.U, m.V, u1, v1, u2, v2));

        // 3. the map reads the game's clock on every refresh
        av.Map.Reload();
        if (av.Map.Clock.Latest != g.GameTime) fail("the map's clock didn't read the game");
        if (!av.Map.Animate) fail("animation off by default");
        Console.WriteLine("MAP ANIMATION TEST OK");
    }

    // "vm idle": the computer airlines' idle aircraft month by month, and why their route searches fail
    static void IdleProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        int months = int.Parse(Environment.GetEnvironmentVariable("TAP_MONTHS") ?? "4");
        Action report = () =>
        {
            var ai = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman && a.Fleet.Count > 0).ToList();
            var fleet = ai.SelectMany(a => a.Fleet.ToList()).Where(f => f.Airliner.BuiltDate <= g.GameTime).ToList();
            var idle = fleet.Where(f => !f.HasRoute).ToList();
            double money = 0;
            foreach (var a in ai) { double i2 = 0, c2 = 0; var last = g.GameTime.AddMonths(-1);
                foreach (TheAirline.Models.General.Finances.Invoice.InvoiceType t in Enum.GetValues(typeof(TheAirline.Models.General.Finances.Invoice.InvoiceType)))
                { if (t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Total || t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Purchases || t == TheAirline.Models.General.Finances.Invoice.InvoiceType.Loans) continue;
                  double v = a.GetInvoicesAmountMonth(last.Year, last.Month, t); if (v > 0) i2 += v; else c2 += v; }
                money += i2 + c2; }
            Console.WriteLine(string.Format("-- {0:dd MMM yyyy}: {1} aircraft, {2} idle ({3} owned, {4} leased; {5}) | {6} on order | operating {7:N0} last month",
                g.GameTime, fleet.Count, idle.Count, idle.Count(TheAirline.Helpers.OwnershipService.IsOwned), idle.Count(f => !TheAirline.Helpers.OwnershipService.IsOwned(f)),
                string.Join(", ", idle.GroupBy(f => f.Status).Select(x => x.Key + " " + x.Count())),
                ai.SelectMany(a => a.Fleet.ToList()).Count(f => f.Airliner.BuiltDate > g.GameTime), money));
            Console.WriteLine("   searches: no start " + TheAirline.Helpers.AIRouteChoice.NoStart + ", no destination " + TheAirline.Helpers.AIRouteChoice.NoDestination + ", no aircraft " + TheAirline.Helpers.AIRouteChoice.NoAircraft + ", idle used " + TheAirline.Helpers.AIRouteChoice.UsedIdle + ", bought/leased " + TheAirline.Helpers.AIRouteChoice.GotAircraft + " | routes for idle aircraft " + TheAirline.Helpers.AIRouteChoice.IdleRoutes + ", sold after a long time idle " + TheAirline.Helpers.OwnershipService.LongIdleSold + " | routes given up: by forecast " + TheAirline.Helpers.AIRouteChoice.Dropped + ", under 20% full " + TheAirline.Helpers.AIRouteChoice.EmptyDropped);
            foreach (var a in ai.OrderByDescending(a => a.Fleet.Count(f => !f.HasRoute)).Take(5))
            {
                var id = a.Fleet.ToList().Where(f => !f.HasRoute && f.Airliner.BuiltDate <= g.GameTime).ToList();
                Console.WriteLine(string.Format("   {0,-22} {1} {2} aircraft, {3} routes, {4} idle: {5}", a.Profile.Name, a.Mentality, a.Fleet.Count, a.Routes.Count, id.Count,
                    string.Join(", ", id.GroupBy(f => f.Airliner.Type.Name).OrderByDescending(x => x.Count()).Take(4).Select(x => x.Key + " x" + x.Count() + " (range " + x.First().Airliner.Range + ")"))));
            }
        };
        report();
        for (int m = 0; m < months; m++) { int sm = g.GameTime.Month; while (g.GameTime.Month == sm) TheAirline.Helpers.GameObjectHelpers.SimulateTurn(); report(); }
    }

    // Airlines > Competitors: the player's routes and airports against the rivals, and the rivals' moves
    /// <summary>"vm stats": the game's statistics against the flights the monthly report recorded, across a month's end.</summary>
    static async Task StatsTest(AirlineViewModel av)
    {
        Console.WriteLine("STATS TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        await GivePlayerRoutes(av, 2);
        var start = g.GameTime;
        int year = start.Year;
        int month = start.Month;
        while (g.GameTime.Month == month) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        for (int i = 0; i < 8; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        if (g.GameTime.Year != year) fail("the test crossed a year");
        Func<string, TheAirline.Models.General.Statistics.StatisticsType> T = TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType;
        var airports = TheAirline.Models.Airports.Airports.GetAllActiveAirports();
        int checkedAirlines = 0;
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(x => x.Fleet.Count > 0).OrderByDescending(x => x.IsHuman).ThenByDescending(x => x.Fleet.Count).Take(8))
        {
            var ops = new TheAirline.Helpers.MonthOps();
            for (var m = new DateTime(start.Year, start.Month, 1); m <= g.GameTime; m = m.AddMonths(1))
            {
                var o = TheAirline.Helpers.AirlineReportService.Ops(a, m.Year, m.Month);
                ops.Flights += o.Flights; ops.OnTime += o.OnTime; ops.Passengers += o.Passengers; ops.Seats += o.Seats; ops.Cargo += o.Cargo; ops.Cancelled += o.Cancelled;
            }
            if (ops.Flights == 0) continue;
            var st = a.Statistics;
            double arr = st.GetStatisticsValue(year, T("Arrivals")), dep = st.GetStatisticsValue(year, T("Departures")), pax = st.GetStatisticsValue(year, T("Passengers"));
            double ontime = st.GetStatisticsValue(year, T("On-Time")), cap = st.GetStatisticsValue(year, T("Capacity")), cargo = st.GetStatisticsValue(year, T("Cargo"));
            double cancelled = st.GetStatisticsValue(year, T("Cancellations"));
            Console.WriteLine("  " + a.Profile.IATACode + ": " + arr + " arrivals (" + ops.Flights + " flown), " + dep + " departures, " + pax + " passengers (" + ops.Passengers + "), " + ontime + " on time (" + ops.OnTime + "), seats " + cap + ", cargo " + cargo.ToString("0") + ", cancelled " + cancelled + " (" + ops.Cancelled + ")");
            if (arr != ops.Flights) fail(a.Profile.IATACode + " arrivals: the game counted " + arr + ", the report recorded " + ops.Flights
                                        + " (" + (ops.Flights - arr) + " lost; the month's end clearing them, or two threads counting at once?)");
            if (pax != ops.Passengers || Math.Abs(cargo - ops.Cargo) > 1) fail(a.Profile.IATACode + " passengers or cargo");
            if (ontime != ops.OnTime) fail(a.Profile.IATACode + " on time: " + ontime + " against " + ops.OnTime);
            if (cancelled != ops.Cancelled) fail(a.Profile.IATACode + " cancellations");
            if (dep < arr || dep > arr + a.Fleet.Count) fail(a.Profile.IATACode + " departures");
            if (ops.Seats > 0 && (cap != ops.Seats)) fail(a.Profile.IATACode + " seats flown: " + cap + " against " + ops.Seats);
            double airportArr = airports.Sum(ap => ap.Statistics.GetStatisticsValue(year, a, T("Arrivals")));
            double airportDep = airports.Sum(ap => ap.Statistics.GetStatisticsValue(year, a, T("Departures")));
            if (airportArr != arr || airportDep != dep) fail(a.Profile.IATACode + " airports: " + airportArr + "/" + airportDep);
            // (the aircraft's own counts aren't compared: one bought or leased from another airline this year brings its flights along)
            double otp = TheAirline.Helpers.FlightStatistics.OnTimePercent(a);
            if (otp < 0 || otp > 100 || Math.Abs(otp - 100 * ontime / arr) > 1e-6) fail(a.Profile.IATACode + " on-time share " + otp);
            // the routes' recent figures (since the month's start): averages within the seats and the cargo space
            foreach (var r in a.Routes.ToList())
            {
                if (r is TheAirline.Models.Routes.PassengerRoute pr)
                    foreach (var rc in pr.Classes)
                    {
                        long n = r.Statistics.GetStatisticsValue(rc, T("Arrivals"));
                        long avg = r.Statistics.GetStatisticsValue(rc, T("Passengers%"));
                        if (n > 0 && (avg < 0 || avg > 1000)) fail("route " + r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode + " " + rc.Type + " passengers per flight " + avg);
                    }
                if (r.Type == TheAirline.Models.Routes.Route.RouteType.Cargo)
                {
                    long n = r.Statistics.GetStatisticsValue(T("Arrivals"));
                    if (n > 0 && (r.Statistics.GetStatisticsValue(T("Cargo%")) < 0 || r.Statistics.GetStatisticsValue(T("Cargo")) > n * 1000)) fail("cargo route figures");
                }
                if (r.HasAirliner && r.Statistics.GetTotalValue(T("Capacity")) > 0 && (r.FillingDegree < 0 || r.FillingDegree > 1.0001)) fail("route filling degree " + r.FillingDegree);
            }
            checkedAirlines++;
        }
        if (checkedAirlines < 3) fail("too few airlines flew");

        // the airports keep this year and the two before
        var someAirport = airports.First(ap => ap.Statistics.Stats.Count > 0);
        var someAirline = someAirport.Statistics.Stats.First().Airline;
        someAirport.Statistics.AddStatisticsValue(year - 5, someAirline, T("Arrivals"), 7);
        someAirport.Statistics.AddStatisticsValue(year - 2, someAirline, T("Arrivals"), 3);
        TheAirline.Helpers.FlightStatistics.PruneAirportStatistics();
        if (someAirport.Statistics.GetStatisticsValue(year - 5, someAirline, T("Arrivals")) != 0 || someAirport.Statistics.GetStatisticsValue(year - 2, someAirline, T("Arrivals")) != 3) fail("airport years kept");

        // saved with the game
        var h = g.HumanAirline;
        double before = h.Statistics.GetStatisticsValue(year, T("Departures"));
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("statstest");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("statstest");
        if (TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Statistics.GetStatisticsValue(year, T("Departures")) != before) fail("departures after loading");
        Console.WriteLine("STATS TEST OK");
    }

    /// <summary>"vm statistics": My Airline > Statistics against the game's figures, after a month's end.</summary>
    static async Task StatisticsTest(AirlineViewModel av)
    {
        Console.WriteLine("STATISTICS TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        await GivePlayerRoutes(av, 2);
        int month = g.GameTime.Month;
        while (g.GameTime.Month == month) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        for (int i = 0; i < 6; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var last = g.GameTime.AddMonths(-1);

        av.Page = av.Pages.ToList().IndexOf("My Airline");
        av.MyAirlineTab = TheAirline.Presentation.AirlineViewModel.StatisticsTabIndex;
        var sv = av.Statistics;
        if (sv.Airlines.Count < 2 || !sv.Airlines[0].IsMine || sv.Airlines.Count(a => a.Shown) < 2) fail("the airlines on the chart");
        Console.WriteLine("  airlines: " + string.Join(", ", sv.Airlines.Select(a => a.Name + (a.Shown ? "" : " (off)") + " " + a.Color)));
        foreach (var figure in TheAirline.Presentation.StatisticsViewModel.Figures)
        {
            sv.Figure = figure;
            var c = sv.Chart;
            var mine = c.Series.First(x => x.IsMine);
            Console.WriteLine("  " + figure + ": " + string.Join(" | ", c.Labels.Select((l, i) => l + " " + (mine.Values[i].HasValue ? c.Format(mine.Values[i].Value) : "-"))) + "  -> " + sv.Summary);
            if (c.Labels.Length != 2 || c.Series.Count != sv.Airlines.Count(a => a.Shown) || sv.Table.Count != 2 || sv.Table[0].Cells.Count != c.Series.Count) fail(figure + ": the chart's shape");
            if (string.IsNullOrEmpty(sv.Summary)) fail(figure + ": no summary");
            if (c.IsShare && mine.Values.Any(v => v.HasValue && (v < 0 || v > 1))) fail(figure + ": a share outside 0 to 100%");
            string axis = c.AxisFormat(mine.Values.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Max());
            if (axis.Length > 9) fail(figure + ": the axis label " + axis + " is too long");
        }
        // the log scale: on by itself when the airlines shown are of very different size, and never for a share
        foreach (var a in sv.Airlines) a.Shown = true;
        sv.Figure = "Passengers";
        var spread = sv.Chart.Series.SelectMany(x => x.Values).Where(v => v.HasValue && v.Value > 0).Select(v => v.Value).ToList();
        bool wide = spread.Count > 1 && spread.Max() >= spread.Min() * TheAirline.Presentation.StatisticsViewModel.LogSpread;
        Console.WriteLine("  passengers: biggest " + spread.Max().ToString("#,0") + ", smallest " + spread.Min().ToString("#,0") + " -> log " + sv.LogScale + " (expected " + wide + ")");
        if (sv.LogScale != wide || sv.Chart.Log != wide) fail("the log scale didn't follow the spread");
        if (!sv.CanLogScale) fail("the log scale should be offered for passengers");
        sv.LogScale = false;
        if (sv.Chart.Log) fail("the log scale stayed on when switched off");
        sv.Figure = "Seats filled";
        if (sv.Chart.Log || sv.CanLogScale) fail("a share should have no log scale");
        sv.Figure = "Passengers";
        if (sv.Chart.Log) fail("the player's choice wasn't kept");
        sv.LogScale = true;
        if (!sv.Chart.Log) fail("the log scale didn't come back on");

        // the figures behind it
        sv.Figure = "Flights";
        var ops = TheAirline.Helpers.AirlineReportService.Ops(h, last.Year, last.Month);
        if (sv.Chart.Series.First(x => x.IsMine).Values[0] != ops.Flights) fail("last month's flights");
        sv.Figure = "Cash";
        var cash = sv.Chart.Series.First(x => x.IsMine).Values;
        if (cash[1] != h.Money || cash[0] != ops.Cash) fail("the cash");
        sv.Figure = "Operating result";
        TheAirline.Helpers.AirlineReportService.IncomeAndCosts(h, last.Year, last.Month, out double inc, out double cost);
        if (Math.Abs(sv.Chart.Series.First(x => x.IsMine).Values[0].Value - (inc + cost)) > 0.5) fail("last month's result");
        // by year: the game's yearly statistics
        sv.Period = TheAirline.Presentation.StatisticsViewModel.ByYear;
        sv.Figure = "Passengers";
        double yearPax = h.Statistics.GetStatisticsValue(g.GameTime.Year, TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Passengers"));
        if (sv.Chart.Labels.Length != g.GameTime.Year - g.StartDate.Year + 1 || sv.Chart.Series.First(x => x.IsMine).Values.Last() != yearPax) fail("the year's passengers");
        Console.WriteLine("  by year: " + string.Join(", ", sv.Chart.Labels) + " -> " + sv.Summary);
        // a rival switched off leaves the chart
        int shown = sv.Chart.Series.Count;
        sv.Airlines.First(a => !a.IsMine && a.Shown).Shown = false;
        if (sv.Chart.Series.Count != shown - 1) fail("switching a rival off");

        // every airline's year so far
        if (sv.League.Count != TheAirline.Models.Airlines.Airlines.GetAllAirlines().Count || sv.League.Count(r => r.IsMine) != 1) fail("the league");
        var pax = sv.League.Select(r => long.Parse(r.Passengers.Replace(",", ""))).ToList();
        for (int i = 1; i < pax.Count; i++) if (pax[i] > pax[i - 1]) fail("the league isn't by passengers");
        sv.LeagueSort = "Fleet";
        var fleets = sv.League.Select(r => int.Parse(r.Fleet)).ToList();
        for (int i = 1; i < fleets.Count; i++) if (fleets[i] > fleets[i - 1]) fail("the league isn't by fleet");
        foreach (var r in sv.League.Take(4)) Console.WriteLine("  league: " + r.Rank + " " + r.Name + " | " + r.Passengers + " pax, " + r.Flights + " flights, " + r.Load + " full, " + r.OnTime + " on time, " + r.Result + ", " + r.Fleet + " aircraft");

        // the player's airports
        if (sv.AirportRows.Count == 0) fail("no airports");
        double arrivals = h.Statistics.GetStatisticsValue(g.GameTime.Year, TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType("Arrivals"));
        if (sv.AirportRows.Sum(r => double.Parse(r.Arrivals.Replace(",", ""))) > arrivals) fail("the airports' arrivals");
        foreach (var r in sv.AirportRows.Take(3)) Console.WriteLine("  airport: " + r.Airport + " | " + r.Arrivals + " arrivals, " + r.Passengers + " pax, last year " + r.LastYear + " " + r.Change + ", " + r.Share);
        Console.WriteLine("STATISTICS TEST OK");
    }

    /// <summary>"vm reputation": a new route's forecast counts the reputation the route itself brings at both ends.</summary>
    static async Task ReputationTest(AirlineViewModel av)
    {
        Console.WriteLine("REPUTATION TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        var mk = av.Market;
        foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Skip(40).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        mk.BuyCommand.Execute(null);
        av.Refresh();
        foreach (var f in h.Fleet) if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f);
        var sv = av.Suggestions;
        await sv.Reload();
        var top = sv.Aircraft.First(a => a.Suggestions.Count > 0).Suggestions[0];
        double add = 7 + 6 * TheAirline.Helpers.RouteForecastService.NewRouteService(h);
        var ends = new[] { top.From, top.To };
        var before = ends.Select(ap => TheAirline.Helpers.PassengerHelpers.ReputationShare(ap, h)).ToArray();
        var predicted = ends.Select(ap => TheAirline.Helpers.PassengerHelpers.ReputationShareWith(ap, h, add)).ToArray();
        // the forecast against one that leaves the route's own reputation out (as the forecast did before)
        var reference = top.Airliner;
        if (!sv.Open(top)) fail("couldn't open " + top.Name + ": " + sv.Status);
        var route = h.Routes.First(r => (r.Destination1 == top.From && r.Destination2 == top.To) || (r.Destination1 == top.To && r.Destination2 == top.From));
        double service = ((TheAirline.Models.Routes.PassengerRoute)route).GetServiceLevel(TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
        // every airline ranked again straight after opening (no game day played: in a day the computer airlines can open
        // routes there too, which no forecast can know)
        Func<TheAirline.Models.Airports.Airport, double> rankedNow = ap =>
        {
            var scores = TheAirline.Models.Airlines.Airlines.GetAllAirlines().ToDictionary(a => a, a => ap.GetAirlineReputation(a));
            return Math.Max(75, TheAirline.Helpers.StatisticsHelpers.GetRatingScale(scores)[h]) / 100;
        };
        var after = ends.Select(rankedNow).ToArray();
        for (int i = 0; i < 2; i++)
            Console.WriteLine("  " + ends[i].Profile.IATACode + ": before " + before[i].ToString("0.000") + ", forecast with the route " + predicted[i].ToString("0.000") + ", once opened " + after[i].ToString("0.000"));
        Console.WriteLine("  the route's service " + service + ", the forecast assumed " + TheAirline.Helpers.RouteForecastService.NewRouteService(h) + "; forecast " + (top.Forecast.Load * 100).ToString("0.0") + "% full");
        for (int i = 0; i < 2; i++) if (Math.Abs(after[i] - predicted[i]) > 0.03) fail(ends[i].Profile.IATACode + " reputation: forecast " + predicted[i] + ", got " + after[i]);
        // an airline's own route on the pair is counted once: a forecast for the flying route doesn't add it again
        var again = TheAirline.Helpers.RouteForecastService.ForecastEach(h, route.Destination1, route.Destination2, top.Airliner, new[] { top.Forecast.RoundTrips }, null)[0];
        var flying = TheAirline.Helpers.RouteForecastService.ForecastEach(h, route.Destination1, route.Destination2, top.Airliner, new[] { top.Forecast.RoundTrips }, route)[0];
        Console.WriteLine("  forecast now, as a new route " + (again.Load * 100).ToString("0.0") + "%, as the flying route " + (flying.Load * 100).ToString("0.0") + "%");
        if (again.Load > flying.Load + 0.03) fail("the airline's own route counted twice");
        Console.WriteLine("REPUTATION TEST OK");
    }

    /// <summary>"vm gapprobe": a suggested route's forecast against its flights, direction by direction (what the forecast assumed, what the flights got).</summary>
    static async Task GapProbe(AirlineViewModel av)
    {
        Console.WriteLine("GAP PROBE");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        var mk = av.Market;
        int skip = int.TryParse(Environment.GetEnvironmentVariable("TAP_SKIP"), out var sk) ? sk : 0;
        foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Skip(skip).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        mk.BuyCommand.Execute(null);
        av.Refresh();
        foreach (var f in h.Fleet) if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f);
        var sv = av.Suggestions;
        await sv.Reload();
        var top = sv.Aircraft.First(a => a.Suggestions.Count > 0).Suggestions[0];
        Console.WriteLine("  " + top.Airliner.Airliner.Type.Name + " (" + top.Airliner.Airliner.GetTotalSeatCapacity() + " seats) on " + top.Name + ": forecast " + top.Forecast.RoundTrips + " trips, " + (top.Forecast.Load * 100).ToString("0.0") + "% full, " + top.Forecast.PassengersPerFlight.ToString("0.0") + " pax a flight");
        foreach (var ap in new[] { top.From, top.To })
            Console.WriteLine("  before: reputation share at " + ap.Profile.IATACode + " " + TheAirline.Helpers.PassengerHelpers.ReputationShare(ap, h).ToString("0.000") + ", usual " + TheAirline.Helpers.RouteForecastService.UsualReputationOf(h).ToString("0.000"));
        foreach (var ap in new[] { top.From, top.To })
        {
            var sc = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Select(a => ap.GetAirlineReputation(a)).ToList();
            Console.WriteLine("  before, at " + ap.Profile.IATACode + ": " + sc.Count + " airlines, mine " + ap.GetAirlineReputation(h).ToString("0") + ", best " + sc.Max().ToString("0") + ", lowest " + sc.Min().ToString("0")
                + "; share with the route " + TheAirline.Helpers.PassengerHelpers.ReputationShareWith(ap, h, 7 + 6 * TheAirline.Helpers.RouteForecastService.NewRouteService(h)).ToString("0.000"));
        }
        sv.Open(top);
        var route = h.Routes.First(r => (r.Destination1 == top.From && r.Destination2 == top.To) || (r.Destination1 == top.To && r.Destination2 == top.From));
        var traces = new System.Collections.Concurrent.ConcurrentBag<TheAirline.Helpers.PassengerHelpers.FlightPaxTrace>();
        TheAirline.Helpers.PassengerHelpers.FlightTrace = t => { if (t.Route == route) traces.Add(t); };
        for (int i = 0; i < 30; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        TheAirline.Helpers.PassengerHelpers.FlightTrace = null;
        foreach (var ap in new[] { top.From, top.To })
        {
            var ts = traces.Where(t => t.From == ap).ToList();
            if (ts.Count == 0) continue;
            Console.WriteLine("  from " + ap.Profile.IATACode + ": " + ts.Count + " class-flights, seats " + ts.Average(t => t.Seats).ToString("0") + ", pax " + ts.Average(t => t.Passengers).ToString("0.0")
                + " | demand a day " + ts.Average(t => t.DailyDemand).ToString("0") + " (direct " + ts.Average(t => t.Direct).ToString("0") + ", connecting " + ts.Average(t => t.Connecting).ToString("0") + ", nearby " + ts.Average(t => t.Nearby).ToString("0") + ")"
                + ", flights a day " + ts.Average(t => t.FlightsPerDay).ToString("0.0") + ", per flight " + ts.Average(t => t.PerFlight).ToString("0") + ", capacity " + ts.Average(t => t.TotalCapacity).ToString("0")
                + " | capacity% " + ts.Average(t => t.CapacityPercent).ToString("0.000") + ", competition " + ts.Average(t => t.Competition).ToString("0.000") + ", reputation " + ts.Average(t => t.Reputation).ToString("0.000"));
            Console.WriteLine("    reputation share now at " + ap.Profile.IATACode + ": " + TheAirline.Helpers.PassengerHelpers.ReputationShare(ap, h).ToString("0.000"));
        }
        Console.WriteLine("  the route's economy service " + ((TheAirline.Models.Routes.PassengerRoute)route).GetServiceLevel(TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass) + ": " + string.Join(", ", ((TheAirline.Models.Routes.PassengerRoute)route).GetRouteAirlinerClass(TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass).GetFacilities().Select(f => f.Type + " " + f.Name + " " + f.ServiceLevel)) + "; the forecast assumed " + TheAirline.Helpers.RouteForecastService.NewRouteService(h));
        foreach (var ap in new[] { top.From, top.To })
            Console.WriteLine("  reputation score at " + ap.Profile.IATACode + ": mine " + ap.GetAirlineReputation(h).ToString("0") + ", the best " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => a != h).Max(a => ap.GetAirlineReputation(a)).ToString("0") + ", the lowest " + TheAirline.Models.Airlines.Airlines.GetAllAirlines().Min(a => ap.GetAirlineReputation(a)).ToString("0"));
        // the forecast made now, for the route as it flies
        var again = TheAirline.Helpers.RouteForecastService.Best(h, route.Destination1, route.Destination2, top.Airliner, 8, out var all, route);
        Console.WriteLine("  flew " + (route.FillingDegree * 100).ToString("0.0") + "% full; a forecast made now for the flying route: " + (again.Load * 100).ToString("0.0") + "% at " + again.RoundTrips + " trips");
        Console.WriteLine("GAP PROBE DONE");
    }

    /// <summary>"vm airportsearch": the airport search finds an airport by its town and state, not only its name and country.</summary>
    static void AirportSearchTest(AirlineViewModel av)
    {
        Console.WriteLine("AIRPORT SEARCH TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var all = TheAirline.Models.Airports.Airports.GetAllAirports();
        Func<string, System.Collections.Generic.List<TheAirline.Models.Airports.Airport>> find =
            q => all.Where(a => TheAirline.Presentation.AirportsViewModel.Matches(a, q)).ToList();

        // the Hawaiian airports are in the game and a search for the state finds them
        foreach (string code in new[] { "KOA", "ITO", "HNL", "OGG", "LIH" })
        {
            var a = all.FirstOrDefault(x => x.Profile.IATACode == code);
            if (a == null) fail(code + " isn't in the game");
            if (a.Profile.Town?.State?.Name != "Hawaii") fail(code + " isn't in Hawaii: " + a.Profile.Town?.State?.Name);
            if (a.GetMaxRunwayLength() <= 0 || a.Terminals.NumberOfGates <= 0) fail(code + " has no runway or gates");
        }
        var hawaii = find("Hawaii");
        Console.WriteLine("  \"Hawaii\": " + string.Join(", ", hawaii.Select(a => a.Profile.IATACode)));
        foreach (string code in new[] { "KOA", "ITO", "HNL", "OGG", "LIH" })
            if (!hawaii.Any(a => a.Profile.IATACode == code)) fail("\"Hawaii\" didn't find " + code);
        if (hawaii.Any(a => a.Profile.Town?.State?.Name != "Hawaii")) fail("\"Hawaii\" found an airport outside Hawaii");

        // the state's short name, the town, the code, the airport's name and the country all still work
        if (!find("HI").Any(a => a.Profile.IATACode == "KOA")) fail("\"HI\" didn't find KOA");
        if (!find("Hilo").Any(a => a.Profile.IATACode == "ITO")) fail("\"Hilo\" didn't find ITO");
        if (!find("KOA").Any(a => a.Profile.IATACode == "KOA")) fail("\"KOA\" didn't find KOA");
        if (!find("Kona International").Any(a => a.Profile.IATACode == "KOA")) fail("the airport's name didn't find KOA");
        if (find("United States").Count < 50) fail("the country search");
        var texas = find("Texas");
        Console.WriteLine("  \"Texas\": " + string.Join(", ", texas.Select(a => a.Profile.IATACode)));
        if (!texas.Any(a => a.Profile.IATACode == "DFW") || !texas.Any(a => a.Profile.IATACode == "IAH")) fail("\"Texas\" didn't find DFW and IAH");
        if (find("zzzz").Count != 0) fail("a search that matches nothing");
        if (find("").Count != all.Count) fail("an empty search should match everything");

        // and a route can be flown to them: the demand is there
        foreach (string code in new[] { "KOA", "ITO" })
        {
            var a = all.First(x => x.Profile.IATACode == code);
            int demand = a.GetDestinationPassengersSum();
            Console.WriteLine("  " + code + " " + a.Profile.Name + ", " + a.Profile.Town.Name + ", " + a.Profile.Town.State.Name
                + ": " + a.Terminals.NumberOfGates + " gates, " + a.GetMaxRunwayLength() + " m runway, demand out " + demand.ToString("#,0"));
            if (demand <= 0) fail(code + " has no demand");
        }
        Console.WriteLine("AIRPORT SEARCH TEST OK");
    }

    /// <summary>"vm airportsort": the airport list sorted by distance from an airport you name.</summary>
    static void AirportSortTest(AirlineViewModel av)
    {
        Console.WriteLine("AIRPORT SORT TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var ap = av.Airports;

        // by name until an airport is named
        ap.Search = "Hawaii";
        ap.Near = "";
        if (ap.HasNear || ap.NearAirport != null || ap.NearStatus != "") fail("nothing should be named yet");
        var byName = ap.Airports.Select(x => x.Airport.Profile.Name).ToList();
        if (byName.Count < 5) fail("the Hawaii search");
        for (int i = 1; i < byName.Count; i++)
            if (string.CompareOrdinal(byName[i], byName[i - 1]) < 0) fail("not sorted by name: " + byName[i - 1] + " then " + byName[i]);
        if (ap.Airports.Any(x => x.HasDetails)) fail("a distance shown with nothing to measure from");

        // nearest first from Hilo
        ap.Near = "ITO";
        if (ap.NearAirport?.Profile.IATACode != "ITO" || !ap.HasNear) fail("ITO wasn't found: " + ap.NearStatus);
        Console.WriteLine("  " + ap.NearStatus);
        var rows = ap.Airports.ToList();
        foreach (var r in rows) Console.WriteLine("    " + r.Text + (r.HasDetails ? " | " + r.Details : ""));
        if (rows[0].Airport.Profile.IATACode != "ITO" || rows[0].Distance != null) fail("the airport measured from should come first, with no distance");
        var far = rows.Skip(1).ToList();
        for (int i = 1; i < far.Count; i++)
            if (far[i].Distance < far[i - 1].Distance - 0.01) fail("not nearest first: " + far[i - 1].Text + " then " + far[i].Text);
        if (far[0].Airport.Profile.IATACode != "KOA") fail("Kona is the nearest to Hilo, not " + far[0].Airport.Profile.IATACode);
        if (!far.All(x => x.HasDetails && x.Details.EndsWith("from ITO"))) fail("each row should say how far from ITO");

        // the Airports page's own Distance sort, around an airport you name
        var mg = av.AirportManager;
        mg.OnlyMine = false;
        mg.Search = "Hawaii";
        mg.SortBy = "Name";
        if (mg.SortByDistance || mg.NearStatus != "") fail("Distance isn't chosen yet");
        if (mg.Airports.Any(x => x.DistanceText != "")) fail("a distance shown while sorting by name");
        mg.SortBy = "Distance";
        if (!mg.SortByDistance || !mg.NearStatus.StartsWith("Name an airport")) fail("Distance with nothing to measure from: " + mg.NearStatus);
        var byNameStill = mg.Airports.Select(x => x.Airport.Profile.Name).ToList();
        for (int i = 1; i < byNameStill.Count; i++)
            if (string.CompareOrdinal(byNameStill[i], byNameStill[i - 1]) < 0) fail("without an airport to measure from the list should stay by name");
        mg.Near = "ITO";
        Console.WriteLine("  Airports page: " + mg.NearStatus);
        var mrows = mg.Airports.OfType<TheAirline.Presentation.AirportRow>().ToList();
        if (mrows.Count != mg.Airports.Count) fail("the Airports page's rows");
        foreach (var r in mrows) Console.WriteLine("    " + r.Codes + " " + r.Name + " | " + r.Town + " | " + (r.DistanceText == "" ? "-" : r.DistanceText));
        if (mrows[0].Airport.Profile.IATACode != "ITO" || mrows[0].DistanceText != "") fail("Hilo should lead its own list");
        var mfar = mrows.Skip(1).ToList();
        for (int i = 1; i < mfar.Count; i++)
            if (mfar[i].Distance < mfar[i - 1].Distance - 0.01) fail("the Airports page isn't nearest first");
        if (mfar[0].Airport.Profile.IATACode != "KOA") fail("Kona is nearest to Hilo, not " + mfar[0].Airport.Profile.IATACode);
        if (mfar.Any(x => x.DistanceText == "")) fail("every row but the first should show its distance");
        // the state search works here too, and the sort goes back when another is chosen
        if (mrows.Count < 5 || mrows.Any(x => x.Airport.Profile.Town?.State?.Name != "Hawaii")) fail("the Airports page's Hawaii search");
        mg.SortBy = "Size";
        if (mg.SortByDistance || mg.Airports.Any(x => x.DistanceText != "")) fail("the distance should go when another sort is chosen");
        mg.SortBy = "Distance";
        mg.Near = "nowhere at all";
        if (!mg.NearStatus.StartsWith("No airport found")) fail("an airport that doesn't exist");
        mg.Search = "";
        mg.SortBy = "Name";
        mg.Near = "";

        // a name works as well as a code, and the sort holds over a new search
        ap.Near = "Kona International";
        if (ap.NearAirport?.Profile.IATACode != "KOA") fail("a name didn't find Kona");
        ap.Search = "";
        if (ap.Airports.Count > 0 && ap.Airports.Any(x => !x.IsOwn)) fail("an empty search should list the airline's own airports");
        ap.Search = "United States";
        var us = ap.Airports.ToList();
        if (us.Count < 20) fail("the United States search");
        if (us[0].Airport.Profile.IATACode != "KOA" || us[0].Distance != null) fail("Kona itself should lead its own list");
        var rest = us.Skip(1).ToList();
        for (int i = 1; i < rest.Count; i++)
            if (rest[i].Distance < rest[i - 1].Distance - 0.01) fail("the US list isn't nearest first");
        Console.WriteLine("  nearest to KOA in the United States: " + string.Join(", ", rest.Take(6).Select(x => x.Airport.Profile.IATACode + " " + DistanceFormatter.Km(x.Distance ?? 0))));
        if (rest[0].Airport.Profile.IATACode != "ITO") fail("Hilo is the nearest to Kona, not " + rest[0].Airport.Profile.IATACode);
        // the mainland is a long way off: the nearest 150 are kept, so the list stops before every US airport
        if (rest.Any(x => x.Distance > 0) && rest.Last().Distance < 3000) fail("the US list looks too short");

        // an airport that doesn't exist says so and leaves the list by name
        ap.Near = "zzzz";
        if (ap.NearAirport != null || !ap.NearStatus.StartsWith("No airport found")) fail("a reference that matches nothing");
        var names = ap.Airports.Select(x => x.Airport.Profile.Name).ToList();
        for (int i = 1; i < names.Count; i++)
            if (string.CompareOrdinal(names[i], names[i - 1]) < 0) fail("should fall back to sorting by name");
        ap.Near = "";
        ap.Search = "";
        Console.WriteLine("AIRPORT SORT TEST OK");
    }

    /// <summary>"vm aiempty": a computer airline passes over destinations its forecast leaves nearly empty.</summary>
    static void EmptyRouteTest(AirlineViewModel av)
    {
        Console.WriteLine("EMPTY ROUTE TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        double floor = TheAirline.Helpers.AIRouteChoice.MinLoad;
        if (floor <= 0 || floor >= 0.5) fail("the floor should be a sensible share: " + floor);

        // a computer airline with aircraft, and the destinations it would consider from one of its airports
        var ai = TheAirline.Models.Airlines.Airlines.GetAllAirlines()
                           .Where(a => !a.IsHuman && a.Airports.Count > 0 && a.Fleet.Count > 0
                                       && a.AirlineRouteFocus == TheAirline.Models.Routes.Route.RouteType.Passenger)
                           .OrderByDescending(a => a.Fleet.Count).First();
        TheAirline.Models.Airports.Airport origin = null;
        System.Collections.Generic.List<TheAirline.Models.Airports.Airport> ranked = null;
        foreach (var ap in ai.Airports.ToList())
        {
            var list = TheAirline.Helpers.AIHelpers.GetDestinationAirports(ai, ap);
            if (list.Count >= 8) { origin = ap; ranked = list; break; }
        }
        if (origin == null) fail("no airport with destinations to forecast");
        Console.WriteLine("  " + ai.Profile.Name + " from " + origin.Profile.IATACode + ": " + ranked.Count + " destinations to choose from");

        // what the forecast says about the ones it would look at
        var fleet = new System.Collections.Generic.List<Tuple<string, double, double>>();
        double fares = TheAirline.Helpers.AIRouteChoice.FareFactor(ai);
        foreach (var d in ranked.Take(TheAirline.Helpers.AIRouteChoice.Candidates))
        {
            var f = TheAirline.Helpers.AIRouteChoice.Reference(ai, TheAirline.Helpers.MathHelpers.GetDistance(origin, d));
            if (f == null) continue;
            var fc = TheAirline.Helpers.AIRouteChoice.Best(ai, origin, d, f, null, false, fares);
            fleet.Add(Tuple.Create(d.Profile.IATACode, fc.Load, fc.ProfitPerDay));
        }
        foreach (var x in fleet) Console.WriteLine("    " + x.Item1 + " forecast " + (x.Item2 * 100).ToString("0") + "% full, " + x.Item3.ToString("+#,0;-#,0") + " a day");
        if (fleet.Count == 0) fail("nothing could be forecast");

        // with the floor where it is, anything picked clears it
        int tooEmpty0 = TheAirline.Helpers.AIRouteChoice.TooEmpty;
        var picked = TheAirline.Helpers.AIRouteChoice.PickDestination(ai, origin, ranked);
        if (picked != null)
        {
            var f = TheAirline.Helpers.AIRouteChoice.Reference(ai, TheAirline.Helpers.MathHelpers.GetDistance(origin, picked));
            double load = TheAirline.Helpers.AIRouteChoice.Best(ai, origin, picked, f, null, false, fares).Load;
            Console.WriteLine("  picked " + picked.Profile.IATACode + " at " + (load * 100).ToString("0") + "% full");
            if (load < floor) fail("picked a destination below the floor: " + (load * 100).ToString("0") + "%");
        }
        else
        {
            Console.WriteLine("  nowhere worth flying from " + origin.Profile.IATACode);
            if (TheAirline.Helpers.AIRouteChoice.TooEmpty != tooEmpty0 + 1) fail("giving up should be counted");
        }

        // a floor nothing can clear: no destination at all, and it is counted
        TheAirline.Helpers.AIRouteChoice.MinLoad = 1.01;
        int before = TheAirline.Helpers.AIRouteChoice.TooEmpty;
        if (TheAirline.Helpers.AIRouteChoice.PickDestination(ai, origin, ranked) != null) fail("a floor of 101% should turn everything away");
        if (TheAirline.Helpers.AIRouteChoice.TooEmpty != before + 1) fail("the count of searches that found nowhere");

        // no floor: the old behaviour, the best of them by profit whatever its load
        TheAirline.Helpers.AIRouteChoice.MinLoad = 0;
        var any = TheAirline.Helpers.AIRouteChoice.PickDestination(ai, origin, ranked);
        if (any == null) fail("without a floor something should always be picked");
        double bestProfit = fleet.Max(x => x.Item3);
        if (Math.Abs(fleet.First(x => x.Item1 == any.Profile.IATACode).Item3 - bestProfit) > 1) fail("without a floor the most profitable should be picked");
        Console.WriteLine("  without a floor: " + any.Profile.IATACode + " at " + (fleet.First(x => x.Item1 == any.Profile.IATACode).Item2 * 100).ToString("0") + "% full");

        // a floor between the two looks past the first candidates rather than giving up at once
        var sorted = fleet.OrderByDescending(x => x.Item2).ToList();
        if (sorted.Count > 1 && sorted[0].Item2 > 0.02)
        {
            TheAirline.Helpers.AIRouteChoice.MinLoad = Math.Min(0.99, sorted[0].Item2 - 0.001);
            int widened = TheAirline.Helpers.AIRouteChoice.Widened;
            var one = TheAirline.Helpers.AIRouteChoice.PickDestination(ai, origin, ranked);
            if (one == null) fail("a floor the fullest candidate clears should still pick it");
            Console.WriteLine("  floor just under the fullest (" + (sorted[0].Item2 * 100).ToString("0") + "%): picked " + one.Profile.IATACode
                              + ", looked past the first " + TheAirline.Helpers.AIRouteChoice.Candidates + ": " + (TheAirline.Helpers.AIRouteChoice.Widened > widened));
        }
        TheAirline.Helpers.AIRouteChoice.MinLoad = floor;

        // and over a few days of play the airlines still open routes
        int routes0 = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).Sum(a => a.Routes.Count);
        int picked0 = TheAirline.Helpers.AIRouteChoice.Picked;
        for (int i = 0; i < 20; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        int routes1 = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).Sum(a => a.Routes.Count);
        Console.WriteLine("  20 days: routes " + routes0 + " -> " + routes1 + ", picked " + (TheAirline.Helpers.AIRouteChoice.Picked - picked0)
                          + ", looked further " + TheAirline.Helpers.AIRouteChoice.Widened + ", nowhere with passengers " + TheAirline.Helpers.AIRouteChoice.TooEmpty);
        if (routes1 <= routes0) fail("the computer airlines stopped opening routes");
        Console.WriteLine("EMPTY ROUTE TEST OK");
    }

    /// <summary>"vm airportprobe": whether the airports whose IATA codes are in TAP_IATA are in the game, and what demand they have.</summary>
    static void AirportProbe(AirlineViewModel av)
    {
        Console.WriteLine("AIRPORT PROBE");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        string[] codes = (Environment.GetEnvironmentVariable("TAP_IATA") ?? "KOA,ITO,HNL,OGG,LIH").Split(',').Select(x => x.Trim()).ToArray();
        var all = TheAirline.Models.Airports.Airports.GetAllAirports();
        var active = TheAirline.Models.Airports.Airports.GetAllActiveAirports();
        Console.WriteLine("  " + all.Count + " airports loaded, " + active.Count + " active on " + g.GameTime.ToString("yyyy-MM-dd"));
        foreach (string code in codes)
        {
            var a = all.FirstOrDefault(x => x.Profile.IATACode == code);
            if (a == null) { Console.WriteLine("  " + code + ": NOT LOADED"); continue; }
            bool isActive = active.Contains(a);
            double pax = a.Profile.PaxValues == null ? -1 : a.Profile.PaxValues.Where(v => v.FromYear <= g.GameTime.Year).OrderBy(v => v.FromYear).Select(v => (double)v.Pax).LastOrDefault();
            double outDemand = a.GetDestinationPassengersSum();
            Console.WriteLine("  " + code + " " + a.Profile.Name + " | " + a.Profile.Town + ", " + a.Profile.Town.Country.Name
                + " | type " + a.Profile.Type + " | size " + a.Profile.Size + " | active " + isActive
                + " | period " + a.Profile.Period.From.Year + "-" + a.Profile.Period.To.Year
                + " | pax " + pax + " | runway " + a.GetMaxRunwayLength() + "m | gates " + a.Terminals.NumberOfGates
                + " | demand out " + outDemand.ToString("#,0"));
        }
        Console.WriteLine("AIRPORT PROBE DONE");
    }

    /// <summary>"vm statsprobe": the game's yearly statistics against the flights the monthly report recorded (TAP_DAYS, default 20).</summary>
    static async Task StatsProbe(AirlineViewModel av)
    {
        Console.WriteLine("STATS PROBE");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        await GivePlayerRoutes(av, 3);
        int days = int.TryParse(Environment.GetEnvironmentVariable("TAP_DAYS"), out var d) ? d : 20;
        int year = g.GameTime.Year;
        var start = g.GameTime;
        for (int i = 0; i < days; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Func<string, TheAirline.Models.General.Statistics.StatisticsType> T = TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsType;
        Console.WriteLine("  " + days + " days from " + start.ToString("yyyy-MM-dd") + "; types: " + string.Join(", ", TheAirline.Models.General.Statistics.StatisticsTypes.GetStatisticsTypes().Select(t => t.Shortname)));
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().OrderByDescending(x => x.IsHuman).ThenByDescending(x => x.Fleet.Count).Take(6))
        {
            var ops = new TheAirline.Helpers.MonthOps();
            for (var m = new DateTime(start.Year, start.Month, 1); m <= g.GameTime; m = m.AddMonths(1))
            {
                var o = TheAirline.Helpers.AirlineReportService.Ops(a, m.Year, m.Month);
                ops.Flights += o.Flights; ops.Cancelled += o.Cancelled; ops.OnTime += o.OnTime; ops.Passengers += o.Passengers; ops.Seats += o.Seats; ops.Cargo += o.Cargo;
            }
            var st = a.Statistics;
            Console.WriteLine("  " + a.Profile.IATACode + (a.IsHuman ? " (player)" : "") + " fleet " + a.Fleet.Count + ": recorded " + ops.Flights + " flights, " + ops.Passengers + " pax, " + ops.Cancelled + " cancelled, " + ops.OnTime + " on time");
            Console.WriteLine("     stats: " + string.Join(", ", st.StatValues.Where(v => v.Year == year && v.Stat != null).Select(v => v.Stat.Shortname + "=" + v.Value.ToString("0.#"))));
            // airports: the airline's arrivals and departures summed over all airports
            double arr = 0, dep = 0, pax = 0;
            foreach (var ap in TheAirline.Models.Airports.Airports.GetAllActiveAirports())
            {
                arr += ap.Statistics.GetStatisticsValue(year, a, T("Arrivals"));
                dep += ap.Statistics.GetStatisticsValue(year, a, T("Departures"));
                pax += ap.Statistics.GetStatisticsValue(year, a, T("Passengers"));
            }
            Console.WriteLine("     airports: arrivals " + arr + ", departures " + dep + ", passengers " + pax);
            // aircraft
            double farr = 0, fdep = 0, fpax = 0;
            foreach (var f in a.Fleet.ToList()) { farr += f.Statistics.GetStatisticsValue(year, T("Arrivals")); fdep += f.Statistics.GetStatisticsValue(year, T("Departures")); fpax += f.Statistics.GetStatisticsValue(year, T("Passengers")); }
            Console.WriteLine("     aircraft: arrivals " + farr + ", departures " + fdep + ", passengers " + fpax);
            // routes
            double rdep = 0, rpax = 0;
            foreach (var r in a.Routes.ToList()) { rdep += r.Statistics.GetTotalValue(T("Departures")); rpax += r.Statistics.GetTotalValue(T("Passengers")); }
            Console.WriteLine("     routes: departures " + rdep + ", passengers " + rpax);
        }
        Console.WriteLine("STATS PROBE DONE");
    }

    /// <summary>"vm engines" (TAP_YEAR=1996): every aircraft type has its engines, and an order can take another one.</summary>
    static void EnginesTest(AirlineViewModel av)
    {
        Console.WriteLine("ENGINES TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        var engines = TheAirline.Models.Airliners.EngineTypes.GetEngineTypes();
        var types = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes();
        Console.WriteLine("  " + engines.Count + " engines for " + types.Count + " aircraft types, from " + engines.Where(e => e.Manufacturer != null).Select(e => e.Manufacturer.ShortName).Distinct().Count() + " makers");
        if (engines.Any(e => e.Manufacturer == null)) fail("an engine without its maker: " + string.Join(", ", engines.Where(e => e.Manufacturer == null).Select(e => e.Model)));
        int gaps = 0, choices = 0, slower = 0, changed = 0;
        foreach (var t in types)
        {
            var mine = TheAirline.Models.Airliners.EngineTypes.GetEngineTypes(t);
            if (mine.Count == 0) { Console.WriteLine("  no engines: " + t.Name); gaps++; continue; }
            if (mine.Count > 1) choices++;
            foreach (var e in mine) if (e.MaxSpeed < t.CruisingSpeed) { slower++; Console.WriteLine("  slows the aircraft: " + e.Model + " on " + t.Name + " (" + e.MaxSpeed + " < " + t.CruisingSpeed + ")"); }
            for (int y = t.Produced.From.Year; y <= Math.Min(t.Produced.To.Year, 2040); y++)
            {
                var std = TheAirline.Models.Airliners.EngineTypes.GetStandardEngineType(t, y);
                if (std == null) { Console.WriteLine("  no engine for " + t.Name + " in " + y); gaps++; break; }
                if ((std.ConsumptationModifier != 1 || std.RangeModifier != 1 || std.RunwayModifier != 1) && !t.Name.StartsWith("Boeing 767") && t.Manufacturer.ShortName != "Airbus" && t.Manufacturer.ShortName != "Aerospatiale")
                { Console.WriteLine("  the standard engine changes " + t.Name + " in " + y + ": " + std.Model); changed++; break; }
            }
        }
        Console.WriteLine("  " + choices + " types with a choice of engines");
        if (gaps > 0 || slower > 0 || changed > 0) fail(gaps + " gaps, " + slower + " engines slowing their aircraft, " + changed + " standard engines changing their type");
        if (choices < 40) fail("too few choices");

        // the 777-200 in 1996: three makers' engines, the GE90 ordered
        var na = av.NewAircraft;
        na.Reload();
        na.Manufacturer = na.Manufacturers.First(m => m.Name == "The Boeing Company");
        var item = na.Types.FirstOrDefault(t => t.Name == "Boeing 777-200");
        if (item == null) fail("no 777-200 in " + g.GameTime.Year);
        na.Selected = item;
        Console.WriteLine("  777-200 engines: " + string.Join(" | ", na.Engines.Select(e => e.Text)));
        if (na.Engines.Count != 3 || !na.Engine.Text.Contains("(standard)")) fail("the 777's engine choices");
        var ge = na.Engines.First(e => e.Engine.Model.StartsWith("GE90"));
        if (!ge.Text.Contains("3% less fuel") || !ge.Text.Contains("2% more range")) fail("the GE90's description");
        long standardPrice = na.Engine.Engine.Price;
        na.Engine = ge;
        na.Amount = 1;
        h.Money = Math.Max(h.Money, 500000000);
        int fleet = h.Fleet.Count;
        na.OrderCommand.Execute(null);
        var a = h.Fleet.Skip(fleet).Select(f => f.Airliner).FirstOrDefault();
        if (a == null) fail("not ordered: " + na.Status);
        if (a.EngineType != ge.Engine) fail("the order hasn't the GE90");
        if (a.Range != (long) (item.Type.Range * 1.02) || Math.Abs(a.FuelConsumption - item.Type.FuelConsumption * 0.97) > 1e-9) fail("the GE90's range and fuel on the aircraft");
        Console.WriteLine("  ordered with " + a.EngineType.Model + ": range " + a.Range + " km (type " + item.Type.Range + "), fuel " + a.FuelConsumption + " (type " + item.Type.FuelConsumption + "), engine premium " + (ge.Engine.Price - standardPrice));

        // used aircraft carry an engine of their build year
        int used = 0, wrong = 0;
        foreach (var u in TheAirline.Models.Airliners.Airliners.GetAirlinersForSale())
        {
            used++;
            var all = TheAirline.Models.Airliners.EngineTypes.GetEngineTypes(u.Type);
            if (all.Count == 0) continue;
            if (u.EngineType == null) { wrong++; Console.WriteLine("  used without an engine: " + u.Type.Name + " built " + u.BuiltDate.Year); continue; }
            var then = TheAirline.Models.Airliners.EngineTypes.GetEngineTypes(u.Type, u.BuiltDate.Year);
            if (then.Count > 0 && !then.Contains(u.EngineType)) { wrong++; Console.WriteLine("  used " + u.Type.Name + " built " + u.BuiltDate.Year + " has " + u.EngineType.Model); }
        }
        Console.WriteLine("  used aircraft for sale: " + used + ", with a wrong or missing engine: " + wrong);
        if (wrong > 0) fail("used aircraft engines");

        // saved with the game
        string model = a.EngineType.Model, tail = a.TailNumber;
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("enginestest");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("enginestest");
        var back = TheAirline.Models.General.GameObject.GetInstance().HumanAirline.Fleet.FirstOrDefault(f => f.Airliner.TailNumber == tail);
        if (back?.Airliner.EngineType?.Model != model || back.Airliner.Range != a.Range) fail("the engine after loading");
        Console.WriteLine("ENGINES TEST OK");
    }

    /// <summary>"vm orderahead" (TAP_YEAR=1994): a type can be ordered before its first deliveries, for delivery from then.</summary>
    static void OrderAheadTest(AirlineViewModel av)
    {
        Console.WriteLine("ORDER AHEAD TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        DateTime now = g.GameTime;
        var na = av.NewAircraft;
        na.Reload();
        na.Manufacturer = na.Manufacturers.First(m => m.Name == "The Boeing Company");
        Console.WriteLine("  " + now.ToString("yyyy-MM-dd") + ", Boeing: " + string.Join(", ", na.Types.Select(t => t.Name)));
        // every type listed is in production now or within the order-ahead window, and every such type is listed
        foreach (var t in TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().Where(t => t.Manufacturer?.Name == "The Boeing Company" && TheAirline.Helpers.AirlineTypeRules.CanUseType(h, t)))
        {
            bool should = t.Produced.From <= now.AddMonths(TheAirline.Presentation.NewAircraftViewModel.OrderAheadMonths) && t.Produced.To > now;
            if (should != na.Types.Any(x => x.Type == t)) fail(t.Name + (should ? " is missing" : " shouldn't be listed"));
        }
        // the 777-200 (first deliveries 1995); in later games the first 777 on sale
        var item = na.Types.FirstOrDefault(t => t.Name == "Boeing 777-200") ?? na.Types.FirstOrDefault(t => t.Name.StartsWith("Boeing 777"));
        if (item == null) fail("no 777 is listed");
        Console.WriteLine("  " + item.Name + " | " + item.Details);
        bool ahead = item.Type.Produced.From > now;
        if (ahead != item.Details.Contains("first deliveries")) fail("the first deliveries note");
        na.Selected = item;
        // (only some makers have engine choices in the data; a type ordered ahead gets those of its first deliveries' year)
        if (TheAirline.Models.Airliners.EngineTypes.GetEngineTypes(item.Type, TheAirline.Presentation.NewAircraftViewModel.EngineYear(item.Type)).Count != na.Engines.Count) fail("the engine choices");
        Console.WriteLine("  engines: " + string.Join(", ", na.Engines.Select(e => e.Text)) + " | " + na.Summary);
        na.Amount = 1;
        h.Money = Math.Max(h.Money, 500000000);
        int fleet = h.Fleet.Count;
        na.OrderCommand.Execute(null);
        var ordered = h.Fleet.Skip(fleet).ToList();
        if (ordered.Count != 1) fail("not ordered: " + na.Status);
        var built = ordered[0].Airliner.BuiltDate;
        Console.WriteLine("  ordered: " + na.Status + " (delivery " + built.ToString("yyyy-MM-dd") + ")");
        if (built < item.Type.Produced.From.Date || built < now.Date.AddMonths(3)) fail("delivered before its first deliveries or sooner than 3 months");
        if (ahead && built > item.Type.Produced.From.Date.AddMonths(3)) fail("delivered much later than its first deliveries");
        if (h.DeliveredFleet.Contains(ordered[0])) fail("counted as delivered already");
        // a type further off isn't offered yet
        var far = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().FirstOrDefault(t => t.Manufacturer?.Name == "The Boeing Company" && t.Produced.From > now.AddMonths(TheAirline.Presentation.NewAircraftViewModel.OrderAheadMonths));
        if (far != null && na.Types.Any(x => x.Type == far)) fail(far.Name + " is offered " + (far.Produced.From.Year - now.Year) + " years ahead");
        Console.WriteLine("ORDER AHEAD TEST OK");
    }

    /// <summary>"vm typecheck": the loaded aircraft types whose name has TAP_TYPE in it, with their maker, years and whether the player may buy them.</summary>
    static void TypeCheck(AirlineViewModel av)
    {
        string q = Environment.GetEnvironmentVariable("TAP_TYPE") ?? "777";
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        foreach (var t in TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().Where(t => t.Name.Contains(q)))
            Console.WriteLine("  " + t.Name + " | maker " + t.Manufacturer?.Name + " | " + t.Produced.From.ToString("yyyy-MM-dd") + " to " + t.Produced.To.ToString("yyyy-MM-dd") + " | " + t.TypeAirliner + " | can use " + TheAirline.Helpers.AirlineTypeRules.CanUseType(h, t));
        av.NewAircraft.Reload();
        Console.WriteLine("  game date " + TheAirline.Models.General.GameObject.GetInstance().GameTime.ToString("yyyy-MM-dd") + "; makers: " + string.Join(", ", av.NewAircraft.Manufacturers.Select(m => m.Text)));
        Console.WriteLine("TYPECHECK DONE");
    }

    static async Task SuggestTest(AirlineViewModel av)
    {
        Console.WriteLine("SUGGEST TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        var mk = av.Market;
        // three idle aircraft of different sizes
        var bought = new System.Collections.Generic.List<string>();
        foreach (int skip in new[] {0, 40, 120})
        {
            foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Skip(skip).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
            mk.BuyCommand.Execute(null);
        }
        av.Refresh();
        foreach (var f in h.Fleet) if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f);
        var idle = TheAirline.Helpers.RouteSuggestionService.IdleAircraft(h);
        Console.WriteLine("  idle: " + string.Join(", ", idle.Select(f => f.Name + " " + f.Airliner.Type.Name + " (" + f.Airliner.GetTotalSeatCapacity() + " seats, " + f.Airliner.Range + " km)")));
        if (idle.Count < 2) fail("no idle aircraft to suggest for");

        var sv = av.Suggestions;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sv.Reload();
        Console.WriteLine("  worked out in " + sw.ElapsedMilliseconds + " ms: " + sv.Summary);
        if (sv.Aircraft.Count != idle.Count) fail("not every idle aircraft is listed");
        foreach (var row in sv.Aircraft)
        {
            Console.WriteLine("  " + row.Title + " | " + row.Line + " | " + row.Best);
            var list = row.Suggestions;
            for (int i = 1; i < list.Count; i++) if (list[i].GainPerDay > list[i - 1].GainPerDay + 0.01) fail("not ranked by what they add");
            foreach (var x in list)
            {
                Console.WriteLine("    " + x.Name + " " + x.Forecast.RoundTrips + " trips, " + (x.Forecast.Load * 100).ToString("0") + "% full, flights " + x.Forecast.ProfitPerDay.ToString("0") + "/day, gates " + x.GatesPerDay.ToString("0") + ", own " + x.OwnershipPerDay.ToString("0"));
                if (x.Km > row.Airliner.Airliner.Range) fail("a route beyond the aircraft's range");
                if (h.Routes.Any(r => (r.Destination1 == x.From && r.Destination2 == x.To) || (r.Destination1 == x.To && r.Destination2 == x.From))) fail("a route the player already flies");
            }
        }
        var first = sv.Aircraft.First(a => a.Suggestions.Count > 0);
        if (first.Suggestions.Count < 3) fail("too few suggestions for " + first.Title);

        // the same forecast the route planner shows
        sv.Selected = first;
        var top = sv.Suggestions[0].Suggestion;
        var planner = new TheAirline.Presentation.CreateRouteViewModel(top.From, top.To, m => { });
        planner.SelectedAircraft = planner.Aircraft.First(a => a.Airliner == top.Airliner);
        if (planner.SuggestedForecast == null || planner.SuggestedForecast.RoundTrips != top.Forecast.RoundTrips || Math.Abs(planner.SuggestedForecast.ProfitPerDay - top.Forecast.ProfitPerDay) > 1)
            fail("the planner forecasts " + planner.SuggestedForecast?.RoundTrips + " trips " + planner.SuggestedForecast?.ProfitPerDay + ", the suggestion " + top.Forecast.RoundTrips + " " + top.Forecast.ProfitPerDay);

        // "Adjust first": the planner with the airports and the aircraft
        sv.Suggestions[1].AdjustCommand.Execute(null);
        var adj = sv.Suggestions[1].Suggestion;
        if (av.Pages[av.Page] != "Routes" || av.RoutesTab != 1 || av.Airports.Create == null || av.Airports.Create.SelectedAircraft?.Airliner != adj.Airliner || !av.Airports.Create.Title.Contains(adj.From.Profile.IATACode))
            fail("Adjust first didn't open the planner");
        av.Airports.Create.CancelCommand.Execute(null);

        // "Open route": one click, gates and all
        int routes = h.Routes.Count;
        av.RoutesTab = TheAirline.Presentation.AirlineViewModel.SuggestionsTabIndex;
        sv.Selected = sv.Aircraft.First(a => a.Airliner == top.Airliner);
        sv.Suggestions[0].OpenCommand.Execute(null);
        Console.WriteLine("  opened: " + sv.Status);
        var opened = h.Routes.FirstOrDefault(r => (r.Destination1 == top.From && r.Destination2 == top.To) || (r.Destination1 == top.To && r.Destination2 == top.From));
        if (h.Routes.Count != routes + 1 || opened == null) fail("the route wasn't opened");
        if (!opened.GetAirliners().Contains(top.Airliner)) fail("the aircraft isn't on the route");
        int monday = opened.TimeTable.Entries.Count(e => e.Day == DayOfWeek.Monday);
        if (monday != top.Forecast.RoundTrips * 2) fail("the schedule has " + monday + " Monday flights, the suggestion " + top.Forecast.RoundTrips + " round trips");
        await Wait(() => !sv.IsBusy, "suggestions");
        if (sv.Aircraft.Any(a => a.Airliner == top.Airliner)) fail("the aircraft is still listed as idle");
        if (sv.Aircraft.SelectMany(a => a.Suggestions).Any(x => (x.From == top.From && x.To == top.To) || (x.From == top.To && x.To == top.From))) fail("the opened route is still suggested");

        // the monthly report's idle aircraft lead here
        av.Report.Reload();
        av.Report.Month = av.Report.MonthChoices[0];
        var idleLine = av.Report.Attention.FirstOrDefault(x => x.Text.Contains("had no route"));
        if (idleLine == null || idleLine.LinkText != "Suggest routes") fail("the report's idle aircraft line");
        av.RoutesTab = 0;
        idleLine.Command.Execute(null);
        if (av.Pages[av.Page] != "Routes" || av.RoutesTab != TheAirline.Presentation.AirlineViewModel.SuggestionsTabIndex) fail("the report line didn't open the suggestions");

        // how the opened route did against its forecast over a month
        var f0 = top.Airliner;
        if (f0.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) f0.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;
        var start = g.GameTime;
        for (int i = 0; i < 35; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        double days = (g.GameTime - start).TotalDays - 5;
        double real = opened.GetBalance(start.AddDays(5), g.GameTime) / days;
        foreach (var x in new[] { h }.Concat(TheAirline.Helpers.CompetitorService.RivalRoutes(opened).Select(rr => rr.Airline)).Distinct())
            Console.WriteLine("  customer rating " + x.Profile.IATACode + ": " + x.Ratings?.CustomerHappinessRating.ToString("0.0") + " (factor " + TheAirline.Helpers.RatingsService.CustomerFactor(x).ToString("0.000") + "), on time " + TheAirline.Helpers.FlightStatistics.OnTimePercent(x).ToString("0") + "%");
        Console.WriteLine("  " + top.Name + ": forecast " + top.Forecast.ProfitPerDay.ToString("0") + " a day, flew " + real.ToString("0") + " a day over " + days.ToString("0") + " days (" + (opened.FillingDegree * 100).ToString("0") + "% full, forecast " + (top.Forecast.Load * 100).ToString("0") + "%)");
        // the load is the fair check (a few points of load move a thin margin a long way); a route may beat its forecast,
        // but not earn far less than it promised
        if (Math.Abs(opened.FillingDegree - top.Forecast.Load) > 0.15) fail("the route's load far from its forecast");
        if (real < top.Forecast.ProfitPerDay - Math.Max(1500, Math.Abs(top.Forecast.ProfitPerDay) * 0.5)) fail("the route earned far less than its forecast");
        Console.WriteLine("SUGGEST TEST OK");
    }

    /// <summary>Buys aircraft and opens that many flying routes for the player (the route advisor's picks).</summary>
    static async Task GivePlayerRoutes(AirlineViewModel av, int count)
    {
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var mk = av.Market;
        var ap = av.Airports;
        // two routes of the player's, flying
        for (int n = 0; n < count; n++)
        {
            foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Skip(n).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
            mk.BuyCommand.Execute(null);
            av.Refresh();
            ap.ReloadAdvice();
            await Wait(() => !ap.IsBusy, "advice");
            var advice = ap.Advice.Where(x => x.Advice.FleetCanFly).FirstOrDefault(x => !h.Routes.Any(r => r.Destination1 == x.Advice.Destination || r.Destination2 == x.Advice.Destination))
                         ?? ap.Advice.FirstOrDefault(x => !h.Routes.Any(r => r.Destination1 == x.Advice.Destination || r.Destination2 == x.Advice.Destination));
            if (advice == null) break;
            ap.SelectedAdvice = advice;
            foreach (var o in ap.Create.Offers.ToList()) o.LeaseCommand.Execute(null);
            ap.Create.CreateCommand.Execute(null);
            foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
            if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
        }
        foreach (var f in h.Fleet)
        {
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f);
            if (f.Routes.Count > 0 && f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) f.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;
        }
    }

    static async Task ReportTest(AirlineViewModel av)
    {
        Console.WriteLine("REPORT TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        var mk = av.Market;
        var ap = av.Airports;
        await GivePlayerRoutes(av, 2);
        if (h.Routes.Count < 2) fail("could not make two routes");
        Console.WriteLine("  routes: " + string.Join(", ", h.Routes.Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode)) + ", start " + g.GameTime.ToString("yyyy-MM-dd"));

        // play into the second month: the first month's report comes in the news
        int sent = TheAirline.Helpers.AirlineReportService.ReportsSent;
        var startMonth = g.GameTime.Month;
        while (g.GameTime.Month == startMonth) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        for (int i = 0; i < 5; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var last = g.GameTime.AddMonths(-1);
        if (TheAirline.Helpers.AirlineReportService.ReportsSent != sent + 1) fail("no report news at the new month");
        TheAirline.Models.General.News news;
        lock (g.NewsBox) news = g.NewsBox.GetNews().LastOrDefault(n => n.ActionObject is TheAirline.Helpers.ReportLink);
        if (news == null || !news.Subject.Contains(TheAirline.Helpers.AirlineReportService.MonthName(last.Year, last.Month))) fail("the report news");
        Console.WriteLine("  news: " + news.Subject + " | " + news.Body);

        // the month's figures
        var rv = av.Report;
        rv.Reload();
        var r = rv.Report;
        if (r == null || r.SoFar || r.Year != last.Year || r.Month != last.Month) fail("the report opens on the month that ended");
        if (!rv.MonthChoices[0].Contains("so far")) fail("this month so far isn't offered");
        Console.WriteLine("  " + rv.Summary);
        foreach (var t in rv.Tiles) Console.WriteLine("    " + t.Label + ": " + t.Value + (t.Change != null ? " (" + t.Change + ")" : ""));
        foreach (var m in rv.Money) Console.WriteLine("    " + m.Name + " " + m.Amount + " | " + m.Previous);
        foreach (var x in rv.Best.Concat(rv.Worst)) Console.WriteLine("    route " + x.Title + " | " + x.Text);
        foreach (var x in rv.Rivals) Console.WriteLine("    rival " + x.Title + " | " + x.Text);
        foreach (var x in rv.Attention) Console.WriteLine("    attention: " + x.Text + " [" + x.LinkText + "]");
        if (r.Ops.Flights == 0 || r.Ops.Passengers == 0 || r.Ops.Seats == 0) fail("no flying recorded");
        if (!r.Ops.Closed || Math.Abs(r.Cash) < 1) fail("no month-end cash");
        double res, inc;
        TheAirline.Helpers.CompetitorService.LastMonth(h, out res, out inc);
        if (Math.Abs(res - r.Result) > 1 || Math.Abs(inc - r.Income) > 1) fail("the result against the invoices: " + r.Result + " vs " + res);
        if (Math.Abs(r.Money.Sum(m => m.Amount) - r.Result) > 1) fail("the money lines don't add up to the result");
        var routeOps = TheAirline.Helpers.AirlineReportService.RouteOps(h, last.Year, last.Month);
        if (routeOps.Sum(x => x.Flights) != r.Ops.Flights || routeOps.Sum(x => x.Passengers) != r.Ops.Passengers) fail("the routes' flights don't add up to the airline's");
        foreach (var route in h.Routes)
        {
            var row = r.Best.Concat(r.Worst).FirstOrDefault(x => x.RouteId == route.Id);
            double p = TheAirline.Helpers.RouteProfitService.Month(route, last.Year, last.Month);
            if (row != null && Math.Abs(row.Profit - p) > 1) fail("a route's profit");
        }
        // the flights against the timetables: each route flies its week's flights every week (a day or two lost to positioning at the start)
        double expected = h.Routes.Sum(x => x.TimeTable.Entries.Count/7.0*DateTime.DaysInMonth(last.Year, last.Month));
        Console.WriteLine("  flights recorded " + r.Ops.Flights + ", the timetables call for " + expected.ToString("0"));
        if (r.Ops.Flights > expected + 2 || r.Ops.Flights < expected*0.85) fail("flights against the timetables");
        if (rv.Tiles.Count < 8) fail("tiles");
        if (!rv.Money.Any(m => m.Name == "Operating result")) fail("the result line");
        if (rv.Best.Count + rv.Worst.Count == 0) fail("no routes in the report");

        // the news link opens the report on that month
        av.Page = av.Pages.ToList().IndexOf("News");
        av.MyAirlineTab = 0;
        rv.Month = rv.MonthChoices[0];
        var nv = av.News;
        nv.Filter = "All";
        nv.Reload();
        nv.Selected = nv.Rows.First(x => x.News == news);
        if (!nv.HasLink || nv.LinkText != "Open the report") fail("the report news has no link");
        nv.OpenLinkCommand.Execute(null);
        if (av.Pages[av.Page] != "My Airline" || av.MyAirlineTab != TheAirline.Presentation.AirlineViewModel.ReportTabIndex || rv.Report.SoFar || rv.Report.Month != last.Month)
            fail("the news link didn't open the report");

        // a route line opens the route
        var routeLine = rv.Best.Concat(rv.Worst).First();
        routeLine.Command.Execute(null);
        if (av.Pages[av.Page] != "Routes" || av.SelectedRoute == null || !routeLine.Title.StartsWith(av.SelectedRoute.Route.Destination1.Profile.IATACode)) fail("a route line didn't open the route");

        // what needs attention: an idle aircraft, and too little cash
        foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        mk.BuyCommand.Execute(null);
        double money = h.Money;
        h.Money = 1000;
        rv.Month = rv.MonthChoices[0];
        var now = rv.Report;
        Console.WriteLine("  so far: " + rv.Summary);
        foreach (var x in rv.Attention) Console.WriteLine("    attention: " + x.Text + " [" + x.LinkText + "]");
        if (!now.SoFar || now.Ops.Idle < 1) fail("the idle aircraft isn't counted");
        var cash = rv.Attention.FirstOrDefault(x => x.Text.StartsWith("Cash of"));
        if (cash == null || !rv.Attention.Any(x => x.Text.Contains("had no route"))) fail("cash and idle aircraft aren't flagged");
        if (rv.Attention.First() != cash) fail("cash isn't first");
        av.Page = av.Pages.ToList().IndexOf("My Airline");
        av.MyAirlineTab = TheAirline.Presentation.AirlineViewModel.ReportTabIndex;
        cash.Command.Execute(null);
        if (av.MyAirlineTab != TheAirline.Presentation.AirlineViewModel.FinancesTabIndex) fail("the cash line didn't open Finances");
        h.Money = money;

        // a rival line opens the competitor screen on that airline
        if (rv.Rivals.Count > 0)
        {
            rv.Rivals[0].Command.Execute(null);
            if (av.Pages[av.Page] != "Airlines" || av.AirlinesTab != 1 || av.Competitors.Show != TheAirline.Presentation.CompetitorsViewModel.ByAirline || av.Competitors.Selected == null || !av.Competitors.Selected.Title.EndsWith(rv.Rivals[0].Title))
                fail("a rival line didn't open the competitor screen");
        }

        // saved with the game
        var before = TheAirline.Helpers.AirlineReportService.Ops(h, last.Year, last.Month);
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("reporttest");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("reporttest");
        g = TheAirline.Models.General.GameObject.GetInstance();
        h = g.HumanAirline;
        var after = TheAirline.Helpers.AirlineReportService.Ops(h, last.Year, last.Month);
        if (after.Flights != before.Flights || after.Passengers != before.Passengers || Math.Abs(after.Cash - before.Cash) > 0.01 || !after.Closed) fail("the month after loading");
        if (TheAirline.Helpers.AirlineReportService.RouteOps(h, last.Year, last.Month).Sum(x => x.Flights) != before.Flights) fail("the routes' months after loading");
        Console.WriteLine("  after loading: " + after.Flights + " flights, " + after.Passengers + " passengers, cash " + after.Cash.ToString("0"));

        // switched off in Options: no news at the next month
        av.Options.MonthlyReport = false;
        sent = TheAirline.Helpers.AirlineReportService.ReportsSent;
        int month = g.GameTime.Month;
        while (g.GameTime.Month == month) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        if (TheAirline.Helpers.AirlineReportService.ReportsSent != sent) fail("report news with the report switched off");
        if (!TheAirline.Helpers.AirlineReportService.Ops(h, g.GameTime.AddMonths(-1).Year, g.GameTime.AddMonths(-1).Month).Closed) fail("the month wasn't closed with the news off");
        av.Options.MonthlyReport = true;
        Console.WriteLine("REPORT TEST OK");
    }

    static void RivalAlertsTest(AirlineViewModel av)
    {
        Console.WriteLine("RIVAL ALERTS TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        Func<System.Collections.Generic.List<TheAirline.Models.General.News>> rivalNews = () =>
        {
            lock (g.NewsBox) return g.NewsBox.GetNews().Where(n => n.Type == TheAirline.Models.General.News.NewsType.RivalNews).ToList();
        };
        // my route on a pair a computer airline flies, dearer than the rival
        var theirs = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList())
                                .OfType<TheAirline.Models.Routes.PassengerRoute>().First(r => r.HasAirliner && TheAirline.Helpers.AIPricingService.EconomyFare(r) > 0);
        double theirFare = TheAirline.Helpers.AIPricingService.EconomyFare(theirs);
        var mine = new TheAirline.Models.Routes.PassengerRoute(Guid.NewGuid().ToString(), theirs.Destination1, theirs.Destination2, g.GameTime, theirFare * 1.2);
        Action<double> setMine = fare => { foreach (var c in mine.Classes) c.FarePrice = fare * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type); };
        setMine(theirFare * 1.2);
        if (!me.Airports.Contains(theirs.Destination1)) me.AddAirport(theirs.Destination1);
        if (!me.Airports.Contains(theirs.Destination2)) me.AddAirport(theirs.Destination2);
        me.AddRoute(mine);
        string pair = theirs.Destination1.Profile.IATACode + "-" + theirs.Destination2.Profile.IATACode;
        Console.WriteLine("  my route " + pair + ", rival " + theirs.Airline.Profile.IATACode);
        // and the pairs the computer airlines fly from my airports, so a month of play brings alerts
        foreach (var r in TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(x => !x.IsHuman).SelectMany(x => x.Routes.ToList())
                                 .OfType<TheAirline.Models.Routes.PassengerRoute>().Where(r => r.HasAirliner && (me.Airports.Contains(r.Destination1) || me.Airports.Contains(r.Destination2))).Take(15))
        {
            if (!me.Airports.Contains(r.Destination1)) me.AddAirport(r.Destination1);
            if (!me.Airports.Contains(r.Destination2)) me.AddAirport(r.Destination2);
            var mr = new TheAirline.Models.Routes.PassengerRoute(Guid.NewGuid().ToString(), r.Destination1, r.Destination2, g.GameTime, 1);
            double f0 = TheAirline.Helpers.AIPricingService.EconomyFare(r) * 1.1;
            foreach (var c in mr.Classes) c.FarePrice = f0 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
            me.AddRoute(mr);
        }
        for (int i = 0; i < 30; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  alerts in 30 days of play: " + rivalNews().Count + " (" + TheAirline.Helpers.CompetitorService.AlertsSent + " sent, " + TheAirline.Helpers.CompetitorService.AlertsHeld + " held back)");
        foreach (var n in rivalNews().Take(3)) Console.WriteLine("    " + n.Subject + " | " + n.Body);
        if (rivalNews().Any(n => !(n.ActionObject is TheAirline.Helpers.RivalLink))) fail("an alert without its link");
        if (g.NewsBox.GetNews().Any(n => n.Subject.Contains(" cuts fares on ") || n.Subject.Contains(" cuts cargo rates on "))) fail("the old fare cut news is still sent");

        TheAirline.Helpers.CompetitorService.Clear();
        int before = rivalNews().Count;
        Func<TheAirline.Models.General.News> newest = () => rivalNews().LastOrDefault();

        // opened
        TheAirline.Helpers.CompetitorService.RecordOpened(theirs);
        if (rivalNews().Count != before + 1 || !newest().Subject.Contains("now flies " + pair)) fail("no alert for the route opened");
        Console.WriteLine("  " + newest().Subject + " | " + newest().Body);
        TheAirline.Helpers.CompetitorService.RecordOpened(theirs);
        if (rivalNews().Count != before + 1) fail("the same alert was sent twice in a month");

        // fare cuts: too shallow, not below mine, then below mine
        TheAirline.Helpers.CompetitorService.RecordFare(theirs.Airline, theirs, 0.99, 0.7);
        if (rivalNews().Count != before + 1) fail("a 1% cut was told");
        setMine(theirFare * 0.5);
        TheAirline.Helpers.CompetitorService.RecordFare(theirs.Airline, theirs, 0.9, 0.7);
        if (rivalNews().Count != before + 1) fail("a cut that leaves the rival dearer than me was told");
        setMine(theirFare * 1.2);
        TheAirline.Helpers.CompetitorService.RecordFare(theirs.Airline, theirs, 0.9, 0.7);
        if (rivalNews().Count != before + 2 || !newest().Subject.Contains("undercuts you on " + pair) || !newest().Body.Contains("70% full")) fail("no alert for the cut below my fare");
        Console.WriteLine("  " + newest().Subject + " | " + newest().Body);

        // given up
        TheAirline.Helpers.CompetitorService.RecordDropped(theirs.Airline, theirs);
        if (rivalNews().Count != before + 3 || !newest().Subject.Contains("leaves " + pair)) fail("no alert for the route given up");
        Console.WriteLine("  " + newest().Subject + " | " + newest().Body);

        // a move on a pair I don't fly isn't told
        var other = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList())
                               .FirstOrDefault(r => !me.Routes.Any(m => (m.Destination1 == r.Destination1 && m.Destination2 == r.Destination2) || (m.Destination1 == r.Destination2 && m.Destination2 == r.Destination1)));
        if (other != null)
        {
            TheAirline.Helpers.CompetitorService.RecordOpened(other);
            if (rivalNews().Count != before + 3) fail("an alert for a route I don't fly");
        }

        // the switch in Options
        TheAirline.Helpers.CompetitorService.Clear();
        if (!av.Options.RivalAlerts) fail("alerts are off by default");
        av.Options.RivalAlerts = false;
        if (!TheAirline.Infrastructure.Settings.GetInstance().NoRivalAlerts) fail("the Options switch doesn't reach the setting");
        TheAirline.Helpers.CompetitorService.RecordOpened(theirs);
        if (rivalNews().Count != before + 3) fail("an alert with alerts switched off");
        av.Options.RivalAlerts = true;
        TheAirline.Helpers.CompetitorService.RecordOpened(theirs);
        if (rivalNews().Count != before + 4) fail("no alert once switched back on");

        // the link in the news: Airlines > Competitors, My routes, my route on that pair
        var nv = av.News;
        nv.Filter = "Rivals";
        nv.Reload();
        if (nv.Rows.Count == 0 || nv.Rows.Any(r => r.Type != "Rivals")) fail("the Rivals filter");
        nv.Selected = nv.Rows.First(r => r.News == newest());
        if (!nv.HasLink) fail("the alert has no link");
        nv.OpenLinkCommand.Execute(null);
        var cv = av.Competitors;
        if (av.Pages[av.Page] != "Airlines" || av.AirlinesTab != 1) fail("the link didn't open Airlines > Competitors");
        if (cv.Show != TheAirline.Presentation.CompetitorsViewModel.ByRoute || cv.Selected?.MyRoute != mine) fail("the link didn't select my route");
        Console.WriteLine("  link opened: " + av.Pages[av.Page] + " > Competitors > " + cv.Show + " > " + cv.Selected.Title + " | " + cv.Selected.Line1);

        // saved with the game: the alert and its link
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("rivalalerts");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("rivalalerts");
        g = TheAirline.Models.General.GameObject.GetInstance();
        var loaded = rivalNews().LastOrDefault(n => n.Subject.Contains(pair));
        var link = loaded?.ActionObject as TheAirline.Helpers.RivalLink;
        if (link == null || !((link.A + "-" + link.B) == pair) || link.IsCargo) fail("the alert's link after loading");
        Console.WriteLine("  after loading: " + rivalNews().Count + " alerts, link " + link.A + "-" + link.B + " (" + link.Airline + ")");

        // a link to a route I no longer fly says so
        me = g.HumanAirline;
        foreach (var r in me.Routes.ToList().Where(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode == pair)) me.RemoveRoute(r);
        av.Page = av.Pages.ToList().IndexOf("News");
        nv.Reload();
        nv.Selected = nv.Rows.FirstOrDefault(r => r.News == loaded);
        if (nv.Selected == null) fail("the loaded alert isn't listed");
        nv.OpenLinkCommand.Execute(null);
        if (string.IsNullOrEmpty(nv.LinkNote) || av.Pages[av.Page] != "News") fail("a link to a route I gave up");
        Console.WriteLine("  gone route: " + nv.LinkNote);
        Console.WriteLine("RIVAL ALERTS TEST OK");
    }

    static void CompetitorsTest(AirlineViewModel av)
    {
        Console.WriteLine("COMPETITORS TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        for (int i = 0; i < 30; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  remembered moves after 30 days: " + TheAirline.Helpers.CompetitorService.Remembered);
        if (TheAirline.Helpers.CompetitorService.Remembered == 0) fail("no rival move remembered");

        // a route of the player's on a pair a computer airline flies, dearer than the rival
        var theirs = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList())
                                .OfType<TheAirline.Models.Routes.PassengerRoute>().First(r => r.HasAirliner && TheAirline.Helpers.AIPricingService.EconomyFare(r) > 0);
        double theirFare = TheAirline.Helpers.AIPricingService.EconomyFare(theirs);
        var mine = new TheAirline.Models.Routes.PassengerRoute(Guid.NewGuid().ToString(), theirs.Destination1, theirs.Destination2, g.GameTime, theirFare * 1.2);
        foreach (var c in mine.Classes) c.FarePrice = theirFare * 1.2 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
        if (!me.Airports.Contains(theirs.Destination1)) me.AddAirport(theirs.Destination1);
        if (!me.Airports.Contains(theirs.Destination2)) me.AddAirport(theirs.Destination2);
        me.AddRoute(mine);
        Console.WriteLine("  my route " + theirs.Destination1.Profile.IATACode + "-" + theirs.Destination2.Profile.IATACode + " at " + (theirFare * 1.2).ToString("0") + ", " + theirs.Airline.Profile.IATACode + " at " + theirFare.ToString("0"));

        var rr = TheAirline.Helpers.CompetitorService.ForRoutes(me).FirstOrDefault(x => x.Mine == mine);
        if (rr == null || !rr.Rivals.Any(x => x.Route == theirs)) fail("the rival isn't on my route");
        if (Math.Abs(rr.Rivals.First(x => x.Route == theirs).Fare - theirFare) > 0.01) fail("the rival's fare");
        var rival = TheAirline.Helpers.CompetitorService.Rivals(me).FirstOrDefault(x => x.Airline == theirs.Airline);
        if (rival == null || rival.SharedRoutes.Count == 0 || rival.SharedAirports.Count == 0) fail("the rival airline isn't listed with the shared route and airports");
        if (!(rival.FareAgainstMine > 0.8 && rival.FareAgainstMine < 0.86)) fail("fare against mine " + rival.FareAgainstMine);
        var ap = TheAirline.Helpers.CompetitorService.ForAirports(me).FirstOrDefault(x => x.Airport == theirs.Destination1);
        if (ap == null || !ap.Airlines.Any(x => x.Item1 == theirs.Airline)) fail("the rival isn't at my airport");
        Console.WriteLine("  " + rival.Airline.Profile.Name + ": " + rival.SharedRoutes.Count + " shared routes, " + rival.SharedAirports.Count + " airports, fares " + rival.FareAgainstMine.ToString("0.00") + " of mine");

        // a fare cut on my pair and a route given up are listed as moves
        TheAirline.Helpers.CompetitorService.RecordFare(theirs.Airline, theirs, 0.9);
        TheAirline.Helpers.CompetitorService.RecordDropped(theirs.Airline, theirs);
        var moves = TheAirline.Helpers.CompetitorService.Moves(me, 30);
        if (!moves.Any(m => m.Kind == TheAirline.Helpers.RivalMoveKind.FareCut && m.Airline == theirs.Airline && Math.Abs(m.Factor - 0.9) < 1e-9)) fail("no fare cut move");
        if (!moves.Any(m => m.Kind == TheAirline.Helpers.RivalMoveKind.Dropped && m.RouteId == theirs.Id)) fail("no dropped move");
        Console.WriteLine("  " + moves.Count + " moves at my airports in 30 days");

        // the screen
        var cv = av.Competitors;
        foreach (var show in cv.ShowChoices)
        {
            cv.Show = show;
            cv.Reload(); // (the Airlines page reloads it when opened)
            if (cv.Rows.Count == 0) fail("no rows for " + show);
            if (cv.Selected == null || cv.DetailLines.Count == 0) fail("no detail for " + show);
            Console.WriteLine("  " + show + ": " + cv.Rows.Count + " rows | " + cv.Summary);
            Console.WriteLine("    " + cv.Rows[0].Title + " | " + cv.Rows[0].Line1 + " | " + cv.Rows[0].Line2);
            Console.WriteLine("    " + string.Join(" / ", cv.DetailLines.Take(3)));
        }
        cv.Show = TheAirline.Presentation.CompetitorsViewModel.ByRoute;
        cv.Filter = "A rival is cheaper";
        var row = cv.Rows.FirstOrDefault(r => r.MyRoute == mine);
        if (row == null || row.Badge != "CHEAPER RIVAL") fail("my route isn't flagged as undercut");
        cv.Selected = row;
        if (!cv.CanOpenRoute) fail("can't open my route");
        av.Refresh();
        cv.OpenCommand.Execute(null);
        if (av.Pages[av.Page] != "Routes" || av.SelectedRoute?.Route != mine) fail("opening my route didn't show it");
        cv.Show = TheAirline.Presentation.CompetitorsViewModel.ByMove;
        cv.Filter = "On my routes only";
        if (!cv.Rows.Any(r => r.Badge == "YOUR ROUTE")) fail("moves on my route not shown");

        me.RemoveRoute(mine);
        Console.WriteLine("COMPETITORS TEST OK");
    }

    // the world map shows rivals in their own colours: one airline, those on my routes, everyone at an airport
    static void RivalMapTest(AirlineViewModel av)
    {
        Console.WriteLine("RIVAL MAP TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        for (int i = 0; i < 20; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        // a route of mine on a pair a rival flies
        var theirs = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList())
                                .OfType<TheAirline.Models.Routes.PassengerRoute>().First(r => r.HasAirliner && r.Airline.Routes.Count >= 3);
        var mine = new TheAirline.Models.Routes.PassengerRoute(Guid.NewGuid().ToString(), theirs.Destination1, theirs.Destination2, g.GameTime, 100);
        if (!me.Airports.Contains(theirs.Destination1)) me.AddAirport(theirs.Destination1);
        if (!me.Airports.Contains(theirs.Destination2)) me.AddAirport(theirs.Destination2);
        me.AddRoute(mine);
        // half way through a flight of theirs, so their aircraft are in the air
        var map = av.Map;
        map.Reload();
        int myLines = map.Lines.Count, myAircraft = map.Aircraft.Count;
        if (map.Lines.Any(l => l.Color != null) || map.Aircraft.Any(a => !a.IsMine)) fail("rivals shown before choosing them");
        Console.WriteLine("  choices: " + map.RivalChoices.Count + " (" + string.Join(" | ", map.RivalChoices.Take(4).Select(c => c.Name)) + " ...)");
        if (!map.RivalChoices.Any(c => c.Value.Airline == theirs.Airline)) fail("the rival isn't a choice");
        if (!map.RivalChoices.Any(c => c.Value.Airport == theirs.Destination1)) fail("my airport isn't a choice");

        // one airline: all its routes in one colour
        map.ShowRivals(new TheAirline.Presentation.RivalView {Airline = theirs.Airline});
        var rl = map.Lines.Where(l => l.Color != null).ToList();
        int theirRoutes = theirs.Airline.Routes.Count(r => r.HasAirliner);
        Console.WriteLine("  " + theirs.Airline.Profile.Name + ": " + rl.Count + " lines (" + theirRoutes + " flying routes), " + map.Aircraft.Count(a => !a.IsMine) + " aircraft | legend: " + string.Join("; ", map.Legend.Select(x => x.Text)));
        if (rl.Count == 0 || rl.Select(l => l.Color).Distinct().Count() != 1) fail("one airline, one colour");
        if (map.Lines.Count(l => l.Color == null) != myLines) fail("my routes changed");
        if (map.Legend.Count != 2 || !map.HasLegend) fail("legend");
        if (map.Aircraft.Where(a => !a.IsMine).Any(a => a.FlightRoute == null || a.FlightRoute.Airline != theirs.Airline || a.Color != rl[0].Color)) fail("rival aircraft");
        if (!map.Places.Any(p => p.IsRivalOnly) && theirs.Airline.Routes.SelectMany(r => new[] {r.Destination1, r.Destination2}).Distinct().Count() > 2) fail("their airports");

        // the airlines on my routes: only my pairs
        map.ShowRivals(new TheAirline.Presentation.RivalView {OnMyRoutes = true});
        var onMine = map.Lines.Where(l => l.Color != null).ToList();
        Console.WriteLine("  on my routes: " + onMine.Count + " lines | legend: " + string.Join("; ", map.Legend.Select(x => x.Text)));
        if (onMine.Count == 0) fail("no rival on my routes");
        if (onMine.Count > me.Routes.Count * TheAirline.Presentation.WorldMapViewModel.RivalColors.Length) fail("rival lines beyond my routes");

        // everyone at an airport: a colour each, up to the palette
        map.ShowRivals(new TheAirline.Presentation.RivalView {Airport = theirs.Destination1});
        var at = map.Lines.Where(l => l.Color != null).ToList();
        Console.WriteLine("  everyone at " + theirs.Destination1.Profile.IATACode + ": " + at.Count + " lines, " + at.Select(l => l.Color).Distinct().Count() + " colours, " + map.Aircraft.Count(a => !a.IsMine) + " aircraft | " + map.Caption);
        if (at.Count == 0) fail("nobody at the airport");
        if (map.Aircraft.Count(a => !a.IsMine) == 0) fail("no rival aircraft at the airport");
        if (at.Select(l => l.Color).Distinct().Count() > TheAirline.Presentation.WorldMapViewModel.RivalColors.Length) fail("too many colours");

        // the choice survives the refreshes and a new day; "No rivals" clears them
        av.Refresh();
        TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        map.Reload();
        if (map.SelectedRival?.Value.Airport != theirs.Destination1) fail("the choice was lost on a new day");
        // an airline that doesn't fly to my airports can be shown too (it is added to the list)
        var far = TheAirline.Models.Airlines.Airlines.GetAllAirlines().FirstOrDefault(a => !a.IsHuman && a.Routes.Any(r => r.HasAirliner) && !map.RivalChoices.Any(c => c.Value.Airline == a));
        if (far != null)
        {
            map.ShowRivals(new TheAirline.Presentation.RivalView { Airline = far });
            if (map.SelectedRival?.Value.Airline != far || !map.Aircraft.Any(a => !a.IsMine)) fail("an airline away from my airports isn't shown");
            map.Reload();
            if (map.SelectedRival?.Value.Airline != far) fail("the added choice was lost on a refresh");
            Console.WriteLine("  " + far.Profile.Name + " (not at my airports): " + map.Aircraft.Count(a => !a.IsMine) + " aircraft shown");
        }
        map.ShowRivals(TheAirline.Presentation.RivalView.None);
        if (map.Lines.Any(l => l.Color != null) || map.Aircraft.Any(a => !a.IsMine) || map.Places.Any(p => p.IsRivalOnly) || map.HasLegend) fail("rivals still shown");
        me.RemoveRoute(mine);
        Console.WriteLine("RIVAL MAP TEST OK");
    }

    // the flight engine's quick sum of connecting passengers gives the same as the markets the Connections page lists
    static void FastDemandTest()
    {
        Console.WriteLine("FAST DEMAND TEST");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int i = 0; i < 45; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        int n = 0, nonzero = 0;
        double worst = 0;
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines())
        foreach (var r in a.Routes.ToList().Where(r => !r.IsCargoRoute).Take(60))
        foreach (var t in new[] { TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass, TheAirline.Models.Airliners.AirlinerClass.ClassType.BusinessClass })
        foreach (var dir in new[] { Tuple.Create(r.Destination1, r.Destination2), Tuple.Create(r.Destination2, r.Destination1) })
        {
            double slow = TheAirline.Helpers.ConnectionService.FlightDemandByMarkets(a, r, dir.Item1, dir.Item2, t);
            double fast = TheAirline.Helpers.ConnectionService.FlightDemandFresh(a, r, dir.Item1, dir.Item2, t);
            n++;
            if (slow > 0) nonzero++;
            worst = Math.Max(worst, Math.Abs(slow - fast));
            if (Math.Abs(slow - fast) > 1e-9) { Console.WriteLine("  FAILED " + a.Profile.IATACode + " " + dir.Item1.Profile.IATACode + "-" + dir.Item2.Profile.IATACode + " " + t + ": markets " + slow + ", quick " + fast); Environment.Exit(1); }
        }
        Console.WriteLine("  " + n + " flights compared (" + nonzero + " with connecting passengers), largest difference " + worst);
        if (nonzero == 0) { Console.WriteLine("  FAILED nothing to compare"); Environment.Exit(1); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int k = 0;
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines())
        foreach (var r in a.Routes.ToList().Where(r => !r.IsCargoRoute).Take(60)) { TheAirline.Helpers.ConnectionService.FlightDemandByMarkets(a, r, r.Destination1, r.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass); k++; }
        double slowMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines())
        foreach (var r in a.Routes.ToList().Where(r => !r.IsCargoRoute).Take(60)) TheAirline.Helpers.ConnectionService.FlightDemandFresh(a, r, r.Destination1, r.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
        Console.WriteLine(string.Format("  {0} flights: markets {1:0} ms, quick {2:0} ms", k, slowMs, sw.Elapsed.TotalMilliseconds));
        Console.WriteLine("FAST DEMAND TEST OK");
    }

    // "vm tripprobe": what one more round trip a day really earns, against the forecast. From one saved game, a month
    // is played twice: as it is, and with one more round trip on a sample of computer routes.
    static void TripProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        int warm = int.Parse(Environment.GetEnvironmentVariable("TAP_WARM") ?? "75");
        int sample = int.Parse(Environment.GetEnvironmentVariable("TAP_SAMPLE") ?? "80");
        for (int i = 0; i < warm; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        while (g.GameTime.Day != 1) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var start = g.GameTime;
        Func<System.Collections.Generic.List<TheAirline.Models.Routes.PassengerRoute>> pick = () =>
            TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList())
                .OfType<TheAirline.Models.Routes.PassengerRoute>()
                .Where(r => !(r is TheAirline.Models.Routes.HelicopterRoute) && r.GetAirliners().Count == 1 && r.StartDate < start.AddDays(-30) && !r.HasStopovers)
                .Where(r => { var f = r.GetAirliners()[0]; int n = TheAirline.Helpers.AIRouteChoice.CurrentTrips(r);
                              return n >= 2 && r.TimeTable.Entries.Count == 14 * n && !TheAirline.Helpers.AIRouteChoice.IsBusiness(r, f); })
                .OrderBy(r => r.Id).ToList();
        var chosen = pick();
        // a spread sample: every k-th
        int step = Math.Max(1, chosen.Count / sample);
        var ids = chosen.Where((r, i) => i % step == 0).Take(sample).Select(r => r.Id).ToList();
        Console.WriteLine("TRIPPROBE " + start.ToString("dd MMM yyyy") + ": " + chosen.Count + " routes fly two round trips a day or more; sample " + ids.Count);
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("tripprobe");

        Func<bool, System.Collections.Generic.Dictionary<string, double[]>> play = extra =>
        {
            TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("tripprobe");
            var gg = TheAirline.Models.General.GameObject.GetInstance();
            var byId = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(a => a.Routes.ToList()).OfType<TheAirline.Models.Routes.PassengerRoute>().GroupBy(r => r.Id).ToDictionary(x => x.Key, x => x.First());
            var info = new System.Collections.Generic.Dictionary<string, double[]>();
            foreach (var id in ids)
            {
                TheAirline.Models.Routes.PassengerRoute r;
                if (!byId.TryGetValue(id, out r)) continue;
                var f = r.GetAirliners()[0];
                int n = TheAirline.Helpers.AIRouteChoice.CurrentTrips(r);
                var fc = TheAirline.Helpers.RouteForecastService.ForecastEach(r.Airline, r.Destination1, r.Destination2, f, new[] { n - 1, n }, r);
                if (extra)
                {
                    string c1 = r.TimeTable.Entries.First(e => e.Destination.Airport == r.Destination2).Destination.FlightCode;
                    string c2 = r.TimeTable.Entries.First(e => e.Destination.Airport == r.Destination1).Destination.FlightCode;
                    r.TimeTable = TheAirline.Helpers.AIHelpers.CreateAirlinerRouteTimeTable(r, f, n - 1, c1, c2);
                }
                // forecast profit a day at n and n+1, forecast load at n and n+1, n
                info[id] = new double[] { fc[0].ProfitPerDay, fc[1].ProfitPerDay, fc[0].Load, fc[1].Load, n - 1, 0, 0, 0, fc[0].RevenuePerFlight * (n - 1) * 2, fc[1].RevenuePerFlight * n * 2 };
            }
            int month = gg.GameTime.Month;
            while (gg.GameTime.Month == month) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
            var last = gg.GameTime.AddMonths(-1);
            int days = DateTime.DaysInMonth(last.Year, last.Month);
            var byId2 = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(a => a.Routes.ToList()).OfType<TheAirline.Models.Routes.PassengerRoute>().GroupBy(r => r.Id).ToDictionary(x => x.Key, x => x.First());
            foreach (var id in info.Keys.ToList())
            {
                TheAirline.Models.Routes.PassengerRoute r;
                if (!byId2.TryGetValue(id, out r)) { info.Remove(id); continue; }
                info[id][5] = TheAirline.Helpers.RouteProfitService.Month(r, last.Year, last.Month) / days;
                info[id][6] = TheAirline.Helpers.ConnectionService.MonthLoad(r);
                info[id][7] = TheAirline.Helpers.RouteProfitService.Month(r, TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, last.Year, last.Month) / days;
            }
            return info;
        };
        var a0 = play(false);
        var b0 = play(true);
        var both = a0.Keys.Intersect(b0.Keys).ToList();
        double fcGain = 0, realGain = 0, fcTk = 0, realTk = 0; int agree = 0, fcSaysNo = 0, realSaysYes = 0;
        foreach (var id in both)
        {
            var A = a0[id]; var B = b0[id];
            double fg = A[1] - A[0], rg = A[5] - B[5];
            fcGain += fg; realGain += rg;
            fcTk += A[9] - A[8]; realTk += A[7] - B[7];
            if (Math.Sign(fg) == Math.Sign(rg)) agree++;
            if (fg < 0) fcSaysNo++;
            if (rg > 0) realSaysYes++;
            Console.WriteLine(string.Format("  {0} trips {1}->{2}: load forecast {3:0}%->{4:0}% real {5:0}%->{6:0}% | tickets a day forecast +{7:N0} real {8:+#,0;-#,0} | profit a day forecast {9:+#,0;-#,0} real {10:+#,0;-#,0}",
                id.Substring(0, 6), A[4], A[4] + 1, A[2] * 100, A[3] * 100, B[6] * 100, A[6] * 100, A[9] - A[8], A[7] - B[7], fg, rg));
        }
        int k = Math.Max(1, both.Count);
        Console.WriteLine(string.Format("TRIPPROBE {0} routes: the last round trip a day earns, on average, forecast {1:+#,0;-#,0} real {2:+#,0;-#,0} a day | tickets forecast {3:+#,0;-#,0} real {4:+#,0;-#,0} | forecast says worth it on {5}, really worth it on {6}, same answer on {7}",
            both.Count, fcGain / k, realGain / k, fcTk / k, realTk / k, both.Count - fcSaysNo, realSaysYes, agree));
        if (Environment.GetCommandLineArgs().Contains("tripadvice"))
        {
            Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
            if (both.Count < 10) fail("too few routes to compare: " + both.Count);
            if (agree < both.Count * 0.85) fail("the forecast and the flights disagree on " + (both.Count - agree) + " of " + both.Count + " routes");
            if (realGain / k > 0 && (fcGain / k < realGain / k * 0.6 || fcGain / k > realGain / k * 1.4)) fail("the forecast gain is far from the real one");

            // the advice on one of the player's routes: a busy pair flown once a day gets "more round trips"
            var gg = TheAirline.Models.General.GameObject.GetInstance();
            var me = gg.HumanAirline;
            var busy = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList()).OfType<TheAirline.Models.Routes.PassengerRoute>()
                .Where(r => r.HasAirliner && !r.HasStopovers && r.GetAirliners().Count == 1 && TheAirline.Helpers.AIRouteChoice.CurrentTrips(r) >= 3 && TheAirline.Helpers.ConnectionService.MonthLoad(r) > 0.9).OrderBy(r => r.Id).FirstOrDefault();
            if (busy == null) fail("no busy route to copy");
            var src = busy.GetAirliners()[0];
            var al = new TheAirline.Models.Airliners.Airliner("adv", src.Airliner.Type, "N-ADV1", gg.GameTime.AddYears(-2)) { Airline = me };
            TheAirline.Models.Airliners.Airliners.AddAirliner(al);
            var fa = TheAirline.Helpers.AirlineHelpers.AddAirliner(me, al, busy.Destination1, false);
            TheAirline.Helpers.AirlinerHelpers.CreateAirlinerClasses(al);
            var mine = new TheAirline.Models.Routes.PassengerRoute(Guid.NewGuid().ToString(), busy.Destination1, busy.Destination2, gg.GameTime.AddDays(-60), TheAirline.Helpers.AIPricingService.EconomyFare(busy));
            foreach (var c in mine.Classes) c.FarePrice = TheAirline.Helpers.PricingService.MarketFare(busy.Destination1, busy.Destination2, c.Type);
            if (!me.Airports.Contains(busy.Destination1)) me.AddAirport(busy.Destination1);
            if (!me.Airports.Contains(busy.Destination2)) me.AddAirport(busy.Destination2);
            me.AddRoute(mine);
            fa.AddRoute(mine);
            mine.TimeTable = TheAirline.Helpers.AIHelpers.CreateAirlinerRouteTimeTable(mine, fa, 1, "ADV1", "ADV2");
            var adv = TheAirline.Helpers.RouteForecastService.AdviseTrips(me, mine);
            if (adv == null) fail("no advice for my route");
            Console.WriteLine("  " + busy.Destination1.Profile.IATACode + "-" + busy.Destination2.Profile.IATACode + " once a day (" + busy.Airline.Profile.IATACode + " flies it " + TheAirline.Helpers.AIRouteChoice.CurrentTrips(busy) + " times, " + (TheAirline.Helpers.ConnectionService.MonthLoad(busy) * 100).ToString("0") + "% full): best " + adv.Best.RoundTrips + " of up to " + adv.Max + " | " + (adv.Text ?? "no change"));
            if (adv.Change && adv.Best.RoundTrips <= 1) fail("advice to fly less than once a day");
            var fig = TheAirline.Helpers.RouteProfitService.Figures(mine);
            if (adv.Change && (fig.Trips == null || !fig.Advice.StartsWith(adv.Text))) fail("Route profits doesn't show the advice");
            // at the suggested number, no further change is advised
            if (adv.Change)
            {
                mine.TimeTable = TheAirline.Helpers.AIHelpers.CreateAirlinerRouteTimeTable(mine, fa, adv.Best.RoundTrips, "ADV1", "ADV2");
                var again = TheAirline.Helpers.RouteForecastService.AdviseTrips(me, mine);
                Console.WriteLine("  at " + adv.Best.RoundTrips + ": " + (again.Text ?? "no change advised"));
                if (again.Change) fail("advice keeps changing");
            }
            Console.WriteLine("TRIP ADVICE TEST OK");
        }
    }

    // "vm newprobe": a new route's forecast when it opens against its first full month
    static void NewRouteProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        int warm = int.Parse(Environment.GetEnvironmentVariable("TAP_WARM") ?? "60");
        for (int i = 0; i < warm; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var made = new System.Collections.Concurrent.ConcurrentDictionary<TheAirline.Models.Routes.Route, Tuple<TheAirline.Helpers.RouteForecast, TheAirline.Helpers.RouteForecast, int, double[]>>();
        TheAirline.Helpers.AIRouteChoice.RecordCreation = (r, f, n) =>
        {
            var a = f.Airliner.Airline;
            double fares = TheAirline.Helpers.AIRouteChoice.FareFactor(a);
            var fc = TheAirline.Helpers.RouteForecastService.ForecastEach(a, r.Destination1, r.Destination2, f, new[] { n, Math.Max(1, n - 1) }, null, fares);
            // the parts: reputation share at both ends, the usual score, rivals
            // (the reputation the forecast now predicts for a new route: today's scores, cached before the route was made, with it added)
            double add = 7 + 6 * TheAirline.Helpers.RouteForecastService.NewRouteService(a);
            made[r] = Tuple.Create(fc[0], fc[1], n, new double[] { TheAirline.Helpers.PassengerHelpers.ReputationShare(r.Destination1, a), TheAirline.Helpers.PassengerHelpers.ReputationShare(r.Destination2, a), TheAirline.Helpers.RouteForecastService.UsualScoreOf(a), fares,
                (TheAirline.Helpers.PassengerHelpers.ReputationShareWith(r.Destination1, a, add) + TheAirline.Helpers.PassengerHelpers.ReputationShareWith(r.Destination2, a, add)) / 2 });
        };
        int days = int.Parse(Environment.GetEnvironmentVariable("TAP_DAYS") ?? "20");
        for (int i = 0; i < days; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        TheAirline.Helpers.AIRouteChoice.RecordCreation = null;
        // to the end of the next full month
        int m0 = g.GameTime.Month;
        while (g.GameTime.Month == m0) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        int m1 = g.GameTime.Month;
        while (g.GameTime.Month == m1) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var last = g.GameTime.AddMonths(-1);
        int dim = DateTime.DaysInMonth(last.Year, last.Month);
        double fl = 0, rl = 0, fp = 0, rp = 0, ft = 0, rt = 0; int n2 = 0;
        var bands = new System.Collections.Generic.List<Tuple<double, double, double>>(); // forecast load, real load, real profit a day
        double sScore = 0, sRep = 0, sRealScore = 0, sRealRep = 0, sFare = 0, sRealFare = 0, sPred = 0;
        foreach (var kv in made)
        {
            var r = kv.Key as TheAirline.Models.Routes.PassengerRoute;
            if (r == null || r.Airline == null || !r.Airline.Routes.Contains(r) || r.GetAirliners().Count != 1 || TheAirline.Helpers.AIRouteChoice.CurrentTrips(r) != kv.Value.Item3) continue;
            var f = r.GetAirliners()[0];
            double realLoad = TheAirline.Helpers.ConnectionService.MonthLoad(r);
            double realProfit = TheAirline.Helpers.RouteProfitService.Month(r, last.Year, last.Month) / dim;
            double realTickets = TheAirline.Helpers.RouteProfitService.Month(r, TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, last.Year, last.Month) / dim;
            var fc = kv.Value.Item1;
            double own = TheAirline.Helpers.OwnershipService.MonthlyCost(f) / 30.4;
            fl += fc.Load; rl += realLoad; fp += fc.ProfitPerDay + own; rp += realProfit; ft += fc.RevenuePerFlight * fc.RoundTrips * 2; rt += realTickets; n2++;
            bands.Add(Tuple.Create(fc.Load, realLoad, realProfit));
            sScore += kv.Value.Item4[2]; sRealScore += TheAirline.Helpers.PassengerHelpers.RouteScoreFor(r);
            sRep += (kv.Value.Item4[0] + kv.Value.Item4[1]) / 2;
            sPred += kv.Value.Item4[4];
            sRealRep += (TheAirline.Helpers.PassengerHelpers.ReputationShare(r.Destination1, r.Airline) + TheAirline.Helpers.PassengerHelpers.ReputationShare(r.Destination2, r.Airline)) / 2;
            double market = TheAirline.Helpers.PricingService.MarketFare(r.Destination1, r.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
            sFare += kv.Value.Item4[3]; sRealFare += market > 0 ? TheAirline.Helpers.AIPricingService.EconomyFare(r) / market : 1;
            var now = TheAirline.Helpers.RouteForecastService.Forecast(r.Airline, r.Destination1, r.Destination2, f, kv.Value.Item3, r);
            if (n2 <= 15) Console.WriteLine(string.Format("  {0}-{1} {2} trips: load at creation {3:0}%, real {4:0}%, forecast now {5:0}% | profit a day at creation {6:+#,0;-#,0}, real {7:+#,0;-#,0}, forecast now {8:+#,0;-#,0} | one fewer at creation {9:+#,0;-#,0}",
                r.Destination1.Profile.IATACode, r.Destination2.Profile.IATACode, kv.Value.Item3, fc.Load * 100, realLoad * 100, now.Load * 100, fc.ProfitPerDay + own, realProfit, now.ProfitPerDay + own, kv.Value.Item2.ProfitPerDay + own));
        }
        // by the load the forecast gave the route when it was opened: which bands are worth opening at all
        Console.WriteLine("  forecast load at creation -> how they really did");
        var edges = new[] { 0.0, 0.05, 0.10, 0.15, 0.20, 0.30, 0.50, 1.01 };
        for (int b = 0; b < edges.Length - 1; b++)
        {
            var inBand = bands.Where(x => x.Item1 >= edges[b] && x.Item1 < edges[b + 1]).ToList();
            if (inBand.Count == 0) continue;
            Console.WriteLine(string.Format("    forecast {0:0}-{1:0}%: {2,3} routes, real load {3,3:0}%, real profit a day {4,9:+#,0;-#,0}, total a day {5,12:+#,0;-#,0}, {6} still losing",
                edges[b] * 100, edges[b + 1] * 100, inBand.Count, 100 * inBand.Average(x => x.Item2), inBand.Average(x => x.Item3), inBand.Sum(x => x.Item3), inBand.Count(x => x.Item3 < 0)));
        }
        Console.WriteLine(string.Format("  routes forecast under 10% full: {0} of {1}, together {2:+#,0;-#,0} a day",
            bands.Count(x => x.Item1 < 0.10), bands.Count, bands.Where(x => x.Item1 < 0.10).Sum(x => x.Item3)));

        int k = Math.Max(1, n2);
        Console.WriteLine(string.Format("NEWPROBE {0} new routes, {1:MMM yyyy}: load at creation {2:0}% real {3:0}% | tickets a day at creation {4:N0} real {5:N0} | profit a day (before ownership) at creation {6:N0} real {7:N0} | score at creation {8:0.00} real {9:0.00} | reputation share {10:0.00} (predicted with the route {14:0.00}) real {11:0.00} | fare level {12:0.00} real {13:0.00}",
            n2, last, 100 * fl / k, 100 * rl / k, ft / k, rt / k, fp / k, rp / k, sScore / k, sRealScore / k, sRep / k, sRealRep / k, sFare / k, sRealFare / k, sPred / k));
    }

    // the world map follows one aircraft: picked in the list or clicked, kept through refreshes, stopped when it goes
    static void FollowTest(AirlineViewModel av)
    {
        Console.WriteLine("FOLLOW TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var me = g.HumanAirline;
        var t320 = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(x => x.Name == "Airbus A320-200");
        var mine = new System.Collections.Generic.List<TheAirline.Models.Airliners.FleetAirliner>();
        for (int i = 0; i < 3; i++)
        {
            var al = new TheAirline.Models.Airliners.Airliner("fol" + i, t320, "N-FOL" + i, g.GameTime.AddYears(-1)) { Airline = me };
            TheAirline.Models.Airliners.Airliners.AddAirliner(al);
            mine.Add(TheAirline.Helpers.AirlineHelpers.AddAirliner(me, al, me.Airports.First(), false));
        }
        var map = av.Map;
        map.Reload();
        Console.WriteLine("  choices: " + string.Join(", ", map.AircraftChoices.Select(c => c.Name)));
        if (map.AircraftChoices.Count != me.Fleet.Count(f => f.Airliner.BuiltDate <= g.GameTime)) fail("the list isn't the fleet");
        if (map.Follow || map.HasSelection) fail("following before a pick");
        map.Follow = true;
        if (map.Follow) fail("following with nothing selected");

        // a pick selects and follows it
        var pick = map.AircraftChoices.First(c => c.Value == mine[1]);
        map.PickedAircraft = pick;
        if (map.Selected?.Aircraft != mine[1] || !map.Follow) fail("the pick isn't selected and followed");
        Console.WriteLine("  " + map.SelectedText);
        if (!map.SelectedText.StartsWith("Following ")) fail("the text doesn't say it's followed");

        // refreshes and new days keep it
        av.Refresh();
        TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        map.Reload();
        if (map.Selected?.Aircraft != mine[1] || !map.Follow || map.PickedAircraft?.Value != mine[1]) fail("lost after a refresh");

        // clicking another aircraft keeps following, now that one; stopping keeps the selection
        map.Selected = map.Aircraft.First(a => a.Aircraft == mine[2]);
        if (!map.Follow || map.PickedAircraft?.Value != mine[2]) fail("the list doesn't show the clicked aircraft");
        map.Follow = false;
        if (map.Selected?.Aircraft != mine[2]) fail("stopping lost the selection");
        map.Follow = true;

        // the followed aircraft leaves the fleet: no longer followed
        me.RemoveAirliner(mine[2]);
        map.Reload();
        if (map.Follow || map.HasSelection) fail("still following a sold aircraft");
        if (map.AircraftChoices.Any(c => c.Value == mine[2])) fail("a sold aircraft is still in the list");
        Console.WriteLine("  after selling it: " + map.SelectedText);
        Console.WriteLine("FOLLOW TEST OK");
    }

    // the flight board: the followed aircraft's flights today, their status through the day, and what they earned
    static void BoardTest(AirlineViewModel av)
    {
        Console.WriteLine("FLIGHT BOARD TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int i = 0; i < 20; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        // an aircraft of a computer airline flying one route alone, several times today
        var f = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Fleet.ToList())
            .Where(x => x.Routes.Count == 1 && x.Routes[0] is TheAirline.Models.Routes.PassengerRoute && x.Routes[0].GetAirliners().Count == 1
                        && x.Routes[0].TimeTable.Entries.Count(e => e.Airliner == x && e.Day == g.GameTime.DayOfWeek) >= 4)
            .OrderBy(x => x.Name).FirstOrDefault();
        if (f == null) fail("no aircraft flying several times a day");
        var route = f.Routes[0];
        var map = av.Map;
        map.ShowRivals(new TheAirline.Presentation.RivalView { Airline = f.Airliner.Airline });
        map.Reload();
        var shown = map.Aircraft.FirstOrDefault(a => a.Aircraft == f);
        if (shown == null) fail("the aircraft isn't on the map");
        map.Selected = shown;
        var day = g.GameTime.Date;
        int planned = route.TimeTable.Entries.Count(e => e.Airliner == f && e.Day == day.DayOfWeek);

        map.LoadBoard(day.AddMinutes(1));
        Console.WriteLine("  " + map.BoardTitle);
        foreach (var r in map.FlightBoard) Console.WriteLine("    " + r.Departs + " " + r.Flight + " " + r.Route + " -> " + r.Arrives + " | " + r.Status + (r.HasResult ? " | " + r.Result : ""));
        if (map.FlightBoard.Count != planned) fail(map.FlightBoard.Count + " rows for " + planned + " flights");
        if (map.FlightBoard.Any(r => r.IsDone || r.IsAirborne)) fail("flights done before they left");

        // half way through the first flight that wasn't cancelled: in the air with its passengers
        var recs = TheAirline.Helpers.FlightRecords.Of(f).Where(x => x.Takeoff.Date == day || x.Landing.Date == day).ToList();
        var first = recs.FirstOrDefault(x => !x.Cancelled && x.Takeoff.Date == day);
        if (first == null) fail("no flight recorded today");
        map.LoadBoard(first.Takeoff + TimeSpan.FromTicks((first.Landing - first.Takeoff).Ticks / 2));
        var air = map.FlightBoard.FirstOrDefault(r => r.IsAirborne);
        if (air == null || air.Flight != first.FlightCode || !air.Result.Contains("passengers")) fail("the first flight isn't shown in the air");
        Console.WriteLine("  mid-flight: " + air.Departs + " " + air.Flight + " " + air.Status + " " + air.Result);

        // the end of the day: landed with passengers and profit; the summary adds them up
        map.LoadBoard(day.AddHours(23).AddMinutes(59));
        Console.WriteLine("  end of day: " + map.BoardSummary);
        foreach (var r in map.FlightBoard) Console.WriteLine("    " + r.Departs + " " + r.Flight + " | " + r.Status + (r.HasResult ? " | " + r.Result : ""));
        int landedToday = recs.Count(x => !x.Cancelled && x.Landing.Date == day && x.Landing <= day.AddHours(23).AddMinutes(59));
        if (map.FlightBoard.Count(r => r.IsDone && r.HasResult) < Math.Min(landedToday, planned) - 1) fail("landed flights without results");
        if (!map.BoardSummary.Contains("today so far")) fail("no summary of today");

        // the recorded income is what the route was paid (tickets and in-flight sales of today's landings)
        double recorded = recs.Where(x => !x.Cancelled && x.Landing.Date == day).Sum(x => x.Income);
        double paid = route.GetInvoices().MonthlyInvoices.Where(m => m.Year == day.Year && m.Month == day.Month && m.Day == day.Day
                                                                     && (m.Type == TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets || m.Type == TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees)).Sum(m => m.Amount);
        Console.WriteLine(string.Format("  income today: recorded {0:N0}, paid to the route {1:N0}", recorded, paid));
        if (paid > 0 && Math.Abs(recorded - paid) > Math.Max(1, paid * 0.02)) fail("the recorded income isn't what the route was paid");
        Console.WriteLine("FLIGHT BOARD TEST OK");
    }

    // "vm oneway": passenger demand both ways on airport pairs
    static void OneWayProbe()
    {
        var eco = TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass;
        Func<string, TheAirline.Models.Airports.Airport> ap = c => TheAirline.Models.Airports.Airports.GetAirport(c);
        foreach (var pr in new[] { "ATL CLT", "ATL MCO", "ATL DCA", "CLT DCA", "JFK LAX", "ATL ORD" })
        {
            var x = pr.Split(' '); var a = ap(x[0]); var b = ap(x[1]);
            if (a == null || b == null) { Console.WriteLine("  " + pr + ": not in the game"); continue; }
            Console.WriteLine(string.Format("  {0}->{1}: {2} a day, {1}->{0}: {3} a day (statics {4} / {5}) size {6}/{7}", x[0], x[1], a.GetDestinationPassengersRate(b, eco), b.GetDestinationPassengersRate(a, eco),
                a.Statics.GetDestinationPassengersRate(b, eco), b.Statics.GetDestinationPassengersRate(a, eco), a.Profile.Size, b.Profile.Size));
        }
        foreach (var c in new[] { "CLT", "MCO", "DCA", "ORD", "ATL", "JFK" })
        {
            var a = ap(c);
            var md = a.Profile.MajorDestionations;
            Console.WriteLine("  " + c + " major destinations: " + md.Count + " | " + string.Join(", ", md.Take(8).Select(k => k.Key + "=" + k.Value)) + (md.ContainsKey("ATL") ? " | ATL=" + md["ATL"] : " | no ATL"));
        }
        // over all pairs with routes: how many have demand one way only
        var pairs = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(l => l.Routes.ToList()).Where(r => !r.IsCargoRoute).Select(r => Tuple.Create(r.Destination1, r.Destination2)).Distinct().ToList();
        int oneWay = 0, both = 0, none = 0;
        var examples = new System.Collections.Generic.List<string>();
        foreach (var p in pairs)
        {
            int ab = p.Item1.GetDestinationPassengersRate(p.Item2, eco), ba = p.Item2.GetDestinationPassengersRate(p.Item1, eco);
            if (ab > 0 && ba > 0) both++; else if (ab == 0 && ba == 0) none++; else { oneWay++; if (examples.Count < 12) examples.Add(p.Item1.Profile.IATACode + "->" + p.Item2.Profile.IATACode + " " + ab + " / back " + ba + " (" + p.Item1.Profile.Size + ", " + p.Item2.Profile.Size + ", " + TheAirline.Helpers.MathHelpers.GetDistance(p.Item1, p.Item2).ToString("0") + " km)"); }
        }
        Console.WriteLine("ONEWAY " + pairs.Count + " flown pairs: demand both ways " + both + ", one way only " + oneWay + ", none " + none);
        examples.ForEach(e => Console.WriteLine("  " + e));
        // across all airport pairs in the demand data
        long all = 0, missing = 0;
        var bySize = new System.Collections.Generic.Dictionary<string, int>();
        var airports = TheAirline.Models.Airports.Airports.GetAllActiveAirports();
        foreach (var a in airports)
            foreach (var b in airports)
            {
                if (a == b) continue;
                int ab = a.Statics.GetDestinationPassengersRate(b, eco);
                if (ab <= 0) continue;
                all++;
                if (b.Statics.GetDestinationPassengersRate(a, eco) <= 0) { missing++; string k = b.Profile.Size + " to " + a.Profile.Size; int n0; bySize.TryGetValue(k, out n0); bySize[k] = n0 + 1; }
            }
        Console.WriteLine("ONEWAY all " + airports.Count + " airports: " + all + " pairs with demand one way, " + missing + " of them with none back");
        foreach (var kv in bySize.OrderByDescending(x => x.Value).Take(12)) Console.WriteLine("  none from " + kv.Key + ": " + kv.Value);
        // the test: the airlines' airports (made first at the start) get demand from the busier airports too
        var homes = TheAirline.Models.Airlines.Airlines.GetAllAirlines().SelectMany(l => l.Airports.ToList()).Distinct().ToList();
        int broken = 0, checkedPairs = 0;
        var brokenExamples = new System.Collections.Generic.List<string>();
        foreach (var h in homes)
            foreach (var x in airports)
            {
                if (x == h || x.Profile.Size < TheAirline.Helpers.GeneralHelpers.Size.Medium) continue;
                int out1 = h.Statics.GetDestinationPassengersRate(x, eco);
                if (out1 < 100) continue;
                checkedPairs++;
                if (x.Statics.GetDestinationPassengersRate(h, eco) == 0) { broken++; if (brokenExamples.Count < 5) brokenExamples.Add(x.Profile.IATACode + "->" + h.Profile.IATACode); }
            }
        Console.WriteLine("ONEWAY airlines' airports: " + homes.Count + ", " + checkedPairs + " busy pairs checked, " + broken + " with no demand back" + (broken > 0 ? " (" + string.Join(", ", brokenExamples) + ")" : ""));
        if (Environment.GetCommandLineArgs().Contains("onewaytest"))
        {
            if (checkedPairs == 0 || broken > 0) { Console.WriteLine("  FAILED demand missing towards the airlines' airports"); Environment.Exit(1); }
            Console.WriteLine("DEMAND BOTH WAYS TEST OK");
        }
        foreach (var g in airports.GroupBy(a => a.Profile.Size).OrderByDescending(x => (int) x.Key))
        {
            double sum = g.Sum(a => (double) a.Statics.GetDestinationPassengersSum());
            double pax = g.Sum(a => a.Profile.Pax);
            int dests = g.Sum(a => a.GetDestinationsPassengers().Count);
            Console.WriteLine(string.Format("  {0,-10} {1,3} airports: demand out {2,10:N0} a day ({3,7:N0} each), {4,6:N0} destinations each, data passengers {5,8:N0} thousand a year each", g.Key, g.Count(), sum, sum / g.Count(), dests / g.Count(), pax / g.Count()));
        }
    }

    // the rivals' moves and each aircraft's flights come back with a saved game
    static void HistoryTest(AirlineViewModel av)
    {
        Console.WriteLine("HISTORY TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int i = 0; i < 25; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var me = g.HumanAirline;
        // a move of each kind, and the player's airports for the moves list
        var theirs = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman).SelectMany(a => a.Routes.ToList()).OfType<TheAirline.Models.Routes.PassengerRoute>().First(r => r.HasAirliner);
        TheAirline.Helpers.CompetitorService.RecordFare(theirs.Airline, theirs, 0.9);
        TheAirline.Helpers.CompetitorService.RecordDropped(theirs.Airline, theirs);
        if (!me.Airports.Contains(theirs.Destination1)) me.AddAirport(theirs.Destination1);
        Func<string> movesKey = () => string.Join(";", TheAirline.Helpers.CompetitorService.ToSave().Select(m => m.Date.ToString("s") + m.Airline.Profile.IATACode + m.A + m.B + m.Kind + m.RouteId + m.Factor.ToString("0.000")));
        Func<string> flightsKey = () => string.Join(";", TheAirline.Helpers.FlightRecords.ToSave(TheAirline.Models.Airlines.Airlines.GetAllAirlines())
            .OrderBy(x => x.Airliner.Airliner.Airline.Profile.IATACode + x.Airliner.Name)
            .Select(x => x.Airliner.Airliner.Airline.Profile.IATACode + x.Airliner.Name + ":" + x.Flights.Count + ":" + x.Flights.Sum(f => f.Passengers) + ":" + x.Flights.Sum(f => f.Profit).ToString("0")));
        int moves = TheAirline.Helpers.CompetitorService.Remembered;
        string mk = movesKey(), fk = flightsKey();
        int aircraftWithFlights = TheAirline.Helpers.FlightRecords.ToSave(TheAirline.Models.Airlines.Airlines.GetAllAirlines()).Count;
        Console.WriteLine("  before saving: " + moves + " rival moves, " + aircraftWithFlights + " aircraft with recorded flights");
        if (moves < 3 || aircraftWithFlights == 0) fail("nothing to save");

        // a flight board to compare: an aircraft of the rival that flies today
        var f0 = theirs.Airline.Fleet.ToList().First(x => TheAirline.Helpers.FlightRecords.Of(x).Count > 0 && x.Routes.Count > 0);
        string code = f0.Airliner.Airline.Profile.IATACode, name = f0.Name;
        av.Map.ShowRivals(new TheAirline.Presentation.RivalView { Airline = f0.Airliner.Airline });
        av.Map.Reload();
        av.Map.Selected = av.Map.Aircraft.First(a => a.Aircraft == f0);
        var t = g.GameTime.Date.AddHours(23);
        av.Map.LoadBoard(t);
        string board = string.Join(";", av.Map.FlightBoard.Select(r => r.Key)) + "|" + av.Map.BoardSummary;
        string movesShown = string.Join(";", TheAirline.Helpers.CompetitorService.Moves(me, 30).Select(m => m.Date.ToString("s") + m.Kind + m.RouteId));

        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("historytest");
        TheAirline.Helpers.CompetitorService.Clear();
        TheAirline.Helpers.FlightRecords.Clear();
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("historytest");
        g = TheAirline.Models.General.GameObject.GetInstance();
        me = g.HumanAirline;

        Console.WriteLine("  after loading: " + TheAirline.Helpers.CompetitorService.Remembered + " rival moves, " + TheAirline.Helpers.FlightRecords.ToSave(TheAirline.Models.Airlines.Airlines.GetAllAirlines()).Count + " aircraft with recorded flights");
        if (movesKey() != mk) fail("the rival moves changed");
        if (flightsKey() != fk) fail("the recorded flights changed");
        if (string.Join(";", TheAirline.Helpers.CompetitorService.Moves(me, 30).Select(m => m.Date.ToString("s") + m.Kind + m.RouteId)) != movesShown) fail("the moves at my airports changed");

        // the same aircraft's flight board, with its flights matched to the timetable again
        var airline = TheAirline.Models.Airlines.Airlines.GetAllAirlines().First(a => a.Profile.IATACode == code);
        var f1 = airline.Fleet.ToList().First(x => x.Name == name);
        var map = av.Map;
        map.ShowRivals(new TheAirline.Presentation.RivalView { Airline = airline });
        map.Reload();
        map.Selected = map.Aircraft.First(a => a.Aircraft == f1);
        map.LoadBoard(t);
        string board2 = string.Join(";", map.FlightBoard.Select(r => r.Key)) + "|" + map.BoardSummary;
        Console.WriteLine("  " + map.BoardTitle + " | " + map.BoardSummary);
        if (board2 != board) { Console.WriteLine("    before: " + board); Console.WriteLine("    after:  " + board2); fail("the flight board changed"); }
        if (!map.FlightBoard.Any(r => r.IsDone && r.HasResult)) fail("no results on the board after loading");

        // the game plays on and keeps recording
        int n0 = TheAirline.Helpers.FlightRecords.Of(f1).Count;
        TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  a day later: " + TheAirline.Helpers.FlightRecords.Of(f1).Count + " flights recorded for " + name + " (was " + n0 + ")");
        Console.WriteLine("HISTORY TEST OK");
    }

    // "vm demandaudit": every airport pair's demand against the data files' figures, the gaps and outliers, and how
    // the demand changes with a save and load and with a new year
    static System.Collections.Generic.Dictionary<long, int> DemandSnapshot(System.Collections.Generic.List<TheAirline.Models.Airports.Airport> airports)
    {
        var eco = TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass;
        var d = new System.Collections.Generic.Dictionary<long, int>();
        for (int i = 0; i < airports.Count; i++)
            for (int j = 0; j < airports.Count; j++)
            {
                if (i == j) continue;
                int r = airports[i].Statics.GetDestinationPassengersRate(airports[j], eco);
                if (r > 0) d[(long) i * 100000 + j] = r;
            }
        return d;
    }

    static string CompareDemand(System.Collections.Generic.Dictionary<long, int> a, System.Collections.Generic.Dictionary<long, int> b, out double totalRatio, out int changed)
    {
        long ta = a.Values.Sum(x => (long) x), tb = b.Values.Sum(x => (long) x);
        totalRatio = ta > 0 ? (double) tb / ta : 0;
        changed = 0; int gone = 0, came = 0; double rel = 0; int n = 0;
        foreach (var kv in a)
        {
            int v; if (!b.TryGetValue(kv.Key, out v)) { gone++; continue; }
            if (v != kv.Value) changed++;
            rel += Math.Abs(v - kv.Value) / (double) kv.Value; n++;
        }
        came = b.Keys.Count(k => !a.ContainsKey(k));
        return string.Format("total {0:N0} -> {1:N0} a day ({2:+0.0;-0.0}%), {3:N0} of {4:N0} pairs changed (on average {5:0.0}%), {6} gone, {7} new", ta, tb, (totalRatio - 1) * 100, changed, a.Count, n > 0 ? 100 * rel / n : 0, gone, came);
    }

    static int auditDataProblems;

    static void DemandAudit(AirlineViewModel av)
    {
        Console.WriteLine("DEMAND AUDIT");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var eco = TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass;
        var airports = TheAirline.Models.Airports.Airports.GetAllActiveAirports().OrderBy(a => a.Profile.IATACode).ToList();
        double level = g.Difficulty.PassengersLevel;
        var activeSet = new System.Collections.Generic.HashSet<TheAirline.Models.Airports.Airport>(airports);
        double yearFactor = 0.15 * ((g.GameTime.Year - TheAirline.Models.General.GameObject.StartYear) / 10.0 + 1);
        Console.WriteLine("  " + airports.Count + " airports, " + g.GameTime.Year + ", passengers level " + level + ", year factor " + yearFactor.ToString("0.000"));

        // 1. the pairs with a figure in the data files
        int dataPairs = 0, exact = 0, missing = 0, off = 0, overflow = 0, selfOrNear = 0;
        var offExamples = new System.Collections.Generic.List<string>();
        var missingExamples = new System.Collections.Generic.List<string>();
        foreach (var a in airports)
            foreach (var kv in a.Profile.MajorDestionations)
            {
                var b = TheAirline.Models.Airports.Airports.GetAirport(kv.Key);
                if (b == null || !activeSet.Contains(b)) continue;
                if (b == a || b.Profile.Town == a.Profile.Town || TheAirline.Helpers.MathHelpers.GetDistance(a, b) <= 50) { selfOrNear++; continue; }
                dataPairs++;
                double expected = kv.Value * 1000.0 / 365 * level * yearFactor;
                int actual = a.Statics.GetDestinationPassengersRate(b, eco);
                if (expected > 65535) overflow++;
                if (actual == 0 && expected >= 1) { missing++; if (missingExamples.Count < 6) missingExamples.Add(a.Profile.IATACode + "->" + b.Profile.IATACode + " data " + kv.Value + " expected " + expected.ToString("0")); }
                else if (Math.Abs(actual - Math.Min(65535, Math.Floor(expected))) <= 1) exact++;
                else { off++; if (offExamples.Count < 6) offExamples.Add(a.Profile.IATACode + "->" + b.Profile.IATACode + " data " + kv.Value + " expected " + expected.ToString("0") + " got " + actual); }
            }
        auditDataProblems = missing + off + overflow;
        Console.WriteLine(string.Format("AUDIT data pairs: {0:N0} (and {1} to the same town, nearby or itself) | as the data says: {2:N0} | missing: {3} | different: {4} | above the 65,535 a day limit: {5}", dataPairs, selfOrNear, exact, missing, off, overflow));
        missingExamples.ForEach(x => Console.WriteLine("    missing " + x));
        offExamples.ForEach(x => Console.WriteLine("    different " + x));

        // 2. the data's own symmetry: A->B against B->A where both are listed
        int both = 0, lopsided = 0;
        foreach (var a in airports)
            foreach (var kv in a.Profile.MajorDestionations)
            {
                var b = TheAirline.Models.Airports.Airports.GetAirport(kv.Key);
                int back;
                if (b == null || b == a || string.CompareOrdinal(a.Profile.IATACode, b.Profile.IATACode) > 0 || !b.Profile.MajorDestionations.TryGetValue(a.Profile.IATACode, out back)) continue;
                both++;
                if (Math.Max(kv.Value, back) > 3 * Math.Max(1, Math.Min(kv.Value, back)) && Math.Max(kv.Value, back) > 50) lopsided++;
            }
        Console.WriteLine("AUDIT data listed both ways: " + both + " pairs, " + lopsided + " more than three times bigger one way");

        // 3. every pair: none, and outliers against the origin's size
        var snap = DemandSnapshot(airports);
        long total = snap.Values.Sum(x => (long) x);
        var origins = airports.Select((a, i) => new { a, i, sum = snap.Where(kv => kv.Key / 100000 == i).Sum(kv => (long) kv.Value), n = snap.Count(kv => kv.Key / 100000 == i) }).ToList();
        var none = origins.Where(o => o.n == 0).ToList();
        Console.WriteLine(string.Format("AUDIT all pairs: {0:N0} with demand, {1:N0} a day in all; airports with no demand at all: {2}{3}", snap.Count, total, none.Count, none.Count > 0 ? " (" + string.Join(", ", none.Take(8).Select(o => o.a.Profile.IATACode)) + ")" : ""));
        // demand per data passenger by airport: the far outliers
        var ratios = origins.Where(o => o.a.Profile.Pax > 0 && o.sum > 0).Select(o => new { o.a, r = o.sum / o.a.Profile.Pax }).OrderBy(x => x.r).ToList();
        if (ratios.Count > 0)
        {
            double median = ratios[ratios.Count / 2].r;
            var low = ratios.Where(x => x.r < median / 4).ToList();
            var high = ratios.Where(x => x.r > median * 4).ToList();
            Console.WriteLine(string.Format("AUDIT demand per thousand yearly passengers: median {0:0.00} a day; below a quarter of it: {1}{2}; above four times: {3}{4}", median, low.Count,
                low.Count > 0 ? " (" + string.Join(", ", low.Take(6).Select(x => x.a.Profile.IATACode + " " + x.r.ToString("0.00"))) + ")" : "", high.Count,
                high.Count > 0 ? " (" + string.Join(", ", high.Take(6).Select(x => x.a.Profile.IATACode + " " + x.r.ToString("0.00"))) + ")" : ""));
        }
        var noPax = airports.Count(a => a.Profile.Pax <= 0);
        Console.WriteLine("AUDIT airports without a passenger figure in the data: " + noPax);

        // 4. a save and a load (and the extra demand from events, which the save keeps)
        Func<System.Collections.Generic.List<TheAirline.Models.Airports.Airport>, string> extras = list => string.Join(";", list.Select(a => a.Profile.IATACode + "=" + (a.GetDestinationPassengersSum() - a.Statics.GetDestinationPassengersSum())).Where(x => !x.EndsWith("=0")));
        var someone = airports.First(a => a.Profile.Size >= TheAirline.Helpers.GeneralHelpers.Size.Large);
        var target = airports.First(a => a != someone && a.Profile.Size >= TheAirline.Helpers.GeneralHelpers.Size.Large);
        someone.AddDestinationPassengersRate(target, 77);
        string extraBefore = extras(airports);
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("demandaudit");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("demandaudit");
        var airports2 = TheAirline.Models.Airports.Airports.GetAllActiveAirports().OrderBy(a => a.Profile.IATACode).ToList();
        double ratio; int changed;
        var snap2 = DemandSnapshot(airports2);
        Console.WriteLine("AUDIT save and load: " + CompareDemand(snap, snap2, out ratio, out changed));
        int loadChanged = changed + snap.Keys.Count(k => !snap2.ContainsKey(k)) + snap2.Keys.Count(k => !snap.ContainsKey(k));
        string extraAfter = extras(airports2);
        Console.WriteLine("AUDIT extra demand from events: before saving " + extraBefore + " | after loading " + extraAfter);
        {
            var byOrigin = snap.Where(kv => { int v; return !snap2.TryGetValue(kv.Key, out v) || v != kv.Value; }).GroupBy(kv => airports[(int) (kv.Key / 100000)].Profile.IATACode).OrderByDescending(x => x.Count()).Take(8);
            Console.WriteLine("    changed by origin: " + string.Join(", ", byOrigin.Select(x => x.Key + " " + x.Count())));
            var ex = snap.Where(kv => { int v; return snap2.TryGetValue(kv.Key, out v) && v != kv.Value; }).Take(5).Select(kv => airports[(int) (kv.Key / 100000)].Profile.IATACode + "->" + airports[(int) (kv.Key % 100000)].Profile.IATACode + " " + kv.Value + " -> " + snap2[kv.Key]);
            Console.WriteLine("    e.g. " + string.Join(", ", ex));
        }

        // 5. a new year
        g = TheAirline.Models.General.GameObject.GetInstance();
        while (!(g.GameTime.Month == 12 && g.GameTime.Day == 31)) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var before = DemandSnapshot(airports2);
        var swYear = System.Diagnostics.Stopwatch.StartNew();
        TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("AUDIT the new year's day took " + swYear.ElapsedMilliseconds + " ms (the demand made again)");
        var after = DemandSnapshot(airports2);
        Console.WriteLine("AUDIT new year " + g.GameTime.Year + ": " + CompareDemand(before, after, out ratio, out changed));
        double yearRatio = ratio;
        // and a load after it: does the year's growth survive?
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("demandaudit");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("demandaudit");
        var airports3 = TheAirline.Models.Airports.Airports.GetAllActiveAirports().OrderBy(a => a.Profile.IATACode).ToList();
        var snap3 = DemandSnapshot(airports3);
        Console.WriteLine("AUDIT save and load after the new year: " + CompareDemand(after, snap3, out ratio, out changed));
        int afterYear = changed + after.Keys.Count(k => !snap3.ContainsKey(k)) + snap3.Keys.Count(k => !after.ContainsKey(k));
        foreach (var k in snap3.Keys.Where(k => !after.ContainsKey(k)).Take(8))
        {
            var o = airports3[(int) (k / 100000)]; var d = airports3[(int) (k % 100000)];
            Console.WriteLine("    new after the load: " + o.Profile.IATACode + " (" + o.Profile.Size + ", " + o.Profile.MajorDestionations.Count + " in the data) -> " + d.Profile.IATACode + " (" + d.Profile.Size + ") " + snap3[k] + " a day; data " + (o.Profile.MajorDestionations.ContainsKey(d.Profile.IATACode) ? o.Profile.MajorDestionations[d.Profile.IATACode].ToString() : "none") + "; airport list same: " + (airports3.Count == airports2.Count));
        }

        if (Environment.GetCommandLineArgs().Contains("demandaudit"))
        {
            if (auditDataProblems > 0) fail(auditDataProblems + " pairs aren't what the data files say");
            if (none.Count > 0) fail(none.Count + " airports without any demand");
            if (loadChanged > 0) fail("a save and load changed " + loadChanged + " pairs");
            if (extraAfter != extraBefore) fail("the extra demand from events didn't come back with the saved game");
            if (yearRatio < 1.005 || yearRatio > 1.03) fail("the new year changed the demand by " + ((yearRatio - 1) * 100).ToString("0.0") + "%");
            if (afterYear > after.Count / 1000) fail("a save and load after the new year changed " + afterYear + " pairs");
            Console.WriteLine("DEMAND AUDIT TEST OK");
        }
    }

    // a smaller save shows the same: past months' daily amounts put together, the empty airport facilities left out,
    // and the old share objects gone
    static void SaveSizeTest()
    {
        Console.WriteLine("SAVE SIZE TEST");
        Action<string> fail = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        for (int i = 0; i < 40; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        // to the last day of a month: the figures before the month update puts the past days together
        while (g.GameTime.AddDays(1).Month == g.GameTime.Month) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var now = g.GameTime;
        var types = (TheAirline.Models.General.Finances.Invoice.InvoiceType[]) Enum.GetValues(typeof(TheAirline.Models.General.Finances.Invoice.InvoiceType));
        Func<string> money = () =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().OrderBy(a => a.Profile.IATACode))
            {
                for (int m = 0; m < 4; m++)
                {
                    var d = now.AddMonths(-m);
                    foreach (var t in types) sb.Append(a.GetInvoicesAmountMonth(d.Year, d.Month, t).ToString("0.00")).Append(',');
                }
                sb.Append(a.GetInvoicesAmount(now.AddDays(-60), now, TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets).ToString("0.00")).Append('|');
                foreach (var r in a.Routes.ToList().OrderBy(r => r.Id))
                {
                    sb.Append(r.GetBalance(now.AddDays(-60), now).ToString("0.00")).Append(',');
                    sb.Append(TheAirline.Helpers.RouteProfitService.Month(r, now.AddMonths(-1).Year, now.AddMonths(-1).Month).ToString("0.00")).Append(',');
                    sb.Append(r.GetRouteInvoiceAmount(TheAirline.Models.General.Finances.Invoice.InvoiceType.Total).ToString("0.00")).Append(';');
                }
            }
            return sb.ToString();
        };
        int before = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Sum(a => a.Invoices.MonthlyInvoices.Count + a.Routes.ToList().Sum(r => r.GetInvoices().MonthlyInvoices.Count));
        string m0 = money();
        // put together as on the first of the next month (this month too), without playing the day
        int removed = 0;
        var next = now.AddDays(1);
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines())
        {
            removed += a.Invoices.CompactBefore(next.Year, next.Month);
            foreach (var r in a.Routes.ToList()) removed += r.GetInvoices().CompactBefore(next.Year, next.Month);
        }
        int after = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Sum(a => a.Invoices.MonthlyInvoices.Count + a.Routes.ToList().Sum(r => r.GetInvoices().MonthlyInvoices.Count));
        Console.WriteLine("  daily amounts: " + before + " -> " + after + " (" + removed + " put together)");
        if (removed == 0) fail("nothing was put together");
        if (money() != m0) fail("a figure changed when the past months were put together");
        // a payment after it goes to today's amount as before
        var aa = TheAirline.Models.Airlines.Airlines.GetAllAirlines().First();
        double cash = aa.Money;
        TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(aa, now, TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees, 123);
        if (Math.Abs(aa.GetInvoicesAmountMonth(now.Year, now.Month, TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees) - aa.GetInvoicesAmountMonth(now.Year, now.Month, TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees)) > 0 || Math.Abs(aa.Money - cash - 123) > 0.001) fail("a payment after it");
        TheAirline.Helpers.AirlineHelpers.AddAirlineInvoice(aa, now, TheAirline.Models.General.Finances.Invoice.InvoiceType.Fees, -123);

        // the empty facilities: the same reputation and the same facility everywhere
        var airports = TheAirline.Models.Airports.Airports.GetAllAirports();
        var airlines = TheAirline.Models.Airlines.Airlines.GetAllAirlines();
        var ftypes = (TheAirline.Models.Airports.AirportFacility.FacilityType[]) Enum.GetValues(typeof(TheAirline.Models.Airports.AirportFacility.FacilityType));
        Func<string> facilities = () => string.Join(";", airports.Select(ap => ap.Profile.IATACode + ":" + string.Join(",", airlines.Select(a => ap.GetAirlineReputation(a).ToString("0.000") + "/" + string.Join(".", ftypes.Select(t => { var f = ap.GetAirlineAirportFacility(a, t); return f?.Facility == null ? "-" : f.Facility.TypeLevel.ToString(); }))))));
        string f0 = facilities(); // (asking makes the missing ones, so ask once first)
        f0 = facilities();
        int pruned = airports.Sum(ap => ap.PruneNoneFacilities());
        string f1 = facilities();
        Console.WriteLine("  empty facilities left out: " + pruned);
        if (pruned == 0) fail("no empty facility to leave out");
        if (f1 != f0)
        {
            var x0 = f0.Split(';'); var x1 = f1.Split(';');
            for (int k = 0; k < x0.Length; k++) if (x0[k] != x1[k]) { Console.WriteLine("    before " + x0[k].Substring(0, Math.Min(200, x0[k].Length))); Console.WriteLine("    after  " + x1[k].Substring(0, Math.Min(200, x1[k].Length))); break; }
            var none0 = TheAirline.Models.Airports.AirportFacilities.GetFacilities().Where(f => f.TypeLevel == 0).Select(f => f.Type + " service " + f.ServiceLevel);
            Console.WriteLine("    level 0 facilities: " + string.Join(", ", none0));
            fail("a reputation or facility changed without the empty facilities");
        }

        // save and load: the shares' owners, the figures
        var holdings = string.Join(";", airlines.OrderBy(a => a.Profile.IATACode).Select(a => a.Profile.IATACode + ":" + string.Join(",", a.Holdings.Select(h => (h.Holder == null ? "public" : h.Holder.Profile.IATACode) + "=" + h.Count))));
        string mBefore = money();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("savesizetest");
        long ms = sw.ElapsedMilliseconds;
        long size = new System.IO.FileInfo(TheAirline.Infrastructure.AppSettings.GetCommonApplicationDataPath() + "/saves/savesizetest.sav").Length;
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("savesizetest");
        g = TheAirline.Models.General.GameObject.GetInstance();
        airlines = TheAirline.Models.Airlines.Airlines.GetAllAirlines();
        var holdings2 = string.Join(";", airlines.OrderBy(a => a.Profile.IATACode).Select(a => a.Profile.IATACode + ":" + string.Join(",", a.Holdings.Select(h => (h.Holder == null ? "public" : h.Holder.Profile.IATACode) + "=" + h.Count))));
        Console.WriteLine("  saved in " + ms + " ms, " + (size / 1024) + " KB; old share objects after loading: " + airlines.Sum(a => a.Shares.Count));
        if (holdings2 != holdings) fail("who owns the shares changed");
        if (airlines.Sum(a => a.Shares.Count) != 0) fail("old share objects are back");
        if (money() != mBefore) fail("a figure changed with the save and load");
        Console.WriteLine("SAVE SIZE TEST OK");
    }

    // the computer airlines plan with the route forecast: destinations, and losing routes given up
    static void AiPlanTest()
    {
        Console.WriteLine("AI PLAN TEST");
        Action<string> fail0 = m => { Console.WriteLine("  FAILED " + m); Environment.Exit(1); };
        var g = TheAirline.Models.General.GameObject.GetInstance();
        // the computer airlines open routes in the first weeks
        for (int i = 0; i < 40; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var ai = TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(a => !a.IsHuman && a.AirlineRouteFocus == TheAirline.Models.Routes.Route.RouteType.Passenger).ToList();
        var routes = ai.SelectMany(a => a.Routes).Where(r => r is TheAirline.Models.Routes.PassengerRoute && r.GetAirliners().Count == 1 && r.TimeTable.Entries.Count > 0).ToList();
        Console.WriteLine("  " + ai.Count + " passenger airlines, " + routes.Count + " routes with one aircraft, " + TheAirline.Helpers.AIRouteChoice.Picked + " destinations forecast so far");
        if (routes.Count == 0) { Console.WriteLine("  FAILED no computer routes"); Environment.Exit(1); }
        if (TheAirline.Helpers.AIRouteChoice.Picked == 0) { Console.WriteLine("  FAILED the computer airlines opened routes without a forecast"); Environment.Exit(1); }

        // 1. a destination picked by forecast, from the ones with the most demand: the one forecast to earn the most
        bool picked = false;
        foreach (var airline in ai)
        {
            foreach (var origin in airline.Airports.Take(3))
            {
                var ranked = TheAirline.Helpers.AIHelpers.GetDestinationAirports(airline, origin);
                if (ranked.Count < 2) continue;
                int before = TheAirline.Helpers.AIRouteChoice.Picked;
                var d = TheAirline.Helpers.AIRouteChoice.PickDestination(airline, origin, ranked);
                if (TheAirline.Helpers.AIRouteChoice.Picked == before) continue;
                double fares = TheAirline.Helpers.AIRouteChoice.FareFactor(airline);
                Func<TheAirline.Models.Airports.Airport, double> earns = x =>
                {
                    var f = TheAirline.Helpers.AIRouteChoice.Reference(airline, TheAirline.Helpers.MathHelpers.GetDistance(origin, x));
                    return f == null ? double.MinValue : TheAirline.Helpers.AIRouteChoice.Best(airline, origin, x, f, null, false, fares).ProfitPerDay;
                };
                var top = ranked.Take(TheAirline.Helpers.AIRouteChoice.Candidates).ToList();
                Console.WriteLine("  " + airline.Profile.Name + " from " + origin.Profile.IATACode + " (fares " + fares.ToString("0.00") + " of the market): " +
                                  string.Join(", ", top.Select(x => x.Profile.IATACode + " " + earns(x).ToString("N0"))) + " a day -> " + d.Profile.IATACode);
                if (!top.Contains(d)) { Console.WriteLine("  FAILED picked outside the candidates"); Environment.Exit(1); }
                if (top.Any(x => earns(x) > earns(d) + 1)) { Console.WriteLine("  FAILED a destination forecast to earn more was passed over"); Environment.Exit(1); }
                picked = true;
                break;
            }
            if (picked) break;
        }
        if (!picked) { Console.WriteLine("  FAILED no destination forecast"); Environment.Exit(1); }

        // 2. a route that loses money with empty seats: given up only after two losing months, when the forecast says
        //    it won't pay its flights; a full one is kept
        var test = routes.First();
        var tf = test.GetAirliners()[0];
        var oldStart = test.StartDate;
        bool young = TheAirline.Helpers.AIRouteChoice.ShouldDrop(test.Airline, test, -50000);
        test.StartDate = g.GameTime.AddDays(-90);
        bool old = TheAirline.Helpers.AIRouteChoice.ShouldDrop(test.Airline, test, -50000);
        var best = TheAirline.Helpers.AIRouteChoice.Best(test.Airline, test.Destination1, test.Destination2, tf, test, TheAirline.Helpers.AIRouteChoice.IsBusiness(test, tf));
        double flying = best.ProfitPerDay + TheAirline.Helpers.OwnershipService.MonthlyCost(tf)/30.4;
        bool expect = test.FillingDegree < 0.5 && test.GetBalance(g.GameTime.AddDays(-60), g.GameTime) < 0 && flying < 0;
        Console.WriteLine(string.Format("  {0}-{1}: {2:0}% full, last 60 days {3:N0}, forecast {4:N0} a day before ownership -> {5}",
            test.Destination1.Profile.IATACode, test.Destination2.Profile.IATACode, test.FillingDegree*100, test.GetBalance(g.GameTime.AddDays(-60), g.GameTime), flying, old ? "given up" : "kept"));
        test.StartDate = oldStart;
        if (young && (g.GameTime - oldStart).TotalDays < 60) { Console.WriteLine("  FAILED a new route given up"); Environment.Exit(1); }
        if (old != expect) { Console.WriteLine("  FAILED expected " + (expect ? "given up" : "kept")); Environment.Exit(1); }
        if (TheAirline.Helpers.AIRouteChoice.ShouldDrop(test.Airline, test, 5000)) { Console.WriteLine("  FAILED a route earning money given up"); Environment.Exit(1); }

        // 3. idle aircraft: more route searches a day the more there are, and those idle four months are sold
        if (TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(0) != 0 || TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(1) != 1 || TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(80) != 3 || TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(600) != 4)
            fail0("searches a day: " + TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(1) + "/" + TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(80) + "/" + TheAirline.Helpers.AIHelpers.IdleSearchesPerDay(600));
        Console.WriteLine("  " + TheAirline.Helpers.AIRouteChoice.IdleRoutes + " routes opened for idle aircraft so far");
        if (TheAirline.Helpers.AIRouteChoice.IdleRoutes == 0) fail0("no route for an idle aircraft");
        {
            var seller = ai.Where(a => a.Airports.Count > 0).OrderBy(a => a.Fleet.Count).First();
            var t320 = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(x => x.Name == "Airbus A320-200");
            var spare = new System.Collections.Generic.List<TheAirline.Models.Airliners.Airliner>();
            int extra = TheAirline.Helpers.OwnershipService.Reserve(seller) + 6;
            for (int i = 0; i < extra; i++)
            {
                var al = new TheAirline.Models.Airliners.Airliner("idle" + i, t320, "N-IDL" + i, g.GameTime.AddYears(-i - 1)) { Airline = seller };
                TheAirline.Models.Airliners.Airliners.AddAirliner(al);
                TheAirline.Helpers.AirlineHelpers.AddAirliner(seller, al, seller.Airports.First(), false);
                spare.Add(al);
            }
            var t1 = g.GameTime;
            Func<int> left = () => seller.Fleet.Count(f => spare.Contains(f.Airliner));
            TheAirline.Helpers.OwnershipService.SellSurplus(seller); // starts counting (and may make the weekly sale)
            int afterFirst = left();
            int long0 = TheAirline.Helpers.OwnershipService.LongIdleSold;
            g.GameTime = t1.AddDays(TheAirline.Helpers.OwnershipService.LongIdleDays - 1);
            TheAirline.Helpers.OwnershipService.SellSurplus(seller);
            int beforeLong = TheAirline.Helpers.OwnershipService.LongIdleSold - long0;
            g.GameTime = t1.AddDays(TheAirline.Helpers.OwnershipService.LongIdleDays + 1);
            TheAirline.Helpers.OwnershipService.SellSurplus(seller);
            int longSold = TheAirline.Helpers.OwnershipService.LongIdleSold - long0 - beforeLong;
            g.GameTime = t1;
            Console.WriteLine("  " + seller.Profile.Name + ": " + extra + " spare aircraft, " + afterFirst + " left after the first day; after four months idle " + longSold + " sold at once (" + beforeLong + " a day earlier), " + left() + " left");
            if (beforeLong != 0) fail0("sold as idle too long before four months");
            if (longSold != TheAirline.Helpers.OwnershipService.LongIdleSalesPerDay) fail0("long idle sale " + longSold);
        }

        // 4. the game plays on with the computer airlines deciding this way
        for (int i = 0; i < 45; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  after 45 more days: " + TheAirline.Helpers.AIRouteChoice.Picked + " destinations forecast (" + TheAirline.Helpers.AIRouteChoice.PickedOther + " not the one with the most demand), " + TheAirline.Helpers.AIRouteChoice.Dropped + " routes given up");
        Console.WriteLine("AI PLAN TEST OK");
    }

    // "vm aicheck": the route forecast against what the computer airlines' routes really did last month
    static void AiCheckProbe()
    {
        var g = TheAirline.Models.General.GameObject.GetInstance();
        int months = int.Parse(Environment.GetEnvironmentVariable("TAP_MONTHS") ?? "3");
        for (int m = 0; m < months; m++) { int sm = g.GameTime.Month; while (g.GameTime.Month == sm) TheAirline.Helpers.GameObjectHelpers.SimulateTurn(); }
        var last = g.GameTime.AddMonths(-1);
        var first = new DateTime(last.Year, last.Month, 1);
        int days = DateTime.DaysInMonth(last.Year, last.Month);
        double fl = 0, al = 0, fp = 0, ap = 0, fr = 0, ar = 0; int n = 0;
        var rows = new System.Collections.Generic.List<string>();
        foreach (var a in TheAirline.Models.Airlines.Airlines.GetAllAirlines().Where(x => !x.IsHuman))
        foreach (var r in a.Routes.OfType<TheAirline.Models.Routes.PassengerRoute>().Where(r => r.StartDate < first && r.GetAirliners().Count == 1 && r.TimeTable.Entries.Count > 0).ToList())
        {
            var f = r.GetAirliners()[0];
            int trips = TheAirline.Helpers.AIRouteChoice.CurrentTrips(r);
            if (trips < 1) continue;
            var fc = TheAirline.Helpers.RouteForecastService.Forecast(a, r.Destination1, r.Destination2, f, trips, r);
            double load = TheAirline.Helpers.ConnectionService.MonthLoad(r);
            double profit = TheAirline.Helpers.RouteProfitService.Month(r, last.Year, last.Month);
            double income = TheAirline.Helpers.RouteProfitService.Month(r, TheAirline.Models.General.Finances.Invoice.InvoiceType.Tickets, last.Year, last.Month);
            double flights = r.TimeTable.Entries.Count*days/7.0;
            double fcProfit = fc.ProfitPerFlight*flights - TheAirline.Helpers.OwnershipService.MonthlyCost(f)*0; // ownership is in the forecast; RouteProfit has none
            fl += fc.Load; al += load; fp += fc.ProfitPerFlight*flights + TheAirline.Helpers.OwnershipService.MonthlyCost(f); ap += profit; fr += fc.RevenuePerFlight*flights; ar += income; n++;
            if (rows.Count < 12) rows.Add(string.Format("  {0,-20} {1}-{2} {3} trips: load forecast {4:0}% real {5:0}% | tickets forecast {6:N0} real {7:N0} | profit before ownership forecast {8:N0} real {9:N0}",
                a.Profile.Name, r.Destination1.Profile.IATACode, r.Destination2.Profile.IATACode, trips, fc.Load*100, load*100, fc.RevenuePerFlight*flights, income, fc.ProfitPerFlight*flights + TheAirline.Helpers.OwnershipService.MonthlyCost(f), profit));
        }
        rows.ForEach(Console.WriteLine);
        Console.WriteLine(string.Format("AICHECK {0} routes, {1:MMM yyyy}: load forecast {2:0}% real {3:0}% | tickets forecast {4:N0} real {5:N0} | profit before ownership forecast {6:N0} real {7:N0}", n, last, 100*fl/n, 100*al/n, fr, ar, fp, ap));
    }

    static async Task ForecastTest(AirlineViewModel av)
    {
        Console.WriteLine("FORECAST TEST");
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var mk = av.Market;
        var ap = av.Airports;
        var made = new System.Collections.Generic.List<Tuple<TheAirline.Models.Routes.Route, TheAirline.Helpers.RouteForecast, bool>>();
        // two aircraft of one type with at least 15 seats, the cheapest such type with two for sale
        var forSale = mk.Aircraft.Where(x => x.Airliner.Type is TheAirline.Models.Airliners.AirlinerPassengerType && x.Airliner.GetTotalSeatCapacity() >= 15 && x.Airliner.Range >= 1000)
                        .GroupBy(x => x.Airliner.Type).Where(gr => gr.Count() >= 2).OrderBy(gr => gr.Min(x => x.Airliner.GetPrice())).First().OrderBy(x => x.Airliner.GetPrice()).ToList();
        for (int n = 0; n < 2; n++)
        {
            mk.Selected = mk.Aircraft.First(x => x.Airliner == forSale[n].Airliner);
            mk.BuyCommand.Execute(null);
            av.Refresh();
            ap.ReloadAdvice();
            await Wait(() => !ap.IsBusy, "advice");
            var advice = ap.Advice.Where(x => x.Advice.FleetCanFly).FirstOrDefault(x => !h.Routes.Any(r => r.Destination1 == x.Advice.Destination || r.Destination2 == x.Advice.Destination));
            if (advice == null) break;
            ap.SelectedAdvice = advice;
            var cr = ap.Create;
            int max = cr.Trips.Count == 0 ? 0 : cr.Trips.Max();
            Console.WriteLine("  " + cr.Title + " with " + cr.SelectedAircraft?.Name + ": trips 1-" + max + ", suggested " + cr.RoundTrips);
            Console.WriteLine("    " + cr.Forecast);
            bool atSuggestion = n == 0;
            if (!atSuggestion && max > 0) { cr.RoundTrips = max; Console.WriteLine("    at the most: " + cr.Forecast); }
            if (string.IsNullOrEmpty(cr.Forecast) || cr.CurrentForecast == null) { Console.WriteLine("  FAILED no forecast"); Environment.Exit(1); }
            var aircraft = cr.SelectedAircraft.Airliner;
            var fc = cr.CurrentForecast;
            foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
            cr.CreateCommand.Execute(null);
            foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
            if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
            var route = h.Routes.FirstOrDefault(r => r.GetAirliners().Contains(aircraft));
            if (route == null) { Console.WriteLine("  FAILED route not made"); Environment.Exit(1); }
            made.Add(Tuple.Create(route, fc, atSuggestion));
        }
        foreach (var f in h.Fleet)
        {
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(f);
            if (f.Routes.Count > 0 && f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) f.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;
        }
        var g = TheAirline.Models.General.GameObject.GetInstance();
        if (Environment.GetEnvironmentVariable("TAP_TRACE") == "1")
        {
            int shown = 0;
            var r0 = made[0].Item1;
            TheAirline.Helpers.PassengerHelpers.FlightTrace = t =>
            {
                if (t.Route != r0 || shown++ > 5) return;
                Console.WriteLine("    trace " + t.Type + ": direct " + t.Direct.ToString("0.0") + " conn " + t.Connecting.ToString("0.0") + " nearby " + t.Nearby.ToString("0.0") + " daily " + t.DailyDemand.ToString("0.0") + " flights/day " + t.FlightsPerDay.ToString("0.0") + " per flight " + t.PerFlight.ToString("0.00") + " cap " + t.TotalCapacity + " cap% " + t.CapacityPercent.ToString("0.000") + " comp " + t.Competition.ToString("0.000") + " rep " + t.Reputation.ToString("0.00") + " score " + t.Score.ToString("0.00") + " seats " + t.Seats + " -> " + t.Passengers);
            };
            Console.WriteLine("    difficulty: passengers " + g.Difficulty.PassengersLevel + ", price " + g.Difficulty.PriceLevel);
        }
        int startMonth = g.GameTime.Month;
        while (g.GameTime.Month == startMonth) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        for (int i = 0; i < 31 && g.GameTime.Day != 1 || i == 0; i++) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        while (g.GameTime.Day != 1) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var last = g.GameTime.AddMonths(-1);
        foreach (var m in made)
        {
            var route = m.Item1;
            var atCreation = m.Item2;
            // the model checked against the network as it is now (the other route brings connecting passengers)
            var fc = TheAirline.Helpers.RouteForecastService.Forecast(h, route.Destination1, route.Destination2, route.GetAirliners()[0], atCreation.RoundTrips, route);
            Console.WriteLine(string.Format("    at creation the forecast was {0:0}% full, {1:N0} a flight", atCreation.Load*100, atCreation.ProfitPerFlight));
            double load = route.ConnectLastSeats > 0 ? (double) route.ConnectLastAllPassengers/route.ConnectLastSeats : 0;
            double flights = route.TimeTable.Entries.Count*DateTime.DaysInMonth(last.Year, last.Month)/7.0;
            double profit = TheAirline.Helpers.RouteProfitService.Month(route, last.Year, last.Month);
            double ownership = TheAirline.Helpers.OwnershipService.MonthlyCost(route.GetAirliners()[0]);
            double perFlight = flights > 0 ? (profit - ownership)/flights : 0;
            Console.WriteLine(string.Format("  {0}-{1} {2}, {3} trips a day: forecast {4:0}% full, {5:N0} a flight | {6:MMM}: {7:0}% full, {8:N0} a flight (route figures less ownership)",
                route.Destination1.Profile.IATACode, route.Destination2.Profile.IATACode, m.Item3 ? "at the suggestion" : "at the most", fc.RoundTrips, fc.Load*100, fc.ProfitPerFlight, last, load*100, perFlight));
            if (Math.Abs(load - fc.Load) > 0.25) { Console.WriteLine("  FAILED forecast load far off"); Environment.Exit(1); }
        }
        Console.WriteLine("FORECAST TEST OK");
    }

    /// <summary>"vm skip": fast-forward a day, a week and to next month; stop one with Pause; stop when cash goes below zero.</summary>
    static async Task SkipTest(AirlineViewModel av)
    {
        Console.WriteLine("SKIP TEST");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var worker = TheAirline.Helpers.Workers.GameObjectWorker.GetInstance();
        Func<System.Windows.Input.ICommand, string, Task<DateTime>> run = async (cmd, name) =>
        {
            DateTime before = g.GameTime;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cmd.Execute(null);
            // a short fast-forward can start and end between two looks: wait for the date to move and the skip to end
            await Wait(() => g.GameTime > before && !worker.IsSkipping && worker.IsPaused, name + " finished");
            av.Refresh();
            Console.WriteLine("  " + name + ": " + before.ToString("d MMM yyyy") + " -> " + g.GameTime.ToString("d MMM yyyy") + " in " + sw.ElapsedMilliseconds + " ms, paused " + worker.IsPaused + " | " + av.Status);
            return before;
        };
        DateTime b = await run(av.SkipDayCommand, "+1 day");
        if (g.GameTime.Date != b.Date.AddDays(1) || !worker.IsPaused) { Console.WriteLine("  FAILED day"); Environment.Exit(1); }
        b = await run(av.SkipWeekCommand, "+1 week");
        if (g.GameTime.Date != b.Date.AddDays(7) || !worker.IsPaused) { Console.WriteLine("  FAILED week"); Environment.Exit(1); }
        b = await run(av.SkipMonthCommand, "next month");
        if (g.GameTime.Day != 1 || g.GameTime.Month == b.Month || !worker.IsPaused || !av.Status.StartsWith("Fast-forwarded")) { Console.WriteLine("  FAILED month"); Environment.Exit(1); }

        // Pause stops a fast-forward part way
        DateTime start = g.GameTime;
        av.SkipMonthCommand.Execute(null);
        await Wait(() => worker.IsSkipping, "skip started");
        await Wait(() => g.GameTime >= start.AddDays(3), "a few days");
        av.TogglePauseCommand.Execute(null);
        av.Refresh();
        Console.WriteLine("  stopped by Pause on " + g.GameTime.ToString("d MMM yyyy") + ", skipping " + worker.IsSkipping + ", paused " + worker.IsPaused + " | " + av.Status);
        if (worker.IsSkipping || !worker.IsPaused || g.GameTime.Day == 1 || !av.Status.Contains("stopped")) { Console.WriteLine("  FAILED stop"); Environment.Exit(1); }
        DateTime stopped = g.GameTime;
        await Task.Delay(500);
        if (g.GameTime != stopped) { Console.WriteLine("  FAILED the clock ran on after Pause"); Environment.Exit(1); }

        // cash below zero stops it early: a few dollars and a month of rents and wages ahead
        g.HumanAirline.Money = 1000;
        DateTime lowStart = g.GameTime;
        av.SkipMonthCommand.Execute(null);
        await Wait(() => g.GameTime > lowStart && !worker.IsSkipping && worker.IsPaused, "skip finished");
        av.Refresh();
        Console.WriteLine("  low cash: stopped on " + g.GameTime.ToString("d MMM yyyy") + " with " + g.HumanAirline.Money.ToString("N0") + " | " + av.Status);
        if (g.HumanAirline.Money >= 0 || !av.Status.Contains("below zero")) { Console.WriteLine("  FAILED cash stop"); Environment.Exit(1); }
        Console.WriteLine("SKIP TEST OK");
    }

    /// <summary>
    ///     "vm renewalorder": a route's aircraft renewed with a new aircraft ordered from the manufacturer (down payment);
    ///     saved and loaded while waiting; on delivery the new one takes over and the old one is sold.
    /// </summary>
    static async Task RenewalOrderTest(AirlineViewModel av)
    {
        Console.WriteLine("RENEWAL ORDER TEST");
        var h = TheAirline.Models.General.GameObject.GetInstance().HumanAirline;
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var mk = av.Market;
        foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Take(200)) { mk.Selected = item; if (mk.Homebase != null) break; }
        mk.BuyCommand.Execute(null);
        av.Refresh();
        var ap = av.Airports;
        await Wait(() => !ap.IsBusy, "advice");
        ap.SelectedAdvice = ap.Advice.First(x => x.Advice.FleetCanFly);
        var cr = ap.Create;
        foreach (var o in cr.Offers.ToList()) o.LeaseCommand.Execute(null);
        cr.CreateCommand.Execute(null);
        foreach (var o in (ap.Create?.Offers.ToList() ?? new System.Collections.Generic.List<TheAirline.Presentation.GateOffer>())) o.LeaseCommand.Execute(null);
        if (ap.Create != null) ap.Create.CreateCommand.Execute(null);
        var route = h.Routes.First(r => r.HasAirliner);
        var a = route.GetAirliners()[0];
        if (a.Pilots.Count < a.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(a);
        if (a.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped) a.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;
        var oldType = a.Airliner.Type;
        var oldTimes = route.TimeTable.Entries.Where(e => e.Airliner == a).Select(e => e.Day + " " + e.Time).OrderBy(x => x).ToList();
        string code = route.Destination1.Profile.IATACode + "-" + route.Destination2.Profile.IATACode;
        Console.WriteLine("  route " + code + " flown by " + a.Name + " (" + oldType.Name + ", " + oldTimes.Count + " weekly flights)");

        var rn = av.Renewal;
        rn.Reload();
        rn.OldType = rn.OldTypes.First(t => t.Value == oldType);
        rn.Source = rn.SourceChoices.First(c => c.Value == TheAirline.Helpers.MissingSource.OrderNew);
        rn.DownPayment = true;
        // a type built now that can fly the route at the same times, the cheapest
        var options = TheAirline.Helpers.FleetRenewalService.NewTypes(oldType);
        var pickType = options.Where(o => o.Orderable && o.Type.Range >= route.GetDistance() && o.Type.CruisingSpeed >= oldType.CruisingSpeed
                                          && o.Type.MinRunwaylength <= Math.Min(route.Destination1.GetMaxRunwayLength(), route.Destination2.GetMaxRunwayLength()))
                              .OrderBy(o => o.NewPrice).FirstOrDefault();
        if (pickType == null) { Console.WriteLine("  FAILED no type to order"); Environment.Exit(1); }
        rn.NewType = rn.NewTypes.First(t => t.Value == pickType.Type);
        Console.WriteLine("  picked " + rn.NewType.Name);
        Console.WriteLine("  " + rn.Summary);
        foreach (var r in rn.Rows) Console.WriteLine("    " + r);
        double money0 = h.Money;
        rn.RenewCommand.Execute(null);
        rn.ConfirmCommand.Execute(null);
        await Wait(() => rn.Status == "Done." || (rn.Status ?? "").StartsWith("That failed"), "renewal");
        foreach (var r in rn.Report) Console.WriteLine("    done: " + r);
        foreach (var w in rn.Waiting) Console.WriteLine("    waiting: " + w);
        var nw = h.Fleet.FirstOrDefault(f => f.ReplacesOnDelivery == a);
        if (nw == null || !route.GetAirliners().Contains(a) || rn.Waiting.Count != 1) { Console.WriteLine("  FAILED order"); Environment.Exit(1); }
        DateTime delivery = nw.Airliner.BuiltDate;
        Console.WriteLine("  ordered " + nw.Name + ", delivery " + delivery.ToShortDateString() + ", paid now " + (money0 - h.Money).ToString("N0") + ", to pay on delivery " + nw.RemainingPayment.ToString("N0") + "; " + a.Name + " still flies");
        // a second renewal of the same type skips the aircraft that waits for its successor
        rn.Reload();
        rn.OldType = rn.OldTypes.First(t => t.Value == oldType);
        Console.WriteLine("  again: " + string.Join(" | ", rn.Rows));
        if (!rn.Rows.Any(r => r.Contains("is ordered for it"))) { Console.WriteLine("  FAILED waiting aircraft not skipped"); Environment.Exit(1); }

        // save and load while waiting: the link must be kept
        TheAirline.Helpers.SerializedLoadSaveHelpers.SaveGame("ordertest");
        TheAirline.Helpers.SerializedLoadSaveHelpers.LoadGame("ordertest");
        TheAirline.Helpers.SerializedLoadSaveHelpers.DeleteSavedGame("ordertest");
        g = TheAirline.Models.General.GameObject.GetInstance();
        h = g.HumanAirline;
        nw = h.Fleet.First(f => f.Name == nw.Name);
        a = h.Fleet.First(f => f.Name == a.Name);
        route = h.Routes.First(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode == code);
        Console.WriteLine("  after load: " + nw.Name + " replaces " + nw.ReplacesOnDelivery?.Name + " (same object as in the fleet: " + (nw.ReplacesOnDelivery == a) + ")");
        if (nw.ReplacesOnDelivery != a) { Console.WriteLine("  FAILED link lost on load"); Environment.Exit(1); }
        if (a.Pilots.Count < a.Airliner.Type.CockpitCrew) TheAirline.Helpers.AirlineHelpers.HireAirlinerPilots(a);

        double moneyBefore = h.Money;
        while (g.GameTime.Date <= delivery.AddDays(1)) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        var news = g.NewsBox.GetNews().LastOrDefault(n => n.Subject.StartsWith("Fleet renewal"));
        Console.WriteLine("  " + g.GameTime.ToShortDateString() + ": " + news?.Subject + " | " + news?.Body?.Replace("\n", " / "));
        var now = route.GetAirliners();
        var newTimes = route.TimeTable.Entries.Where(e => e.Airliner == nw).Select(e => e.Day + " " + e.Time).OrderBy(x => x).ToList();
        Console.WriteLine("  route now flown by " + string.Join(", ", now.Select(f => f.Name + " " + f.Airliner.Type.Name + " pilots " + f.Pilots.Count + "/" + f.Airliner.Type.CockpitCrew + " " + f.Status))
                          + " | same times " + oldTimes.SequenceEqual(newTimes) + " | " + a.Name + " in fleet " + h.Fleet.Contains(a) + " | paid " + nw.Purchased + " | waiting " + TheAirline.Helpers.FleetRenewalService.Pending().Count);
        if (!now.Contains(nw) || now.Contains(a) || h.Fleet.Contains(a) || nw.ReplacesOnDelivery != null || news == null || nw.Purchased != TheAirline.Models.Airliners.FleetAirliner.PurchasedType.Bought)
        { Console.WriteLine("  FAILED handover"); Environment.Exit(1); }
        Console.WriteLine("RENEWAL ORDER TEST OK");
    }

    /// <summary>
    ///     "vm runway": an Airbus A350-900 (needs 2,810 m) and an airport with a shorter runway: the planner says why it
    ///     can't fly; extended to 2,800 m it still can't (and says so), to 2,900 m it can; an engine that needs 5% less
    ///     runway makes 2,800 m enough.
    /// </summary>
    static void RunwayTest(AirlineViewModel av)
    {
        Console.WriteLine("RUNWAY TEST");
        var g = TheAirline.Models.General.GameObject.GetInstance();
        var h = g.HumanAirline;
        var type = TheAirline.Models.Airliners.AirlinerTypes.GetAllTypes().First(t => t.Name == "Airbus A350-900");
        var a350 = TheAirline.Helpers.AirlineHelpers.CreateAirliner(h, type);
        h.AddAirliner(a350);
        a350.Airliner.EngineType = null;
        var home = h.Airports[0];
        var all = TheAirline.Models.Airports.Airports.GetAllAirports();
        var target = all.Where(x => x != home && x.Runways.Any(r => r.Type == TheAirline.Models.Airports.Runway.RunwayType.Regular) && x.GetMaxRunwayLength() >= 2000 && x.GetMaxRunwayLength() < 2700
                                    && TheAirline.Helpers.MathHelpers.GetDistance(home, x) > 500 && TheAirline.Helpers.MathHelpers.GetDistance(home, x) < 5000)
                        .OrderByDescending(x => x.Profile.Pax).First();
        Console.WriteLine("  " + a350.Name + " " + type.Name + " needs " + a350.Airliner.MinRunwaylength + " m; " + home.Profile.IATACode + " " + home.GetMaxRunwayLength() + " m, " + target.Profile.IATACode + " " + target.GetMaxRunwayLength() + " m");
        Func<bool> canPlan = () =>
        {
            var cr = new TheAirline.Presentation.CreateRouteViewModel(home, target, _ => { });
            Console.WriteLine("    planner: aircraft " + string.Join(" / ", cr.Aircraft.Select(x => x.Name)) + " | " + cr.CantFly);
            return cr.Aircraft.Any(x => x.Airliner == a350);
        };
        if (canPlan() || !(new TheAirline.Presentation.CreateRouteViewModel(home, target, _ => { }).CantFly ?? "").Contains("2,810 m")) { Console.WriteLine("  FAILED before extending"); Environment.Exit(1); }

        var runway = target.Runways.Where(r => r.Type == TheAirline.Models.Airports.Runway.RunwayType.Regular).OrderByDescending(r => r.Length).First();
        string m;
        h.Money = Math.Max(h.Money, 500000000);
        if (!TheAirline.Helpers.RunwayService.Extend(h, target, runway, 2800, out m)) { Console.WriteLine("  FAILED extend: " + m); Environment.Exit(1); }
        Console.WriteLine("  " + m + " | still too short for: " + string.Join(", ", TheAirline.Helpers.RunwayService.TooShortFor(2800, false)));
        Console.WriteLine("  while extending: " + TheAirline.Helpers.RunwayService.Describe(target));
        while (runway.IsExtending) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  extended: " + TheAirline.Helpers.RunwayService.Describe(target));
        if (canPlan()) { Console.WriteLine("  FAILED 2,800 m should still be too short"); Environment.Exit(1); }

        // an engine that needs 5% less runway: 2,670 m, so 2,800 m is enough
        var engine = TheAirline.Models.Airliners.EngineTypes.GetEngineTypes(type).Where(e => e.RunwayModifier < 1).OrderBy(e => e.RunwayModifier).FirstOrDefault();
        if (engine != null)
        {
            a350.Airliner.EngineType = engine;
            Console.WriteLine("  with " + engine.Model + " (runway x" + engine.RunwayModifier + "): needs " + a350.Airliner.MinRunwaylength + " m");
            if (!canPlan()) { Console.WriteLine("  FAILED the engine's shorter run is not used"); Environment.Exit(1); }
            a350.Airliner.EngineType = null;
        }

        if (!TheAirline.Helpers.RunwayService.Extend(h, target, runway, 2900, out m)) { Console.WriteLine("  FAILED extend again: " + m); Environment.Exit(1); }
        Console.WriteLine("  " + m + " | still too short for: " + string.Join(", ", TheAirline.Helpers.RunwayService.TooShortFor(2900, false)));
        while (runway.IsExtending) TheAirline.Helpers.GameObjectHelpers.SimulateTurn();
        Console.WriteLine("  extended: " + TheAirline.Helpers.RunwayService.Describe(target));
        if (!canPlan()) { Console.WriteLine("  FAILED 2,900 m should be enough"); Environment.Exit(1); }
        Console.WriteLine("RUNWAY TEST OK");
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

            // a rival cuts its fare below the player's on the player's route: a rival alert, once a month at most
            // (two computer airlines share a route; for this check one of them stands in for the player)
            if (byPair != null)
            {
                var player = gP.HumanAirline;
                var list2 = byPair.ToList();
                var mineN = list2[0]; var rivalP = list2.First(r => r.Airline != mineN.Airline);
                gP.SetHumanAirline(mineN.Airline);
                double mk = TheAirline.Helpers.PricingService.MarketFare(mineN.Destination1, mineN.Destination2, TheAirline.Models.Airliners.AirlinerClass.ClassType.EconomyClass);
                foreach (var c in mineN.Classes) c.FarePrice = mk * 1.05 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
                foreach (var c in rivalP.Classes) c.FarePrice = mk * 1.1 * TheAirline.Helpers.GeneralHelpers.ClassToPriceFactor(c.Type);
                TheAirline.Helpers.CompetitorService.Clear();
                int keyP = gP.GameTime.Year * 12 + gP.GameTime.Month - 1;
                rivalP.ConnectMonth = keyP; rivalP.ConnectLastAllPassengers = 550; rivalP.ConnectLastSeats = 1000;
                TheAirline.Helpers.AIPricingService.ForgetReviews();
                int newsBefore = gP.NewsBox.GetNews().Count;
                Console.WriteLine("  rival on your route: " + TheAirline.Helpers.AIPricingService.Review(rivalP.Airline, rivalP));
                var n = gP.NewsBox.GetNews().LastOrDefault();
                Console.WriteLine("  news: " + (n == null ? "none" : n.Subject + ": " + n.Body));
                bool okNews = gP.NewsBox.GetNews().Count == newsBefore + 1 && n.Subject.Contains("undercuts you") && n.Type == TheAirline.Models.General.News.NewsType.RivalNews;
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
            // an airline of the same kind (a cargo airline can't merge into a passenger airline)
            var t2 = cheap.Skip(1).Select(r => r.Airline).First(x => (x.AirlineRouteFocus == TheAirline.Models.Routes.Route.RouteType.Cargo) == (me7.AirlineRouteFocus == TheAirline.Models.Routes.Route.RouteType.Cargo));
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
            // (the airline may be sitting on idle aircraft of its own, and may sell one of those instead of one of the
            // six just given to it, so what is counted is how many more of its aircraft reach the used market)
            var ownedO = aiO.Fleet.Select(f => f.Airliner).ToList();
            int marketO = TheAirline.Models.Airliners.Airliners.GetAirlinersForSale().Count(a => ownedO.Contains(a));
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
            Console.WriteLine("  " + aiO.Profile.Name + ": " + idle0 + " idle owned, reserve " + TheAirline.Helpers.OwnershipService.Reserve(aiO) + " -> sold " + sold + " for " + (aiO.Money - cash0).ToString("N0") + ", " + idle1 + " idle left; new on the used market: " + (TheAirline.Models.Airliners.Airliners.GetAirlinersForSale().Count(a => ownedO.Contains(a)) - marketO));
            int onMarketO = TheAirline.Models.Airliners.Airliners.GetAirlinersForSale().Count(a => ownedO.Contains(a)) - marketO;
            if (idle1 > TheAirline.Helpers.OwnershipService.Reserve(aiO) || sold == 0 || aiO.Money <= cash0 || onMarketO != sold)
            {
                Console.WriteLine("  FAILED surplus sale: " + sold + " sold, " + onMarketO + " reached the used market, "
                                  + idle1 + " idle left against a reserve of " + TheAirline.Helpers.OwnershipService.Reserve(aiO)
                                  + ", cash " + (aiO.Money - cash0).ToString("N0"));
                Environment.Exit(1);
            }
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
            // TAP_YEAR starts the game in another year (the probes)
            int startYear; if (int.TryParse(Environment.GetEnvironmentVariable("TAP_YEAR"), out startYear)) { ng.StartYear = startYear; await Wait(() => !ng.IsBusy, "airlines for the year"); }
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
            if (Environment.GetCommandLineArgs().Contains("renewal")) { await RenewalTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("routeprofit")) { await RouteProfitTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("forecast")) { await ForecastTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("aiplan")) { AiPlanTest(); return; }
            if (Environment.GetCommandLineArgs().Contains("oneway") || Environment.GetCommandLineArgs().Contains("onewaytest")) { OneWayProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("follow")) { FollowTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("board")) { BoardTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("history")) { HistoryTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("savesize")) { SaveSizeTest(); return; }
            if (Environment.GetCommandLineArgs().Contains("demandaudit")) { DemandAudit(av); return; }
            if (Environment.GetCommandLineArgs().Contains("tripprobe")) { TripProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("tripadvice")) { TripProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("newprobe")) { NewRouteProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("fastdemand")) { FastDemandTest(); return; }
            if (Environment.GetCommandLineArgs().Contains("rivalmap")) { RivalMapTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("competitors")) { CompetitorsTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("rivalalerts")) { RivalAlertsTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("report")) { await ReportTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("suggest")) { await SuggestTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("typecheck")) { TypeCheck(av); return; }
            if (Environment.GetCommandLineArgs().Contains("orderahead")) { OrderAheadTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("engines")) { EnginesTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("aiempty")) { EmptyRouteTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("airportprobe")) { AirportProbe(av); return; }
            if (Environment.GetCommandLineArgs().Contains("airportsearch")) { AirportSearchTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("airportsort")) { AirportSortTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("statsprobe")) { await StatsProbe(av); return; }
            if (Environment.GetCommandLineArgs().Contains("gapprobe")) { await GapProbe(av); return; }
            if (Environment.GetCommandLineArgs().Contains("reputation")) { await ReputationTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("statistics")) { await StatisticsTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("stats")) { await StatsTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("idle")) { IdleProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("mapanim")) { MapAnimTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("aicheck")) { AiCheckProbe(); return; }
            if (Environment.GetCommandLineArgs().Contains("skip")) { await SkipTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("renewalorder")) { await RenewalOrderTest(av); return; }
            if (Environment.GetCommandLineArgs().Contains("runway")) { RunwayTest(av); return; }
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
                // quitting is a matter of chance each month: give it up to three years when it hasn't happened in one
                for (int mth = 0; mth < 36 && (mth < 12 || (anyIdle && quits == 0)); mth++) quits += TheAirline.Helpers.PilotMoraleService.ProcessMonth(pa).Count;
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
