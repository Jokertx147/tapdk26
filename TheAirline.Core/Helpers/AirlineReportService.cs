using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>An airline's flying in one month: flights, what they carried, how many left on time; its cash and fleet at the month's end.</summary>
    public class MonthOps
    {
        public int Year, Month;
        public int Flights, Cancelled, OnTime;
        public long Passengers, Seats;
        public double Cargo, CargoCapacity;

        /// <summary>Set when the month has ended (then Cash, Fleet and Idle are the month-end figures).</summary>
        public bool Closed;

        public double Cash;
        public int Fleet, Idle;

        public MonthOps Copy() => (MonthOps) MemberwiseClone();

        public double Load => Seats > 0 ? (double) Passengers/Seats : 0;
        public double CargoLoad => CargoCapacity > 0 ? Cargo/CargoCapacity : 0;
        public double OnTimeShare => Flights > 0 ? (double) OnTime/Flights : 0;
    }

    /// <summary>One route's flying in one month (kept by id: a route given up still shows in its months).</summary>
    public class RouteMonthOps
    {
        public string RouteId, Name;
        public bool IsCargo;
        public int Year, Month, Flights;
        public long Passengers, Seats;
        public double Cargo, CargoCapacity;
        public double Load => IsCargo ? (CargoCapacity > 0 ? Cargo/CargoCapacity : 0) : (Seats > 0 ? (double) Passengers/Seats : 0);
    }

    /// <summary>An airline's months as a saved game keeps them.</summary>
    public class SavedMonthOps
    {
        public Airline Airline;
        public List<MonthOps> Months;
        public List<RouteMonthOps> Routes;
    }

    public class ReportMoneyLine
    {
        public string Name { get; set; }
        public double Amount { get; set; }
        public double Previous { get; set; }
    }

    public class ReportRoute
    {
        public string RouteId { get; set; }
        public string Name { get; set; }
        public bool IsCargo { get; set; }
        public double Profit { get; set; }

        /// <summary>Seats (or cargo space) filled; 0 when it didn't fly.</summary>
        public double Load { get; set; }

        public int Flights { get; set; }
        public double Carried { get; set; }
    }

    public class ReportRival
    {
        public Airline Airline { get; set; }
        public int SharedRoutes { get; set; }
        public int SharedAirports { get; set; }
        public double Result { get; set; }
        public double Income { get; set; }
        public double Load { get; set; }

        /// <summary>Their fares against the player's on the routes they share (1 = the same; 0 when none).</summary>
        public double FareAgainstMine { get; set; }
    }

    public enum ReportItemKind
    {
        Route,
        Competitors,
        Fleet,
        Finances,
        None,

        /// <summary>Aircraft without a route: Routes > Suggestions.</summary>
        IdleAircraft
    }

    /// <summary>Something the report says needs looking at, and where to go for it.</summary>
    public class ReportItem
    {
        public string Text { get; set; }
        public ReportItemKind Kind { get; set; }

        /// <summary>The route it is about (Route and Competitors items).</summary>
        public string RouteId { get; set; }

        /// <summary>The airport pair (Competitors items).</summary>
        public string A { get; set; }
        public string B { get; set; }
        public bool IsCargo { get; set; }

        /// <summary>The rival it is about (a Competitors item without a pair).</summary>
        public Airline Airline { get; set; }

        /// <summary>Higher first.</summary>
        public double Weight { get; set; }
    }

    /// <summary>What a monthly report news item points to.</summary>
    public class ReportLink
    {
        public int Year, Month;
    }

    /// <summary>A month of an airline: its money, its flying, its routes, its rivals and what needs attention.</summary>
    public class AirlineReport
    {
        public Airline Airline { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>The month isn't over yet: its figures are so far.</summary>
        public bool SoFar { get; set; }

        public int DaysCounted { get; set; }
        public int DaysInMonth { get; set; }
        public bool HasPrevious { get; set; }

        public double Income { get; set; }
        public double Costs { get; set; }
        public double Result => Income + Costs;
        public double PreviousIncome { get; set; }
        public double PreviousCosts { get; set; }
        public double PreviousResult => PreviousIncome + PreviousCosts;

        /// <summary>Money by kind (income and operating costs), the largest first.</summary>
        public List<ReportMoneyLine> Money { get; set; } = new List<ReportMoneyLine>();

        /// <summary>Aircraft bought and sold, and loans (taken, repaid, interest): money that isn't the operating result.</summary>
        public List<ReportMoneyLine> Capital { get; set; } = new List<ReportMoneyLine>();

        /// <summary>Cash at the month's end (now, for this month).</summary>
        public double Cash { get; set; }

        public double PreviousCash { get; set; }
        public MonthOps Ops { get; set; }
        public MonthOps PreviousOps { get; set; }
        public int Routes { get; set; }
        public List<ReportRoute> Best { get; set; } = new List<ReportRoute>();
        public List<ReportRoute> Worst { get; set; } = new List<ReportRoute>();
        public List<ReportRival> Rivals { get; set; } = new List<ReportRival>();
        public List<ReportItem> Attention { get; set; } = new List<ReportItem>();

        public string Title => AirlineReportService.MonthName(Year, Month) + (SoFar ? " so far" : "");
    }

    /// <summary>
    ///     The monthly airline report: each airline's flights by month (the game's own statistics are kept by year), and
    ///     the report built from them, the invoices and the routes. A news item brings the player's report at each new
    ///     month. The months are saved with the game (an older save has the months from loading on).
    /// </summary>
    public static class AirlineReportService
    {
        /// <summary>Months kept: this one and the twelve before.</summary>
        public const int KeepMonths = 13;

        /// <summary>A flight that left this late or less was on time.</summary>
        public const int OnTimeMinutes = 15;

        private class Book
        {
            public readonly Dictionary<int, MonthOps> Months = new Dictionary<int, MonthOps>();
            public readonly Dictionary<string, RouteMonthOps> Routes = new Dictionary<string, RouteMonthOps>();
        }

        private static readonly ConditionalWeakTable<Airline, Book> Books = new ConditionalWeakTable<Airline, Book>();

        private static readonly Invoice.InvoiceType[] Operating =
            Enum.GetValues(typeof(Invoice.InvoiceType)).Cast<Invoice.InvoiceType>()
                .Where(t => t != Invoice.InvoiceType.Total && t != Invoice.InvoiceType.Purchases && t != Invoice.InvoiceType.Loans).ToArray();

        private static int Key(int year, int month) => year*12 + month - 1;

        public static string MonthName(int year, int month)
        {
            return CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month) + " " + year;
        }

        public static string RouteName(Route r)
        {
            return r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode;
        }

        // ---- recording ----

        private static MonthOps MonthOf(Book b, int year, int month)
        {
            MonthOps m;
            int k = Key(year, month);
            if (!b.Months.TryGetValue(k, out m)) b.Months[k] = m = new MonthOps {Year = year, Month = month};
            return m;
        }

        private static RouteMonthOps RouteMonthOf(Book b, Route route, int year, int month)
        {
            string k = Key(year, month) + "|" + route.Id;
            RouteMonthOps m;
            if (!b.Routes.TryGetValue(k, out m))
                b.Routes[k] = m = new RouteMonthOps {RouteId = route.Id, Name = RouteName(route), IsCargo = route.Type == Route.RouteType.Cargo, Year = year, Month = month};
            return m;
        }

        /// <summary>A flight landed (called with its record from FlightRecords).</summary>
        public static void Flown(FleetAirliner airliner, FlownFlight f, Route route)
        {
            Airline airline = airliner?.Airliner?.Airline;
            if (airline == null || f == null) return;
            DateTime when = f.Takeoff == default(DateTime) ? GameObject.GetInstance().GameTime : f.Takeoff;
            double capacity = f.IsCargo ? airliner.Airliner.GetCargoCapacity() : 0;
            Book b = Books.GetValue(airline, x => new Book());
            lock (b)
            {
                MonthOps m = MonthOf(b, when.Year, when.Month);
                m.Flights++;
                if (f.DelayMinutes <= OnTimeMinutes) m.OnTime++;
                m.Passengers += f.Passengers;
                m.Seats += f.Seats;
                m.Cargo += f.Cargo;
                m.CargoCapacity += capacity;
                // routes month by month for the player's airlines only (the computer airlines' totals are enough for the report)
                if (route != null && airline.IsHuman && route.Destination1 != null && route.Destination2 != null)
                {
                    RouteMonthOps r = RouteMonthOf(b, route, when.Year, when.Month);
                    r.Flights++;
                    r.Passengers += f.Passengers;
                    r.Seats += f.Seats;
                    r.Cargo += f.Cargo;
                    r.CargoCapacity += capacity;
                }
            }
        }

        /// <summary>A flight was cancelled.</summary>
        public static void Cancelled(FleetAirliner airliner)
        {
            Airline airline = airliner?.Airliner?.Airline;
            if (airline == null) return;
            DateTime now = GameObject.GetInstance().GameTime;
            Book b = Books.GetValue(airline, x => new Book());
            lock (b) MonthOf(b, now.Year, now.Month).Cancelled++;
        }

        /// <summary>The month's flying (a fresh empty one when nothing was recorded).</summary>
        public static MonthOps Ops(Airline airline, int year, int month)
        {
            Book b;
            if (airline == null || !Books.TryGetValue(airline, out b)) return new MonthOps {Year = year, Month = month};
            lock (b)
            {
                MonthOps m;
                if (!b.Months.TryGetValue(Key(year, month), out m)) return new MonthOps {Year = year, Month = month};
                return m.Copy();
            }
        }

        public static List<RouteMonthOps> RouteOps(Airline airline, int year, int month)
        {
            Book b;
            if (airline == null || !Books.TryGetValue(airline, out b)) return new List<RouteMonthOps>();
            lock (b) return b.Routes.Values.Where(r => r.Year == year && r.Month == month).ToList();
        }

        /// <summary>
        ///     At a new month: every airline's month just ended gets its month-end cash and fleet, the oldest months are
        ///     dropped, and the player gets the report in the news.
        /// </summary>
        public static void MonthEnded()
        {
            var game = GameObject.GetInstance();
            DateTime last = game.GameTime.AddMonths(-1);
            int oldest = Key(game.GameTime.Year, game.GameTime.Month) - (KeepMonths - 1);
            foreach (Airline airline in Airlines.GetAllAirlines().ToList())
            {
                Book b = Books.GetValue(airline, x => new Book());
                int fleet, idle;
                Fleet(airline, out fleet, out idle);
                lock (b)
                {
                    MonthOps m = MonthOf(b, last.Year, last.Month);
                    m.Closed = true;
                    m.Cash = airline.Money;
                    m.Fleet = fleet;
                    m.Idle = idle;
                    foreach (int k in b.Months.Keys.Where(k => k < oldest).ToList()) b.Months.Remove(k);
                    foreach (var k in b.Routes.Where(x => Key(x.Value.Year, x.Value.Month) < oldest).Select(x => x.Key).ToList()) b.Routes.Remove(k);
                }
            }
            if (!Infrastructure.Settings.GetInstance().NoMonthlyReport) TellPlayer(last.Year, last.Month);
        }

        private static void Fleet(Airline airline, out int fleet, out int idle)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            List<FleetAirliner> list;
            lock (airline.Fleet) list = airline.Fleet.ToList();
            fleet = list.Count(f => f.Airliner.BuiltDate <= now);
            idle = list.Count(f => f.Airliner.BuiltDate <= now && !f.HasRoute && f.UsableBy(airline));
        }

        /// <summary>For the tests: report news sent.</summary>
        public static int ReportsSent;

        private static void TellPlayer(int year, int month)
        {
            try
            {
                var game = GameObject.GetInstance();
                Airline me = game.HumanAirline;
                if (me == null) return;
                AirlineReport r = Build(me, year, month);
                if (r.Ops.Flights == 0 && Math.Abs(r.Income) < 1 && Math.Abs(r.Costs) < 1) return; // nothing happened yet
                var f = new ValueCurrencyFormatter();
                string change = r.HasPrevious && Math.Abs(r.PreviousResult) >= 1
                    ? string.Format(" ({0} {1} on {2})", r.Result >= r.PreviousResult ? "up" : "down", f.Convert(Math.Abs(r.Result - r.PreviousResult)), CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(r.PreviousOps.Month))
                    : "";
                string flying = r.Ops.Flights == 0
                    ? "No flights."
                    : string.Format("{0:#,0} flights{1}, {2:0}% on time.", r.Ops.Flights,
                                    r.Ops.Seats > 0 ? string.Format(", {0:#,0} passengers, {1:0}% of seats filled", r.Ops.Passengers, r.Ops.Load*100) : "",
                                    r.Ops.OnTimeShare*100);
                string body = string.Format("Operating result {0}{1}: income {2}, costs {3}. {4} Cash at the month's end: {5}. {6}",
                                            f.Convert(r.Result), change, f.Convert(r.Income), f.Convert(-r.Costs), flying, f.Convert(r.Cash),
                                            r.Attention.Count == 0 ? "Nothing needs your attention." : r.Attention.Count == 1 ? "1 thing needs your attention." : r.Attention.Count + " things need your attention.");
                var news = new News(News.NewsType.AirlineNews, game.GameTime, "Your report for " + MonthName(year, month), body) {ActionObject = new ReportLink {Year = year, Month = month}};
                lock (game.NewsBox) game.NewsBox.AddNews(news);
                ReportsSent++;
            }
            catch (Exception)
            {
                // a report that can't be made never stops the game day
            }
        }

        // ---- the report ----

        /// <summary>The months a report can be made for, the latest first (this month so far included).</summary>
        public static List<Tuple<int, int>> ReportMonths(Airline airline)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            DateTime start = GameObject.GetInstance().StartDate;
            var list = new List<Tuple<int, int>>();
            for (int i = 0; i < KeepMonths; i++)
            {
                DateTime d = now.AddMonths(-i);
                if (Key(d.Year, d.Month) < Key(start.Year, start.Month)) break;
                list.Add(Tuple.Create(d.Year, d.Month));
            }
            return list;
        }

        public static string MoneyName(Invoice.InvoiceType t)
        {
            switch (t)
            {
                case Invoice.InvoiceType.Tickets: return "Tickets and cargo";
                case Invoice.InvoiceType.OnFlightIncome: return "In-flight sales";
                case Invoice.InvoiceType.FlightExpenses: return "Fuel, handling and overhead";
                case Invoice.InvoiceType.Maintenances: return "Maintenance";
                case Invoice.InvoiceType.Wages: return "Wages";
                case Invoice.InvoiceType.Fees: return "Fees";
                case Invoice.InvoiceType.Rents: return "Rents and leases";
                case Invoice.InvoiceType.AirlineExpenses: return "Other expenses";
                case Invoice.InvoiceType.Purchases: return "Aircraft bought and sold";
                case Invoice.InvoiceType.Loans: return "Loans";
                default: return t.ToString();
            }
        }

        /// <summary>An airline's operating income and costs in a month (aircraft bought and loans left out).</summary>
        public static void IncomeAndCosts(Airline airline, int year, int month, out double income, out double costs)
        {
            income = 0;
            costs = 0;
            foreach (var t in Operating)
            {
                double v = airline.GetInvoicesAmountMonth(year, month, t);
                if (v > 0) income += v;
                else costs += v;
            }
        }

        public static AirlineReport Build(Airline me, int year, int month)
        {
            var game = GameObject.GetInstance();
            DateTime now = game.GameTime;
            var r = new AirlineReport {Airline = me, Year = year, Month = month, SoFar = year == now.Year && month == now.Month, DaysInMonth = DateTime.DaysInMonth(year, month)};
            r.DaysCounted = r.SoFar ? now.Day : r.DaysInMonth;
            DateTime prev = new DateTime(year, month, 1).AddMonths(-1);
            DateTime start = game.StartDate;
            r.HasPrevious = Key(prev.Year, prev.Month) >= Key(start.Year, start.Month);

            double income, costs;
            IncomeAndCosts(me, year, month, out income, out costs);
            r.Income = income;
            r.Costs = costs;
            if (r.HasPrevious)
            {
                IncomeAndCosts(me, prev.Year, prev.Month, out income, out costs);
                r.PreviousIncome = income;
                r.PreviousCosts = costs;
            }
            foreach (var t in Operating)
            {
                double v = me.GetInvoicesAmountMonth(year, month, t), p = r.HasPrevious ? me.GetInvoicesAmountMonth(prev.Year, prev.Month, t) : 0;
                if (Math.Abs(v) >= 0.5 || Math.Abs(p) >= 0.5) r.Money.Add(new ReportMoneyLine {Name = MoneyName(t), Amount = v, Previous = p});
            }
            r.Money = r.Money.OrderByDescending(x => x.Amount > 0).ThenByDescending(x => Math.Abs(x.Amount)).ToList();
            foreach (var t in new[] {Invoice.InvoiceType.Purchases, Invoice.InvoiceType.Loans})
            {
                double v = me.GetInvoicesAmountMonth(year, month, t), p = r.HasPrevious ? me.GetInvoicesAmountMonth(prev.Year, prev.Month, t) : 0;
                if (Math.Abs(v) >= 0.5 || Math.Abs(p) >= 0.5) r.Capital.Add(new ReportMoneyLine {Name = MoneyName(t), Amount = v, Previous = p});
            }

            r.Ops = Ops(me, year, month);
            r.PreviousOps = r.HasPrevious ? Ops(me, prev.Year, prev.Month) : new MonthOps {Year = prev.Year, Month = prev.Month};
            if (r.SoFar || !r.Ops.Closed)
            {
                int fleet, idle;
                Fleet(me, out fleet, out idle);
                if (r.SoFar)
                {
                    r.Ops.Fleet = fleet;
                    r.Ops.Idle = idle;
                }
                r.Cash = r.SoFar ? me.Money : r.Ops.Closed ? r.Ops.Cash : 0;
            }
            else r.Cash = r.Ops.Cash;
            r.PreviousCash = r.PreviousOps.Closed ? r.PreviousOps.Cash : 0;

            Routes(r, me, year, month, prev);
            RivalsOf(r, me, year, month);
            Attention(r, me, year, month, prev);
            return r;
        }

        private static void Routes(AirlineReport r, Airline me, int year, int month, DateTime prev)
        {
            List<Route> routes = me.Routes.ToList();
            r.Routes = routes.Count;
            var ops = RouteOps(me, year, month).ToDictionary(x => x.RouteId);
            var rows = new List<ReportRoute>();
            foreach (Route route in routes)
            {
                if (route.Destination1 == null || route.Destination2 == null) continue;
                RouteMonthOps o;
                ops.TryGetValue(route.Id, out o);
                double profit = RouteProfitService.Month(route, year, month);
                if (o == null && Math.Abs(profit) < 0.5) continue; // didn't fly that month
                rows.Add(new ReportRoute
                {
                    RouteId = route.Id, Name = RouteName(route), IsCargo = route.Type == Route.RouteType.Cargo, Profit = profit,
                    Load = o?.Load ?? 0, Flights = o?.Flights ?? 0, Carried = o == null ? 0 : o.IsCargo ? o.Cargo : o.Passengers
                });
            }
            var ordered = rows.OrderByDescending(x => x.Profit).ToList();
            r.Best = ordered.Where(x => x.Profit > 0).Take(5).ToList();
            r.Worst = ordered.Where(x => x.Profit < 0).Reverse().Take(5).ToList();
        }

        private static void RivalsOf(AirlineReport r, Airline me, int year, int month)
        {
            List<RivalAirline> rivals;
            try { rivals = CompetitorService.Rivals(me); }
            catch (Exception) { return; } // another airline changed its routes meanwhile
            foreach (RivalAirline x in rivals.Where(x => x.SharedRoutes.Count > 0 || x.SharedAirports.Count > 0).Take(3))
            {
                double income, costs;
                IncomeAndCosts(x.Airline, year, month, out income, out costs);
                MonthOps o = Ops(x.Airline, year, month);
                r.Rivals.Add(new ReportRival
                {
                    Airline = x.Airline, SharedRoutes = x.SharedRoutes.Count, SharedAirports = x.SharedAirports.Count,
                    Result = income + costs, Income = income, Load = o.Load, FareAgainstMine = x.FareAgainstMine
                });
            }
        }

        private static void Attention(AirlineReport r, Airline me, int year, int month, DateTime prev)
        {
            var f = new ValueCurrencyFormatter();
            var items = new List<ReportItem>();
            var ops = RouteOps(me, year, month).ToDictionary(x => x.RouteId);
            bool full = !r.SoFar || r.DaysCounted >= 10; // a few days aren't enough to judge a route on

            foreach (Route route in me.Routes.ToList())
            {
                if (route.Destination1 == null || route.Destination2 == null) continue;
                string name = RouteName(route);
                double p = RouteProfitService.Month(route, year, month);
                double pp = r.HasPrevious ? RouteProfitService.Month(route, prev.Year, prev.Month) : 0;
                // losing money two months running
                if (full && p < 0 && pp < 0)
                {
                    items.Add(new ReportItem
                    {
                        Kind = ReportItemKind.Route, RouteId = route.Id, Weight = -(p + pp),
                        Text = string.Format("{0} lost {1} in {2} and {3} in {4}.", name, f.Convert(-p), CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month) + (r.SoFar ? " so far" : ""),
                                             f.Convert(-pp), CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(prev.Month))
                    });
                    continue;
                }
                // flying with most seats empty
                RouteMonthOps o;
                if (full && ops.TryGetValue(route.Id, out o) && o.Flights >= 10 && o.Load < 0.4)
                {
                    items.Add(new ReportItem
                    {
                        Kind = ReportItemKind.Route, RouteId = route.Id, Weight = 1000*(0.4 - o.Load),
                        Text = string.Format("{0} flew {1} flights only {2:0}% full.", name, o.Flights, o.Load*100)
                    });
                }
            }

            // a rival is cheaper on one of my routes (as things stand now)
            try
            {
                foreach (RouteRivals rr in CompetitorService.ForRoutes(me))
                {
                    if (rr.MyFare <= 0) continue;
                    var cheaper = rr.Rivals.Where(x => x.Fare > 0 && x.Fare < rr.MyFare*0.97).OrderBy(x => x.Fare).FirstOrDefault();
                    if (cheaper == null) continue;
                    items.Add(new ReportItem
                    {
                        Kind = ReportItemKind.Competitors, RouteId = rr.Mine.Id, A = rr.Mine.Destination1.Profile.IATACode, B = rr.Mine.Destination2.Profile.IATACode,
                        IsCargo = rr.Mine.Type == Route.RouteType.Cargo, Weight = 500*(1 - cheaper.Fare/rr.MyFare),
                        Text = string.Format("{0} charges {1} on {2}, {3:0}% below your {4}.", cheaper.Airline.Profile.Name, f.Convert(cheaper.Fare), RouteName(rr.Mine),
                                             (1 - cheaper.Fare/rr.MyFare)*100, f.Convert(rr.MyFare))
                    });
                }
            }
            catch (Exception)
            {
                // another airline changed its routes meanwhile
            }

            // aircraft without a route
            int idle = r.SoFar ? r.Ops.Idle : r.Ops.Closed ? r.Ops.Idle : 0;
            if (idle > 0)
                items.Add(new ReportItem
                {
                    Kind = ReportItemKind.IdleAircraft, Weight = 300*idle,
                    Text = idle == 1 ? "1 aircraft had no route" + (r.SoFar ? "." : " at the month's end.") : idle + " aircraft had no route" + (r.SoFar ? "." : " at the month's end.")
                });

            // cash for less than a month of costs
            double monthCosts = -(r.SoFar ? r.PreviousCosts : r.Costs);
            if (monthCosts > 0 && r.Cash < monthCosts)
                items.Add(new ReportItem
                {
                    Kind = ReportItemKind.Finances, Weight = 1e9,
                    Text = string.Format("Cash of {0} covers less than a month of costs ({1}).", f.Convert(r.Cash), f.Convert(monthCosts))
                });

            // late flights
            if (r.Ops.Flights >= 30 && r.Ops.OnTimeShare < 0.8)
                items.Add(new ReportItem
                {
                    Kind = ReportItemKind.Fleet, Weight = 200,
                    Text = string.Format("Only {0:0}% of flights left on time ({1} cancelled). Older aircraft and too little time between flights cause delays.", r.Ops.OnTimeShare*100, r.Ops.Cancelled)
                });

            r.Attention = items.OrderByDescending(x => x.Weight).Take(8).ToList();
        }

        // ---- saving ----

        public static void Clear()
        {
            Books.Clear();
        }

        public static List<SavedMonthOps> ToSave(IEnumerable<Airline> airlines)
        {
            var result = new List<SavedMonthOps>();
            foreach (Airline a in airlines)
            {
                Book b;
                if (!Books.TryGetValue(a, out b)) continue;
                lock (b) result.Add(new SavedMonthOps {Airline = a, Months = b.Months.Values.ToList(), Routes = b.Routes.Values.ToList()});
            }
            return result;
        }

        public static int Restore(List<SavedMonthOps> saved)
        {
            Clear();
            if (saved == null) return 0;
            int n = 0;
            foreach (SavedMonthOps s in saved)
            {
                if (s?.Airline == null) continue;
                Book b = Books.GetValue(s.Airline, x => new Book());
                lock (b)
                {
                    foreach (MonthOps m in s.Months ?? new List<MonthOps>())
                    {
                        if (m == null) continue;
                        b.Months[Key(m.Year, m.Month)] = m;
                        n++;
                    }
                    foreach (RouteMonthOps m in s.Routes ?? new List<RouteMonthOps>())
                        if (m?.RouteId != null) b.Routes[Key(m.Year, m.Month) + "|" + m.RouteId] = m;
                }
            }
            return n;
        }
    }
}
