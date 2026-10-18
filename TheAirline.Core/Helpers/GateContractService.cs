using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Environment;
using TheAirline.Models.General.Finances;

namespace TheAirline.Helpers
{
    /// <summary>Managing the player's airport gate contracts: extend, auto renew, more or fewer gates, end.</summary>
    public static class GateContractService
    {
        public static readonly int[] Lengths = { 1, 2, 3, 5, 10, 15, 20 };

        /// <summary>All of the airline's airport contracts.</summary>
        public static List<AirportContract> GetContracts(Airline airline)
        {
            return Airports.GetAllAirports()
                           .SelectMany(a => a.AirlineContracts.ToList())
                           .Where(c => c.Airline == airline)
                           .ToList();
        }

        public static string TypeName(AirportContract.ContractType type)
        {
            switch (type)
            {
                case AirportContract.ContractType.Full: return "Gates only";
                case AirportContract.ContractType.LowService: return "Low service";
                case AirportContract.ContractType.MediumService: return "Medium service";
                case AirportContract.ContractType.FullService: return "Full service";
                default: return type.ToString();
            }
        }

        public static int MonthsLeft(AirportContract c)
        {
            var now = GameObject.GetInstance().GameTime;
            return Math.Max(0, (c.ExpireDate.Year - now.Year)*12 + c.ExpireDate.Month - now.Month);
        }

        /// <summary>The penalty for ending a contract early: a tenth of the rent still to pay.</summary>
        public static double EndPenalty(AirportContract c)
        {
            return c.YearlyPayment/12*MonthsLeft(c)/10;
        }

        public static int RoutesUsing(AirportContract c)
        {
            return AirportHelpers.GetAirportRoutes(c.Airport, c.Airline).Count;
        }

        /// <summary>The yearly rent after extending by some years, at today's prices (longer contracts are cheaper per year).</summary>
        public static double ExtendedPayment(AirportContract c, int years)
        {
            if (c.Terminal != null) return c.YearlyPayment;
            return AirportHelpers.GetYearlyContractPayment(c.Airport, c.Type, c.NumberOfGates, years);
        }

        public static bool Extend(AirportContract c, int years, out string message)
        {
            if (years < 1 || years > 30)
            {
                message = "Pick between 1 and 30 years.";
                return false;
            }
            var now = GameObject.GetInstance().GameTime;
            DateTime from = c.ExpireDate > now ? c.ExpireDate : now;
            c.ExpireDate = from.AddYears(years);
            c.Length = years;
            c.YearlyPayment = ExtendedPayment(c, years);
            message = string.Format(
                "The contract at {0} now runs until {1}, at {2} a year.",
                c.Airport.Profile.IATACode, c.ExpireDate.ToShortDateString(), new ValueCurrencyFormatter().Convert(c.YearlyPayment));
            return true;
        }

        public static void SetAutoRenew(AirportContract c, bool on)
        {
            c.AutoRenew = on;
        }

        public static int SetAutoRenewAll(Airline airline, bool on)
        {
            int n = 0;
            foreach (var c in GetContracts(airline).Where(c => c.AutoRenew != on))
            {
                c.AutoRenew = on;
                n++;
            }
            return n;
        }

        /// <summary>The extra yearly rent for more gates on the contract.</summary>
        public static double AddGatesPrice(AirportContract c, int gates)
        {
            return AirportHelpers.GetYearlyContractPayment(c.Airport, c.Type, gates, Math.Max(1, c.Length));
        }

        public static string GetAddGatesBlocker(AirportContract c, int gates)
        {
            if (gates < 1) return "Pick at least one gate.";
            if (c.Terminal != null) return "Gates in your own terminal are added by extending the terminal.";
            int free = c.Airport.Terminals.GetFreeGates(c.TerminalType);
            if (free < gates) return free == 0 ? "There are no free gates at " + c.Airport.Profile.IATACode + "." : "Only " + free + " gate(s) are free.";
            return null;
        }

        public static bool AddGates(AirportContract c, int gates, out string message)
        {
            message = GetAddGatesBlocker(c, gates);
            if (message != null) return false;
            c.YearlyPayment += AddGatesPrice(c, gates);
            c.NumberOfGates += gates;
            MarkGates(c.Airport, c.Airline, c.TerminalType, gates, true);
            message = string.Format("{0} gate(s) added at {1}. The contract now has {2} gates.", gates, c.Airport.Profile.IATACode, c.NumberOfGates);
            return true;
        }

        public static string GetRemoveGatesBlocker(AirportContract c, int gates)
        {
            if (gates < 1) return "Pick at least one gate.";
            if (c.Terminal != null) return "Gates in your own terminal can't be given back.";
            if (gates >= c.NumberOfGates) return "Keep at least one gate, or end the contract instead.";

            // would the flights still fit with fewer gates?
            c.NumberOfGates -= gates;
            try
            {
                var mine = c.Airport.AirlineContracts.Where(x => x.Airline == c.Airline).ToList();
                if (!AirportHelpers.CanFillRoutesEntries(c.Airport, c.Airline, mine, Weather.Season.AllYear))
                    return "Your flights at " + c.Airport.Profile.IATACode + " would no longer fit with fewer gates.";
            }
            finally
            {
                c.NumberOfGates += gates;
            }
            return null;
        }

        public static bool RemoveGates(AirportContract c, int gates, out string message)
        {
            message = GetRemoveGatesBlocker(c, gates);
            if (message != null) return false;
            double share = (double) gates/c.NumberOfGates;
            c.YearlyPayment -= c.YearlyPayment*share;
            c.NumberOfGates -= gates;
            MarkGates(c.Airport, c.Airline, c.TerminalType, gates, false);
            message = string.Format("{0} gate(s) given back at {1}. The contract now has {2} gates.", gates, c.Airport.Profile.IATACode, c.NumberOfGates);
            return true;
        }

        public static string GetEndBlocker(AirportContract c)
        {
            if (c.Terminal != null) return "This contract belongs to your own terminal and can't be ended.";
            var others = c.Airport.AirlineContracts.Where(a => a.Airline == c.Airline && a != c).ToList();
            if (!AirportHelpers.CanFillRoutesEntries(c.Airport, c.Airline, others, Weather.Season.AllYear))
                return "Your flights at " + c.Airport.Profile.IATACode + " would no longer fit. Remove or move those flights first.";
            return null;
        }

        public static bool End(AirportContract c, out string message)
        {
            message = GetEndBlocker(c);
            if (message != null) return false;
            double penalty = EndPenalty(c);
            AirlineHelpers.AddAirlineInvoice(c.Airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -penalty);
            c.Airport.RemoveAirlineContract(c);
            MarkGates(c.Airport, c.Airline, c.TerminalType, c.NumberOfGates, false);
            message = "Contract at " + c.Airport.Profile.IATACode + " ended (penalty " + new ValueCurrencyFormatter().Convert(penalty) + ").";
            return true;
        }

        // gates carry the airline that uses them; the free gate count itself comes from the contracts
        private static void MarkGates(Airport airport, Airline airline, Terminal.TerminalType type, int count, bool take)
        {
            var gates = airport.Terminals.GetDeliveredTerminals()
                               .Where(t => t.Type == type)
                               .SelectMany(t => t.Gates.GetGates())
                               .ToList();
            if (gates.Count == 0) gates = airport.Terminals.GetGates();
            for (int i = 0; i < count; i++)
            {
                var gate = take ? gates.FirstOrDefault(g => g.Airline == null) : gates.LastOrDefault(g => g.Airline == airline);
                if (gate == null) break;
                gate.Airline = take ? airline : null;
            }
        }

        /// <summary>A new contract with the chosen number of gates and years (the route creator signs 2 gates for 2 years).</summary>
        public static bool Sign(Airline airline, Airport airport, AirportContract.ContractType type, Terminal.TerminalType terminalType, int gates, int years, out string message)
        {
            int free = airport.Terminals.GetFreeGates(terminalType);
            if (gates < 1 || free < gates)
            {
                message = free == 0 ? "There are no free gates at " + airport.Profile.IATACode + "." : "Only " + free + " gate(s) are free.";
                return false;
            }
            var now = GameObject.GetInstance().GameTime;
            if (type == AirportContract.ContractType.Full)
            {
                bool cargo = terminalType == Terminal.TerminalType.Cargo;
                var facilityType = cargo ? AirportFacility.FacilityType.Cargo : AirportFacility.FacilityType.CheckIn;
                if (airport.GetAirportFacility(airline, facilityType, cargo).TypeLevel == 0)
                {
                    var basic = AirportFacilities.GetFacilities(facilityType).Find(f => f.TypeLevel == 1);
                    airport.AddAirportFacility(airline, basic, now);
                    AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -basic.Price);
                }
            }
            double yearly = AirportHelpers.GetYearlyContractPayment(airport, type, gates, years);
            AirportHelpers.AddAirlineContract(new AirportContract(airline, airport, type, terminalType, now, gates, years, yearly, true));
            MarkGates(airport, airline, terminalType, gates, true);
            message = string.Format(
                "Leased {0} gate(s) at {1} for {2} year(s), {3} a year. It renews by itself.",
                gates, airport.Profile.IATACode, years, new ValueCurrencyFormatter().Convert(yearly));
            return true;
        }
    }
}
