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

    public class RenewalSettings
    {
        public AirlinerType OldType { get; set; }
        public AirlinerType NewType { get; set; }

        /// <summary>Buy (or lease) aircraft of the new type on the market when the fleet hasn't enough idle ones.</summary>
        public bool BuyMissing { get; set; } = true;

        public bool Lease { get; set; }
        public SlowerChoice Slower { get; set; } = SlowerChoice.ShiftOrKeep;
        public OldAircraftChoice AfterOld { get; set; } = OldAircraftChoice.Sell;
    }

    /// <summary>One old aircraft and what the renewal does with it.</summary>
    public class RenewalRow
    {
        public FleetAirliner Old { get; set; }

        /// <summary>An idle aircraft of the new type from the fleet, or null.</summary>
        public FleetAirliner IdleNew { get; set; }

        /// <summary>An aircraft of the new type to buy or lease on the market, or null.</summary>
        public Airliner MarketNew { get; set; }

        public bool Ok { get; set; }
        public ReplaceMode Mode { get; set; }
        public double Cost { get; set; }

        /// <summary>One line: "N101AA on ATL-MIA: N201AA (idle), same times" or why it is skipped.</summary>
        public string Text { get; set; }
    }

    public class RenewalPlan
    {
        public RenewalSettings Settings { get; set; }
        public List<RenewalRow> Rows { get; } = new List<RenewalRow>();
        public double Cost => Rows.Where(r => r.Ok).Sum(r => r.Cost);
        public int Replacing => Rows.Count(r => r.Ok && (r.IdleNew != null || r.MarketNew != null));
        public int Buying => Rows.Count(r => r.Ok && r.MarketNew != null);
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

        /// <summary>
        ///     The types that can replace the old one: the same kind (passenger, cargo, helicopter), idle in the fleet or
        ///     on the market. With how many are idle and how many are for sale.
        /// </summary>
        public static List<Tuple<AirlinerType, int, int, double>> NewTypes(AirlinerType oldType)
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            bool cargo = oldType.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo;
            bool heli = oldType.TypeAirliner == AirlinerType.TypeOfAirliner.Helicopter;
            Func<AirlinerType, bool> kind = t => cargo ? t.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo
                : heli ? t.TypeAirliner == AirlinerType.TypeOfAirliner.Helicopter
                : t.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger || t.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed;

            var idle = airline.DeliveredFleet.Where(f => f.Routes.Count == 0 && Usable(f) && f.Airliner.Type != oldType && kind(f.Airliner.Type))
                              .GroupBy(f => f.Airliner.Type).ToDictionary(g => g.Key, g => g.Count());
            var market = Airliners.GetAirlinersForSale(a => a.Status == Airliner.StatusTypes.Normal && a.Type != oldType && kind(a.Type) && a.BuiltDate <= GameObject.GetInstance().GameTime)
                                  .Where(a => airline.Contract == null || airline.Contract.Manufacturer == a.Type.Manufacturer)
                                  .GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.ToList());
            return idle.Keys.Union(market.Keys)
                       .Select(t => Tuple.Create(t, idle.TryGetValue(t, out int i) ? i : 0, market.TryGetValue(t, out var m) ? m.Count : 0,
                                                 market.TryGetValue(t, out var m2) ? m2.Min(a => (double) a.GetPrice()) : 0))
                       .OrderByDescending(x => x.Item2).ThenBy(x => x.Item1.Name).ToList();
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

            foreach (FleetAirliner old in airline.DeliveredFleet.Where(f => f.Airliner.Type == s.OldType).OrderBy(f => f.Name))
            {
                var row = new RenewalRow {Old = old};
                plan.Rows.Add(row);
                string head = old.Name;
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
                Airliner marketNew = idleNew == null && s.BuyMissing && market.Count > 0 ? market.Peek() : null;
                if (idleNew == null && marketNew == null)
                {
                    row.Text = head + ": skipped, no " + s.NewType.Name + " is idle in the fleet" + (s.BuyMissing ? " or for sale on the market" : "") + ".";
                    continue;
                }
                AircraftSpec spec = idleNew != null ? AircraftSpec.Of(idleNew) : AircraftSpec.Of(marketNew);

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

                if (marketNew != null)
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
                row.Ok = true;
            }

            int skipped = plan.Rows.Count(r => !r.Ok);
            string after = s.AfterOld == OldAircraftChoice.Sell ? "sold (leased ones go back)" : s.AfterOld == OldAircraftChoice.ListForSale ? "listed for sale" : "kept idle";
            plan.Summary = plan.Rows.Count == 0
                ? "You have no " + s.OldType.Name + "."
                : plan.Replacing + " of " + plan.Rows.Count + " " + s.OldType.Name + " replaced by " + s.NewType.Name
                  + (plan.Buying > 0 ? " (" + plan.Buying + " " + (s.Lease ? "leased" : "bought") + " for " + Math.Round(plan.Cost).ToString("N0") + ")" : "")
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
            foreach (RenewalRow row in plan.Rows)
            {
                FleetAirliner old = row.Old;
                if (!row.Ok)
                {
                    report.Add(row.Text);
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
    }
}
