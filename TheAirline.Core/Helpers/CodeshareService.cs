using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airlines.Subsidiary;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Codeshare agreements for the player. A partner gets 1% of the ticket income on the other airline's flights
    ///     (one way: only the airline sharing its routes pays), and both airlines get more passengers at airports
    ///     the partner flies to.
    /// </summary>
    public static class CodeshareService
    {
        private static readonly Dictionary<Airline, DateTime> Refusals = new Dictionary<Airline, DateTime>();
        public const int RefusalDays = 90;

        public static Airline Partner(CodeshareAgreement c, Airline me)
        {
            return c.Airline1 == me ? c.Airline2 : c.Airline1;
        }

        public static CodeshareAgreement Find(Airline a, Airline b)
        {
            return a.Codeshares.FirstOrDefault(c => (c.Airline1 == a && c.Airline2 == b) || (c.Airline1 == b && c.Airline2 == a));
        }

        public static string Describe(CodeshareAgreement c, Airline me)
        {
            if (c.Type == CodeshareAgreement.CodeshareType.BothWays) return "Both ways: each airline sells the other's flights";
            return c.Airline1 == me ? "One way: the partner sells your flights" : "One way: you sell the partner's flights";
        }

        /// <summary>The one-time fee for signing (the game's codeshare price).</summary>
        public static double SigningFee(Airline partner, Airline me)
        {
            return AirlineHelpers.GetCodesharingPrice(partner, me);
        }

        /// <summary>Why the partner would say no, or an empty list when it would say yes. Follows the game's own rules.</summary>
        public static List<string> Reasons(Airline partner, Airline asker, CodeshareAgreement.CodeshareType type)
        {
            var reasons = new List<string>();
            double coeff = type == CodeshareAgreement.CodeshareType.OneWay ? 0.25 : 0.40;

            var partnerCountries = partner.Airports.Select(a => a.Profile.Country).Distinct().ToList();
            var askerCountries = asker.Airports.Select(a => a.Profile.Country).Distinct().ToList();
            int sameCountries = askerCountries.Intersect(partnerCountries).Count();
            int sameAirports = asker.Airports.Distinct().Intersect(partner.Airports).Count();
            int partnerCodeshareCountries = partner.GetCodesharingAirlines().SelectMany(a => a.Airports)
                                                   .Select(a => a.Profile.Country).Distinct().Intersect(partnerCountries).Count();

            if (partner.Routes.Count > 3*asker.Routes.Count)
                reasons.Add(string.Format("You are too small for them: they fly {0} routes, you need at least {1}.", partner.Routes.Count, (int) Math.Ceiling(partner.Routes.Count/3.0)));
            if (partner.Airports.Count > 0 && sameAirports >= partner.Airports.Count*coeff)
                reasons.Add(string.Format("You share too many airports ({0} of their {1}); they want partners that add new places.", sameAirports, partner.Airports.Count));
            if (partnerCountries.Count > 0 && sameCountries >= partnerCountries.Count*0.75)
                reasons.Add(string.Format("You fly to most of the same countries ({0} of their {1}).", sameCountries, partnerCountries.Count));
            if (partnerCountries.Count > 0 && partnerCodeshareCountries >= partnerCountries.Count*coeff && partner.Codeshares.Count > 0)
                reasons.Add("Their other codeshare partners already cover those countries.");
            return reasons;
        }

        /// <summary>Why a proposal can't even be made, or null.</summary>
        public static string GetBlocker(Airline me, Airline partner)
        {
            if (partner == null) return "Pick an airline.";
            if (partner == me) return "That is your own airline.";
            if (Find(me, partner) != null) return "You already codeshare with " + partner.Profile.Name + ".";
            if (partner is SubsidiaryAirline sub && sub.Airline == me) return "That is your own subsidiary.";
            if (me.Routes.Count == 0) return "Open some routes first; there is nothing to share yet.";
            if (Refusals.TryGetValue(partner, out var when) && (GameObject.GetInstance().GameTime - when).TotalDays < RefusalDays)
                return partner.Profile.Name + " said no recently. Try again after " + when.AddDays(RefusalDays).ToShortDateString() + ".";
            return null;
        }

        public static bool Propose(Airline me, Airline partner, CodeshareAgreement.CodeshareType type, out string message)
        {
            message = GetBlocker(me, partner);
            if (message != null) return false;

            var reasons = Reasons(partner, me, type);
            if (reasons.Count > 0 || !AirlineHelpers.AcceptCodesharing(partner, me, type))
            {
                Refusals[partner] = GameObject.GetInstance().GameTime;
                message = partner.Profile.Name + " said no. " + string.Join(" ", reasons);
                return false;
            }

            double fee = SigningFee(partner, me);
            AirlineHelpers.AddAirlineInvoice(me, GameObject.GetInstance().GameTime, Invoice.InvoiceType.AirlineExpenses, -fee);
            // the partner shares its routes with you (one way), or both share (both ways)
            var agreement = new CodeshareAgreement(partner, me, type);
            partner.AddCodeshareAgreement(agreement);
            me.AddCodeshareAgreement(agreement);
            message = partner.Profile.Name + " agreed to a codeshare (" + Describe(agreement, me).ToLower() + ").";
            return true;
        }

        public static void End(Airline me, CodeshareAgreement c)
        {
            c.Airline1.RemoveCodeshareAgreement(c);
            c.Airline2.RemoveCodeshareAgreement(c);
        }

        /// <summary>Airports both airlines fly to.</summary>
        public static int SharedAirports(Airline a, Airline b)
        {
            return a.Airports.Intersect(b.Airports).Count();
        }

        /// <summary>The partner's routes that touch one of your airports: these bring you more passengers there.</summary>
        public static int PartnerRoutesAtMyAirports(Airline me, Airline partner)
        {
            var mine = new HashSet<Airport>(me.Airports);
            return partner.Routes.ToList().Count(r => mine.Contains(r.Destination1) || mine.Contains(r.Destination2));
        }

        /// <summary>What you got from the partner last month (1% of its ticket income when it shares its routes with you).</summary>
        public static double MonthlyIncome(Airline me, CodeshareAgreement c)
        {
            var partner = Partner(c, me);
            bool partnerShares = c.Airline1 == partner || c.Type == CodeshareAgreement.CodeshareType.BothWays;
            if (!partnerShares) return 0;
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return partner.GetInvoicesAmountMonth(last.Year, last.Month, Invoice.InvoiceType.Tickets)*CodeshareAgreement.TicketSalePercent/100;
        }

        /// <summary>What you paid the partner last month (1% of your ticket income when you share your routes with it).</summary>
        public static double MonthlyCost(Airline me, CodeshareAgreement c)
        {
            bool iShare = c.Airline1 == me || c.Type == CodeshareAgreement.CodeshareType.BothWays;
            if (!iShare) return 0;
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return me.GetInvoicesAmountMonth(last.Year, last.Month, Invoice.InvoiceType.Tickets)*CodeshareAgreement.TicketSalePercent/100;
        }

        /// <summary>Airlines worth asking, best first: many routes at your airports and few shared airports.</summary>
        public static List<Airline> Candidates(Airline me)
        {
            return Airlines.GetAllAirlines()
                           .Where(a => a != me && !a.IsHuman && Find(me, a) == null && !(a is SubsidiaryAirline s && s.Airline == me))
                           .OrderByDescending(a => PartnerRoutesAtMyAirports(me, a) - SharedAirports(me, a))
                           .ThenByDescending(a => a.Routes.Count)
                           .ToList();
        }

        // ---- offers from computer airlines ----

        public static List<CodeshareAgreement> Offers(Airline me)
        {
            return me.CodeshareOffers ?? (me.CodeshareOffers = new List<CodeshareAgreement>());
        }

        /// <summary>A computer airline asks the player for a codeshare (kept until the player answers).</summary>
        public static void AddOffer(Airline me, Airline asker)
        {
            var offers = Offers(me);
            if (Find(me, asker) != null || offers.Any(o => o.Airline1 == asker || o.Airline2 == asker)) return;
            offers.Add(new CodeshareAgreement(me, asker, CodeshareAgreement.CodeshareType.BothWays));
            while (offers.Count > 5) offers.RemoveAt(0);
            GameObject.GetInstance().NewsBox.AddNews(
                new News(
                    News.NewsType.AllianceNews,
                    GameObject.GetInstance().GameTime,
                    "Codeshare offer",
                    asker.Profile.Name + " has asked you for a codeshare agreement. Answer it under Airlines > Codeshares."));
        }

        public static void AnswerOffer(Airline me, CodeshareAgreement offer, bool accept, out string message)
        {
            var partner = Partner(offer, me);
            Offers(me).Remove(offer);
            if (!accept)
            {
                message = "You turned down " + partner.Profile.Name + ".";
                return;
            }
            if (Find(me, partner) != null)
            {
                message = "You already codeshare with " + partner.Profile.Name + ".";
                return;
            }
            offer.Airline1.AddCodeshareAgreement(offer);
            offer.Airline2.AddCodeshareAgreement(offer);
            message = "You now codeshare with " + partner.Profile.Name + ".";
        }
    }
}
