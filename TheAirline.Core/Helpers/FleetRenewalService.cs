using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>What to do when the new type is too slow for an old aircraft's flights.</summary>
    public enum SlowerChoice
    {
        /// <summary>Shift the times; when the week is too short for that, keep the round trips that fit.</summary>
        ShiftOrKeep,
        KeepWhatFits,
        Skip
    }

    /// <summary>What happens to an old aircraft once its routes are taken over.</summary>
    public enum OldAircraftChoice
    {
        Sell,
        ListForSale,
        KeepIdle
    }

    /// <summary>Where the aircraft come from when the fleet hasn't enough idle ones of the new type.</summary>
    public enum MissingSource
    {
        IdleOnly,
        BuyUsed,
        LeaseUsed,
        OrderNew,

        /// <summary>Used ones on the market first, new ones ordered for the rest.</summary>
        UsedThenNew
    }

    public class RenewalSettings
    {
        public AirlinerType OldType { get; set; }
        public AirlinerType NewType { get; set; }
        public MissingSource Source { get; set; } = MissingSource.BuyUsed;

        /// <summary>For new aircraft: pay the down payment now and the rest on delivery (else all now).</summary>
        public bool DownPayment { get; set; }

        public SlowerChoice Slower { get; set; } = SlowerChoice.ShiftOrKeep;
        public OldAircraftChoice AfterOld { get; set; } = OldAircraftChoice.Sell;

        public bool UsesMarket => Source == MissingSource.BuyUsed || Source == MissingSource.LeaseUsed || Source == MissingSource.UsedThenNew;
        public bool Lease => Source == MissingSource.LeaseUsed;
        public bool Orders => Source == MissingSource.OrderNew || Source == MissingSource.UsedThenNew;
    }

    /// <summary>A type that can replace the old one, with where it can come from.</summary>
    public class RenewalTypeOption
    {
        public AirlinerType Type { get; set; }
        public int Idle { get; set; }
        public int ForSale { get; set; }
        public double UsedFrom { get; set; }

        /// <summary>The manufacturer builds it now (it can be ordered new).</summary>
        public bool Orderable { get; set; }

        public double NewPrice { get; set; }
    }

    /// <summary>One old aircraft and what the renewal does with it.</summary>
    public class RenewalRow
    {
        public FleetAirliner Old { get; set; }

        /// <summary>An idle aircraft of the new type from the fleet, or null.</summary>
        public FleetAirliner IdleNew { get; set; }

        /// <summary>An aircraft of the new type to buy or lease on the market, or null.</summary>
        public Airliner MarketNew { get; set; }

        /// <summary>A new aircraft is ordered for this one: it takes over on delivery.</summary>
        public bool Ordered { get; set; }

        public bool Ok { get; set; }
        public ReplaceMode Mode { get; set; }
        public double Cost { get; set; }

        // the part of the row's text after the new aircraft is named (to finish ordered rows once the delivery is known)
        internal string Head, Fit;

        /// <summary>One line: "N101AA on ATL-MIA: N201AA (idle), same times" or why it is skipped.</summary>
        public string Text { get; set; }
    }

    public class RenewalPlan
    {
        public RenewalSettings Settings { get; set; }
        public List<RenewalRow> Rows { get; } = new List<RenewalRow>();
        public double Cost => Rows.Where(r => r.Ok).Sum(r => r.Cost);
        public int Replacing => Rows.Count(r => r.Ok && (r.IdleNew != null || r.MarketNew != null || r.Ordered));
        public int Buying => Rows.Count(r => r.Ok && r.MarketNew != null);
        public int Ordering => Rows.Count(r => r.Ok && r.Ordered);

        /// <summary>For the new aircraft: the whole price, what is paid now, and the delivery day.</summary>
        public double OrderTotal { get; set; }
        public double OrderNow { get; set; }
        public DateTime OrderDelivery { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>
    ///     Fleet renewal: every aircraft of one type is replaced by an aircraft of another type, route by route, with the
    ///     same logic as replacing one aircraft on the route screen (same times, or shifted times / the round trips
    ///     that fit when the new type is slower). New aircraft come from the idle fleet first, then from the market. The
    ///     old aircraft are sold, listed for sale or kept idle afterwards.
    /// </summary>
    public static class FleetRenewalService
    {
        private static string Code(Route r)
        {
            return r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode;
        }

        private static bool Usable(FleetAirliner f)
        {
            return !AircraftDealService.IsLeasedOut(f) && !AircraftDealService.IsListed(f) && !CharterService.IsOnCharter(f);
        }

        private static bool RightKind(AirlinerType type, Route route)
        {
            AirlinerType.TypeOfAirliner kind = type.TypeAirliner;
            if (route.Type == Route.RouteType.Cargo) return kind == AirlinerType.TypeOfAirliner.Cargo;
            if (route.Type == Route.RouteType.Helicopter) return kind == AirlinerType.TypeOfAirliner.Helicopter;
            return kind == AirlinerType.TypeOfAirliner.Passenger || kind == AirlinerType.TypeOfAirliner.Mixed;
        }

        /// <summary>The aircraft types in the player's fleet, with how many of each.</summary>
        public static List<Tuple<AirlinerType, int>> FleetTypes()
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            return airline.DeliveredFleet.GroupBy(f => f.Airliner.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Name)
                          .Select(g => Tuple.Create(g.Key, g.Count())).ToList();
        }

        private static bool KindMatches(AirlinerType oldType, AirlinerType t)
        {
            if (oldType.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo) return t.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo;
            if (oldType.TypeAirliner == AirlinerType.TypeOfAirliner.Helicopter) return t.TypeAirliner == AirlinerType.TypeOfAirliner.Helicopter;
            return t.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger || t.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed;
        }

        /// <summary>Whether the type is built now (can be ordered new), within a manufacturer contract.</summary>
        public static bool Orderable(Airline airline, AirlinerType t)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            return t.Produced != null && t.Produced.From <= now && t.Produced.To >= now
                   && (airline.Contract == null || airline.Contract.Manufacturer == t.Manufacturer);
        }

        /// <summary>The delivery day of an order of `amount` aircraft (from the production rate, as on the new aircraft page).</summary>
        public static DateTime DeliveryDate(AirlinerType type, int amount)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            var date = new DateTime(now.Year, now.Month, now.Day);
            int rate = Math.Max(1, type.ProductionRate);
            if (amount <= rate/4) return date.AddMonths(3);
            for (int i = (rate/4) + 1; i <= amount; i++)
            {
                date = date.AddDays(Math.Round((double) (365/rate), 0, MidpointRounding.AwayFromZero));
            }
            return date;
        }

        /// <summary>The price of ordering `amount` new aircraft of the type (quantity and contract discounts included).</summary>
        public static double OrderPrice(Airline airline, AirlinerType type, int amount)
        {
            double total = type.Price*amount*(1 - GeneralHelpers.GetAirlinerOrderDiscount(amount));
            if (airline.Contract != null && airline.Contract.Manufacturer == type.Manufacturer) total *= (100 - airline.Contract.Discount)/100;
            return total;
        }

        /// <summary>What is paid when ordering: all of it, or the down payment (the rest on delivery).</summary>
        public static double OrderNow(double total, bool downPayment)
        {
            return downPayment ? total*(GameObject.GetInstance().Difficulty.PriceLevel/10) : total;
        }

        /// <summary>
        ///     The types that can replace the old one: the same kind (passenger, cargo, helicopter), idle in the fleet, on the
        ///     market, or built new now. With how many are idle and for sale, and the price new.
        /// </summary>
        public static List<RenewalTypeOption> NewTypes(AirlinerType oldType)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            DateTime now = GameObject.GetInstance().GameTime;
            var idle = airline.DeliveredFleet.Where(f => f.Routes.Count == 0 && Usable(f) && f.Airliner.Type != oldType && KindMatches(oldType, f.Airliner.Type))
                              .GroupBy(f => f.Airliner.Type).ToDictionary(g => g.Key, g => g.Count());
            var market = Airliners.GetAirlinersForSale(a => a.Status == Airliner.StatusTypes.Normal && a.Type != oldType && KindMatches(oldType, a.Type) && a.BuiltDate <= now)
                                  .Where(a => airline.Contract == null || airline.Contract.Manufacturer == a.Type.Manufacturer)
                                  .GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.ToList());
            var built = AirlinerTypes.GetAllTypes().Where(t => t != oldType && KindMatches(oldType, t) && Orderable(airline, t)).ToList();
            return idle.Keys.Union(market.Keys).Union(built).Distinct()
                       .Select(t => new RenewalTypeOption
                       {
                           Type = t,
                           Idle = idle.TryGetValue(t, out int i) ? i : 0,
                           ForSale = market.TryGetValue(t, out var m) ? m.Count : 0,
                           UsedFrom = market.TryGetValue(t, out var m2) ? m2.Min(a => (double) a.GetPrice()) : 0,
                           Orderable = Orderable(airline, t),
                           NewPrice = Orderable(airline, t) ? OrderPrice(airline, t, 1) : 0
                       })
                       .OrderByDescending(x => x.Idle).ThenByDescending(x => x.ForSale > 0).ThenBy(x => x.Type.Name).ToList();
        }

        /// <summary>The new aircraft from earlier renewals still waiting for delivery, one line each.</summary>
        public static List<string> Pending()
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            return airline.Fleet.Where(f => f.ReplacesOnDelivery != null).OrderBy(f => f.Airliner.BuiltDate)
                          .Select(f => f.Name + " (" + f.Airliner.Type.Name + "), delivery " + f.Airliner.BuiltDate.ToString("d MMM yyyy") + ": takes over "
                                       + (f.ReplacesOnDelivery.Routes.Count > 0 ? string.Join(", ", f.ReplacesOnDelivery.Routes.Select(Code)) + " from " : "from ")
                                       + f.ReplacesOnDelivery.Name)
                          .ToList();
        }

        private static double MarketCost(Airline airline, Airliner a, bool lease)
        {
            double discount = airline.Contract?.Discount ?? 0;
            return lease ? a.GetLeasingPrice()*2 : a.GetPrice()*((100 - discount)/100);
        }

        /// <summary>What a renewal would do, aircraft by aircraft (nothing is changed).</summary>
        public static RenewalPlan Plan(RenewalSettings s)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            var plan = new RenewalPlan {Settings = s};
            if (s.OldType == null || s.NewType == null || s.OldType == s.NewType)
            {
                plan.Summary = "Pick the type to replace and the type to replace it with.";
                return plan;
            }

            var idle = new Queue<FleetAirliner>(airline.DeliveredFleet.Where(f => f.Airliner.Type == s.NewType && f.Routes.Count == 0 && Usable(f)).OrderBy(f => f.Name));
            var market = new Queue<Airliner>(Airliners.GetAirlinersForSale(a => a.Status == Airliner.StatusTypes.Normal && a.Type == s.NewType && a.BuiltDate <= GameObject.GetInstance().GameTime)
                                                      .Where(a => airline.Contract == null || airline.Contract.Manufacturer == a.Type.Manufacturer)
                                                      .OrderBy(a => a.GetAge()).ThenBy(a => a.GetPrice()));
            double money = airline.Money;
            bool canOrder = s.Orders && Orderable(airline, s.NewType);
            var waiting = airline.Fleet.Where(f => f.ReplacesOnDelivery != null).ToDictionary(f => f.ReplacesOnDelivery, f => f);

            foreach (FleetAirliner old in airline.DeliveredFleet.Where(f => f.Airliner.Type == s.OldType).OrderBy(f => f.Name))
            {
                var row = new RenewalRow {Old = old};
                plan.Rows.Add(row);
                string head = old.Name;
                FleetAirliner successor;
                if (waiting.TryGetValue(old, out successor))
                {
                    row.Text = head + ": skipped, " + successor.Name + " (" + successor.Airliner.Type.Name + ") is ordered for it, delivery " + successor.Airliner.BuiltDate.ToString("d MMM yyyy") + ".";
                    continue;
                }
                if (AircraftDealService.IsLeasedOut(old) || CharterService.IsOnCharter(old))
                {
                    row.Text = head + ": skipped, it is " + (AircraftDealService.IsLeasedOut(old) ? "leased out" : "on a charter") + ".";
                    continue;
                }
                if (old.Routes.Count == 0)
                {
                    row.Ok = true;
                    row.Text = head + ": no route, nothing to take over.";
                    continue;
                }
                head += " on " + string.Join(", ", old.Routes.Select(Code));

                // can the new type fly these routes at all?
                string cant = null;
                foreach (Route r in old.Routes)
                {
                    long runway = Math.Min(r.Destination1.GetMaxRunwayLength(), r.Destination2.GetMaxRunwayLength());
                    if (!RightKind(s.NewType, r)) cant = "the " + s.NewType.Name + " is the wrong kind for " + Code(r);
                    else if (s.NewType.Range < r.GetDistance()) cant = "the " + s.NewType.Name + "'s range is too short for " + Code(r);
                    else if (s.NewType.MinRunwaylength > runway) cant = "the runways of " + Code(r) + " are too short for the " + s.NewType.Name;
                    if (cant != null) break;
                }
                if (cant != null)
                {
                    row.Text = head + ": skipped, " + cant + ".";
                    continue;
                }

                // the aircraft that takes over
                FleetAirliner idleNew = idle.Count > 0 ? idle.Peek() : null;
                Airliner marketNew = idleNew == null && s.UsesMarket && market.Count > 0 ? market.Peek() : null;
                bool order = idleNew == null && marketNew == null && canOrder;
                if (idleNew == null && marketNew == null && !order)
                {
                    row.Text = head + ": skipped, no " + s.NewType.Name + " is idle in the fleet" + (s.UsesMarket ? " or for sale on the market" : "")
                               + (s.Orders ? (Orderable(airline, s.NewType) ? "" : ", and it isn't built any more (or your manufacturer contract doesn't allow it)") : "") + ".";
                    continue;
                }
                AircraftSpec spec = idleNew != null ? AircraftSpec.Of(idleNew) : marketNew != null ? AircraftSpec.Of(marketNew) : AircraftSpec.OfType(s.NewType);

                // the flights
                var entries = old.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Airliner == old)).ToList();
                string fit;
                if (AircraftReplaceService.FirstClash(spec, entries) == null)
                {
                    row.Mode = ReplaceMode.SameTimes;
                    fit = "same times";
                }
                else if (s.Slower == SlowerChoice.Skip)
                {
                    row.Text = head + ": skipped, the " + s.NewType.Name + " is too slow for its flights.";
                    continue;
                }
                else if (old.Routes.Count > 1)
                {
                    row.Text = head + ": skipped, the " + s.NewType.Name + " is too slow for its flights on " + old.Routes.Count
                               + " routes together. Replace it route by route on the route screen.";
                    continue;
                }
                else
                {
                    Route r = old.Routes[0];
                    FitPlan shift = s.Slower == SlowerChoice.ShiftOrKeep ? AircraftReplaceService.PlanShift(r, old, spec, new List<RouteTimeTableEntry>()) : null;
                    FitPlan keep = shift == null ? AircraftReplaceService.PlanKeep(r, old, spec, new List<RouteTimeTableEntry>()) : null;
                    if (shift != null)
                    {
                        row.Mode = ReplaceMode.ShiftTimes;
                        fit = "slower: " + shift.Summary;
                    }
                    else if (keep != null)
                    {
                        row.Mode = ReplaceMode.KeepFitting;
                        fit = "slower: " + keep.Summary;
                    }
                    else
                    {
                        row.Text = head + ": skipped, the " + s.NewType.Name + " is too slow for its flights and no round trip fits.";
                        continue;
                    }
                }

                if (order)
                {
                    row.Ordered = true;
                    row.Head = head;
                    row.Fit = fit;
                }
                else if (marketNew != null)
                {
                    double cost = MarketCost(airline, marketNew, s.Lease);
                    if (cost > money)
                    {
                        row.Text = head + ": skipped, not enough money to " + (s.Lease ? "lease" : "buy") + (plan.Buying > 0 ? " another " : " a ") + s.NewType.Name + ".";
                        continue;
                    }
                    money -= cost;
                    row.Cost = cost;
                    market.Dequeue();
                    row.MarketNew = marketNew;
                    row.Text = head + ": " + (s.Lease ? "lease" : "buy") + " " + marketNew.TailNumber + " (built " + marketNew.BuiltDate.Year + ") for "
                               + Math.Round(cost).ToString("N0") + (s.Lease ? " deposit" : "") + ", " + fit + ".";
                }
                else
                {
                    idle.Dequeue();
                    row.IdleNew = idleNew;
                    row.Text = head + ": " + idleNew.Name + " (idle in the fleet), " + fit + ".";
                }
                row.Ok = !row.Ordered;
            }

            // the new aircraft are one order: its price with the quantity discount, paid now (or the down payment); when the
            // money isn't enough, fewer are ordered
            var ordered = plan.Rows.Where(r => r.Ordered).ToList();
            while (ordered.Count > 0 && OrderNow(OrderPrice(airline, s.NewType, ordered.Count), s.DownPayment) > money)
            {
                var last = ordered.Last();
                last.Ordered = false;
                last.Text = last.Head + ": skipped, not enough money to order another " + s.NewType.Name + ".";
                ordered.Remove(last);
            }
            if (ordered.Count > 0)
            {
                plan.OrderTotal = OrderPrice(airline, s.NewType, ordered.Count);
                plan.OrderNow = OrderNow(plan.OrderTotal, s.DownPayment);
                plan.OrderDelivery = DeliveryDate(s.NewType, ordered.Count);
                foreach (var r in ordered)
                {
                    r.Ok = true;
                    r.Cost = plan.OrderNow/ordered.Count;
                    r.Text = r.Head + ": a new " + s.NewType.Name + " is ordered, delivery " + plan.OrderDelivery.ToString("d MMM yyyy") + "; it takes over then ("
                             + r.Fit + "), " + r.Old.Name + " flies until then.";
                }
            }

            int skipped = plan.Rows.Count(r => !r.Ok);
            string after = s.AfterOld == OldAircraftChoice.Sell ? "sold (leased ones go back)" : s.AfterOld == OldAircraftChoice.ListForSale ? "listed for sale" : "kept idle";
            plan.Summary = plan.Rows.Count == 0
                ? "You have no " + s.OldType.Name + "."
                : plan.Replacing + " of " + plan.Rows.Count + " " + s.OldType.Name + " replaced by " + s.NewType.Name
                  + (plan.Buying > 0 ? " (" + plan.Buying + " " + (s.Lease ? "leased" : "bought") + " for " + Math.Round(plan.Rows.Where(r => r.Ok && r.MarketNew != null).Sum(r => r.Cost)).ToString("N0") + ")" : "")
                  + (plan.Ordering > 0 ? " (" + plan.Ordering + " ordered new for " + Math.Round(plan.OrderTotal).ToString("N0")
                                         + (s.DownPayment ? ", " + Math.Round(plan.OrderNow).ToString("N0") + " now and the rest on delivery" : "")
                                         + ", delivery " + plan.OrderDelivery.ToString("d MMM yyyy") + ")" : "")
                  + "; the old ones without a route afterwards are " + after + "." + (skipped > 0 ? " " + skipped + " skipped (see the list)." : "");
            return plan;
        }

        private static Airport HomebaseFor(Airline airline, FleetAirliner old, AirlinerType type)
        {
            List<Airport> bases = AirlineHelpers.GetHomebases(airline, type.MinRunwaylength);
            if (old != null && (bases.Contains(old.Homebase) || old.Homebase?.GetMaxRunwayLength() >= type.MinRunwaylength)) return old.Homebase;
            return bases.FirstOrDefault();
        }

        // takes the old aircraft's pilots that may fly the new type, and hires the rest
        private static void Crew(FleetAirliner old, FleetAirliner nw)
        {
            string family = nw.Airliner.Type.AirlinerFamily;
            foreach (var pilot in old.Pilots.ToList())
            {
                if (nw.Pilots.Count >= nw.Airliner.Type.CockpitCrew) break;
                if (pilot.Aircrafts == null || !pilot.Aircrafts.Contains(family)) continue;
                old.RemovePilot(pilot);
                pilot.Airliner = nw;
                nw.AddPilot(pilot);
            }
            if (nw.Pilots.Count < nw.Airliner.Type.CockpitCrew)
            {
                AirlineHelpers.HireAirlinerPilots(nw);
            }
        }

        private static string DoneWithOld(Airline airline, FleetAirliner old, OldAircraftChoice choice)
        {
            if (old.Routes.Count > 0) return null;
            old.Status = FleetAirliner.AirlinerStatus.Stopped;
            if (choice == OldAircraftChoice.KeepIdle) return null;
            if (choice == OldAircraftChoice.ListForSale)
            {
                string m;
                AircraftDealService.List(old, 1.0, out m);
                return m;
            }

            if (old.Purchased == FleetAirliner.PurchasedType.BoughtDownPayment)
            {
                return old.Name + " is not paid in full, so it is kept.";
            }
            long price = old.Purchased == FleetAirliner.PurchasedType.Bought ? old.Airliner.GetPrice() : 0;
            foreach (var pilot in old.Pilots.ToList()) old.RemovePilot(pilot);
            airline.RemoveAirliner(old);
            if (price > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, price);
                return old.Name + " sold for " + price.ToString("N0") + ".";
            }
            return old.Name + " went back to the lessor.";
        }

        /// <summary>Carries out the plan (worked out again first, as the fleet or the market may have changed). Returns what happened.</summary>
        public static List<string> Renew(RenewalSettings settings)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            RenewalPlan plan = Plan(settings);
            var report = new List<string>();
            DateTime now = GameObject.GetInstance().GameTime;
            var ordered = plan.Rows.Where(r => r.Ok && r.Ordered).ToList();
            if (ordered.Count > 0)
            {
                // one order for all of them: paid now (or the down payment, the rest on each delivery)
                AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -plan.OrderNow);
                if (airline.Contract != null && airline.Contract.Manufacturer == settings.NewType.Manufacturer)
                {
                    airline.Contract.PurchasedAirliners += ordered.Count;
                }
            }
            foreach (RenewalRow row in plan.Rows)
            {
                FleetAirliner old = row.Old;
                if (!row.Ok)
                {
                    report.Add(row.Text);
                    continue;
                }

                if (row.Ordered)
                {
                    Airport home = HomebaseFor(airline, old, settings.NewType) ?? old.Homebase;
                    var airliner = new Airliner(Guid.NewGuid().ToString(), settings.NewType, airline.Profile.Country.TailNumbers.GetNextTailNumber(airline), plan.OrderDelivery);
                    Airliners.AddAirliner(airliner);
                    var nw = new FleetAirliner(settings.DownPayment ? FleetAirliner.PurchasedType.BoughtDownPayment : FleetAirliner.PurchasedType.Bought, now, airline, airliner, home)
                    {
                        RemainingPayment = settings.DownPayment ? Math.Max(0, (plan.OrderTotal - plan.OrderNow)/ordered.Count) : 0,
                        ReplacesOnDelivery = old,
                        RenewalMode = (int) row.Mode,
                        RenewalAfterOld = (int) settings.AfterOld
                    };
                    airline.AddAirliner(nw);
                    report.Add("Ordered " + nw.Name + " (" + settings.NewType.Name + "), delivery " + plan.OrderDelivery.ToString("d MMM yyyy") + ": it takes over "
                               + string.Join(", ", old.Routes.Select(Code)) + " from " + old.Name + " then.");
                    continue;
                }

                if (old.Routes.Count > 0)
                {
                    FleetAirliner nw = row.IdleNew;
                    if (nw == null)
                    {
                        Airliner a = row.MarketNew;
                        if (a.Airline != null)
                        {
                            report.Add(old.Name + ": skipped, " + a.TailNumber + " was sold in the meantime.");
                            continue;
                        }
                        Airport home = HomebaseFor(airline, old, a.Type);
                        if (home == null)
                        {
                            report.Add(old.Name + ": skipped, none of your airports can be the home base of a " + a.Type.Name + ".");
                            continue;
                        }
                        double cost = MarketCost(airline, a, settings.Lease);
                        if (cost > airline.Money)
                        {
                            report.Add(old.Name + ": skipped, not enough money left.");
                            continue;
                        }
                        if (settings.Lease)
                        {
                            if (AirlineHelpers.NeedsNewRegistration(airline, a.TailNumber))
                            {
                                a.TailNumber = airline.Profile.Country.TailNumbers.GetNextTailNumber(airline);
                            }
                            airline.AddAirliner(FleetAirliner.PurchasedType.Leased, a, home);
                            AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Rents, -cost);
                            nw = airline.Fleet.Last(f => f.Airliner == a);
                        }
                        else
                        {
                            nw = AirlineHelpers.BuyAirliner(airline, a, home, airline.Contract?.Discount ?? 0);
                        }
                        if (airline.Contract != null) airline.Contract.PurchasedAirliners++;
                    }

                    Crew(old, nw);
                    var done = new List<string>();
                    foreach (Route route in old.Routes.ToList())
                    {
                        string m;
                        if (AircraftReplaceService.Replace(route, old, nw, row.Mode, out m))
                        {
                            done.Add(Code(route));
                        }
                        else
                        {
                            report.Add(old.Name + " on " + Code(route) + ": " + m);
                        }
                    }
                    if (done.Count > 0)
                    {
                        report.Add(nw.Name + " (" + nw.Airliner.Type.Name + ") took over " + string.Join(", ", done) + " from " + old.Name
                                   + (row.Mode == ReplaceMode.SameTimes ? "" : row.Mode == ReplaceMode.ShiftTimes ? ", times shifted" : ", keeping the round trips that fit") + "."
                                   + (nw.Status == FleetAirliner.AirlinerStatus.Stopped ? " It has no full crew yet." : ""));
                    }
                }

                string after = DoneWithOld(airline, old, settings.AfterOld);
                if (after != null) report.Add(after);
            }
            RouteIndex.Invalidate();
            return report;
        }

        /// <summary>
        ///     Every day: aircraft ordered in a renewal that have been delivered (and paid) take over the routes of the
        ///     aircraft they were ordered for, which is then sold, listed or kept idle as chosen. A news item says what
        ///     happened.
        /// </summary>
        public static void ProcessDeliveries()
        {
            var game = GameObject.GetInstance();
            Airline airline = game?.HumanAirline;
            if (airline == null) return;
            foreach (FleetAirliner nw in airline.Fleet.ToList())
            {
                if (nw.ReplacesOnDelivery == null || nw.Airliner.BuiltDate > game.GameTime || nw.Purchased == FleetAirliner.PurchasedType.BoughtDownPayment) continue;
                FleetAirliner old = nw.ReplacesOnDelivery;
                nw.ReplacesOnDelivery = null;
                var mode = (ReplaceMode) nw.RenewalMode;
                var after = (OldAircraftChoice) nw.RenewalAfterOld;
                var lines = new List<string>();
                if (!airline.Fleet.Contains(old) || old.Routes.Count == 0)
                {
                    lines.Add(nw.Name + " (" + nw.Airliner.Type.Name + ") has been delivered. " + old.Name + " has no routes any more, so there is nothing to take over: put "
                              + nw.Name + " on a route yourself.");
                }
                else
                {
                    Crew(old, nw);
                    foreach (Route route in old.Routes.ToList())
                    {
                        string m;
                        bool ok = AircraftReplaceService.Replace(route, old, nw, mode, out m);
                        // the timetable may have changed since the order: try shifting, then keeping what fits
                        if (!ok && mode == ReplaceMode.SameTimes) ok = AircraftReplaceService.Replace(route, old, nw, ReplaceMode.ShiftTimes, out m);
                        if (!ok && mode != ReplaceMode.KeepFitting) ok = AircraftReplaceService.Replace(route, old, nw, ReplaceMode.KeepFitting, out m);
                        lines.Add(ok ? m : old.Name + " keeps " + Code(route) + ": " + m);
                    }
                    string done = DoneWithOld(airline, old, after);
                    if (done != null) lines.Add(done);
                }
                RouteIndex.Invalidate();
                game.NewsBox.AddNews(new News(News.NewsType.FleetNews, game.GameTime, "Fleet renewal: " + nw.Name + " delivered", string.Join("\n", lines)));
            }
        }
    }
}
