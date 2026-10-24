using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airlines.Subsidiary;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     How computer airlines set their fares (with realistic costs on). Every week or two each route is reviewed:
    ///     full flights raise the fare, empty ones cut it, and a cheaper rival on the same airport pair is answered:
    ///     careful airlines stay a little above it, balanced ones match it, fast-growing ones undercut it. A fare never
    ///     goes below what the flight costs at the load it gets, moves at most 10% per review, and stays within a band
    ///     around the market fare. Business and first move with economy.
    /// </summary>
    public static class AIPricingService
    {
        public const double MaxStep = 0.10;

        /// <summary>The lowest and highest share of the market fare an airline of this mentality charges.</summary>
        public static void Band(Airline.AirlineMentality mentality, out double low, out double high)
        {
            switch (mentality)
            {
                // careful airlines keep their margins, fast-growing ones price like low-fare carriers
                case Airline.AirlineMentality.Safe: low = 0.85; high = 1.40; break;
                case Airline.AirlineMentality.Aggressive: low = 0.70; high = 1.25; break;
                default: low = 0.80; high = 1.35; break;
            }
        }

        /// <summary>
        ///     The new fare as a share of the market fare. ratio: today's; load: share of seats filled; rival: the cheapest
        ///     rival's share of the market fare (null when nobody else flies it); floor: the break-even share.
        /// </summary>
        public static double Decide(double ratio, double load, double? rival, Airline.AirlineMentality mentality, double floor)
        {
            double low, high;
            Band(mentality, out low, out high);
            double target = ratio;

            // the load: aim for flights about 85% full (an empty seat earns nothing, a full flight could charge more)
            if (load > 0.92) target *= 1.06;
            else if (load > 0.87) target *= 1.03;
            else if (load < 0.45) target *= 0.92;
            else if (load < 0.60) target *= 0.95;
            else if (load < 0.75) target *= 0.98;

            // the competition
            if (rival.HasValue && rival.Value > 0)
            {
                double r = rival.Value;
                if (r < ratio*0.95 && load < 0.88)
                {
                    // a rival is cheaper and we have empty seats: answer it
                    double goal = mentality == Airline.AirlineMentality.Aggressive ? r*0.97
                                : mentality == Airline.AirlineMentality.Safe ? r*1.05 : r;
                    target = Math.Min(target, ratio + (goal - ratio)*0.6);
                }
                else if (r > ratio*1.10 && load > 0.75)
                {
                    // everyone else charges more and we're full: follow them up a little
                    target = Math.Max(target, ratio + (r*0.97 - ratio)*0.5);
                }
            }

            // at most 10% per review, inside the band, never below break-even (unless break-even is above the band)
            target = Math.Max(ratio*(1 - MaxStep), Math.Min(ratio*(1 + MaxStep), target));
            double bottom = Math.Min(high, Math.Max(low, floor));
            return Math.Max(bottom, Math.Min(high, target));
        }

        private static bool SameCompany(Airline a, Airline b)
        {
            if (a == b) return true;
            var sa = a as SubsidiaryAirline;
            var sb = b as SubsidiaryAirline;
            return (sa != null && sa.Airline == b) || (sb != null && sb.Airline == a) || (sa != null && sb != null && sa.Airline == sb.Airline);
        }

        // every passenger route by airport pair, built once a game day
        private static readonly object IndexLock = new object();
        private static Dictionary<string, List<PassengerRoute>> _index;
        private static DateTime _indexDay;

        private static string Key(Route r)
        {
            string a = r.Destination1.Profile.IATACode + r.Destination1.Profile.ICAOCode, b = r.Destination2.Profile.IATACode + r.Destination2.Profile.ICAOCode;
            return string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        }

        public static void Invalidate()
        {
            lock (IndexLock) _index = null;
        }

        private static Dictionary<string, List<PassengerRoute>> Index()
        {
            var day = GameObject.GetInstance().GameTime.Date;
            lock (IndexLock)
            {
                if (_index != null && _indexDay == day) return _index;
                var map = new Dictionary<string, List<PassengerRoute>>();
                foreach (var a in Airlines.GetAllAirlines().ToList())
                {
                    foreach (var r in a.Routes)
                    {
                        var pr = r as PassengerRoute;
                        if (pr?.Destination1 == null || pr.Destination2 == null) continue;
                        List<PassengerRoute> list;
                        string k = Key(pr);
                        if (!map.TryGetValue(k, out list)) map[k] = list = new List<PassengerRoute>();
                        list.Add(pr);
                    }
                }
                _index = map;
                _indexDay = day;
                return map;
            }
        }

        /// <summary>The other airlines' flying passenger routes on the same airport pair.</summary>
        public static List<PassengerRoute> Rivals(Route route)
        {
            List<PassengerRoute> all;
            if (route.Airline == null || !Index().TryGetValue(Key(route), out all)) return new List<PassengerRoute>();
            return all.Where(r => r.Airline != null && !SameCompany(r.Airline, route.Airline) && r.HasAirliner && r.Airline.Routes.Contains(r)).ToList();
        }

        // when each route was last reviewed
        private static readonly Dictionary<Route, DateTime> LastReview = new Dictionary<Route, DateTime>();
        public const int ReviewDays = 7;

        /// <summary>Lets every route be reviewed again at once (for tests).</summary>
        public static void ForgetReviews()
        {
            lock (LastReview) LastReview.Clear();
        }

        public static double EconomyFare(PassengerRoute r)
        {
            return r.GetRouteAirlinerClass(AirlinerClass.ClassType.EconomyClass)?.FarePrice ?? 0;
        }

        // the last time a route's cut was reported to the player, so the news isn't flooded
        private static readonly Dictionary<Route, DateTime> Reported = new Dictionary<Route, DateTime>();

        /// <summary>Reviews a computer airline's route and changes its fares. Returns what changed, or null.</summary>
        public static string Review(Airline airline, PassengerRoute route)
        {
            if (!PricingService.Realistic || airline == null || airline.IsHuman || route == null || !route.HasAirliner) return null;
            var now = GameObject.GetInstance().GameTime;
            lock (LastReview)
            {
                DateTime last;
                if (LastReview.TryGetValue(route, out last) && (now - last).TotalDays < ReviewDays) return null;
                LastReview[route] = now;
            }
            var eco = route.GetRouteAirlinerClass(AirlinerClass.ClassType.EconomyClass);
            if (eco == null || eco.FarePrice <= 0) return null;
            double market = PricingService.MarketFare(route.Destination1, route.Destination2, AirlinerClass.ClassType.EconomyClass);
            if (market <= 0) return null;
            double load = ConnectionService.MonthLoad(route);
            if (load <= 0) return null;

            List<PassengerRoute> rivals;
            try { rivals = Rivals(route); }
            catch (InvalidOperationException) { return null; } // another airline changed its routes meanwhile; next time
            var cheapest = rivals.Where(r => EconomyFare(r) > 0).OrderBy(EconomyFare).FirstOrDefault();
            double? rival = cheapest != null ? EconomyFare(cheapest)/market : (double?) null;

            double floor = 0;
            try { floor = PricingService.BreakEvenFare(route, Math.Max(0.5, Math.Min(0.9, load)))/market; }
            catch (Exception) { floor = 0; }

            double ratio = eco.FarePrice/market;
            double next = Decide(ratio, load, rival, airline.Mentality, floor);
            if (Math.Abs(next - ratio)/ratio < 0.01) return null;

            double factor = next/ratio;
            foreach (var c in route.Classes) c.FarePrice = Math.Round(c.FarePrice*factor, 2);

            string pair = route.Destination1.Profile.IATACode + "-" + route.Destination2.Profile.IATACode;
            string change = string.Format("{0} {1}: economy {2:0.00} -> {3:0.00} ({4:+0;-0}%), {5:0}% full{6}",
                                          airline.Profile.IATACode, pair, eco.FarePrice/factor, eco.FarePrice, (factor - 1)*100, load*100,
                                          rival.HasValue ? string.Format(", cheapest rival {0:0.00}", EconomyFare(cheapest)) : "");

            // tell the player when a fare on one of their routes is cut
            var game = GameObject.GetInstance();
            var human = rivals.FirstOrDefault(r => r.Airline == game.HumanAirline);
            if (human != null && factor < 0.96)
            {
                bool tell;
                lock (Reported)
                {
                    DateTime last;
                    tell = !Reported.TryGetValue(human, out last) || (game.GameTime - last).TotalDays >= 30;
                    if (tell) Reported[human] = game.GameTime;
                }
                if (tell)
                {
                    var f = new ValueCurrencyFormatter();
                    lock (game.NewsBox) game.NewsBox.AddNews(new News(
                        News.NewsType.AirlineNews, game.GameTime, airline.Profile.Name + " cuts fares on " + pair,
                        string.Format("{0} cut its economy fare on {1} by {2:0}% to {3}. Your economy fare there is {4}; the market fare is {5}. Their flights were {6:0}% full.",
                                      airline.Profile.Name, pair, (1 - factor)*100, f.Convert(eco.FarePrice), f.Convert(EconomyFare(human)), f.Convert(market), load*100)));
                }
            }
            return change;
        }
    }
}
