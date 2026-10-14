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
    /// <summary>
    ///     Buying gates from the computer airlines: an airline gives up gates it doesn't need for its flights,
    ///     and the buyer pays it a transfer fee and gets a normal gate contract for them.
    /// </summary>
    public static class GateTrading
    {
        //a gate is sold for one and a half years of the standard gate rent, to the airline that gives it up
        public static double GetPricePerGate(Airport airport)
        {
            return 1.5 * AirportHelpers.GetYearlyContractPayment(airport, AirportContract.ContractType.Full, 1, 2);
        }

        //true when the contract is one that can be bought from: a computer airline's passenger gates, no own terminal
        public static bool IsTradable(Airport airport, AirportContract contract)
        {
            return contract != null
                   && !contract.Airline.IsHuman
                   && contract.TerminalType == Terminal.TerminalType.Passenger
                   && contract.Terminal == null
                   && !contract.IsExclusiveDeal
                   && contract.NumberOfGates > 0
                   && airport.AirlineContracts.Contains(contract)
                   && !airport.HasHub(contract.Airline);
        }

        //how many of the contract's gates the airline can give up and still fly everything it flies from the airport
        public static int GetSpareGates(Airport airport, AirportContract contract)
        {
            if (!IsTradable(airport, contract))
            {
                return 0;
            }

            Airline seller = contract.Airline;

            int total = airport.GetAirlineContracts(seller)
                               .Where(c => c.TerminalType == Terminal.TerminalType.Passenger)
                               .Sum(c => c.NumberOfGates);

            int ownedGates = airport.Terminals.GetGates().Count(g => g.Airline == seller);

            int spare = 0;

            try
            {
                bool hasRoutes = AirportHelpers.GetAirportRoutes(airport, seller).Count > 0;

                for (int n = 1; n <= Math.Min(contract.NumberOfGates, ownedGates); n++)
                {
                    int left = total - n;

                    if (left == 0 && hasRoutes)
                    {
                        break;
                    }

                    var remaining = new List<AirportContract>
                    {
                        new AirportContract(
                            seller,
                            airport,
                            AirportContract.ContractType.Full,
                            Terminal.TerminalType.Passenger,
                            GameObject.GetInstance().GameTime,
                            left,
                            1,
                            0,
                            false)
                    };

                    if (left > 0 && !AirportHelpers.CanFillRoutesEntries(airport, seller, remaining, Weather.Season.AllYear))
                    {
                        break;
                    }

                    spare = n;
                }
            }
            catch (Exception)
            {
                // an airline with an unusual timetable is simply not selling
                return 0;
            }

            return spare;
        }

        //buys gates from a computer airline; returns false with a message that says why not
        public static bool BuyGates(
            Airport airport,
            AirportContract sellerContract,
            int gates,
            AirportContract.ContractType type,
            out string message)
        {
            Airline buyer = GameObject.GetInstance().HumanAirline;
            DateTime now = GameObject.GetInstance().GameTime;

            if (gates < 1)
            {
                message = "Pick how many gates you want to buy.";
                return false;
            }

            int spare = GetSpareGates(airport, sellerContract);

            if (spare < gates)
            {
                message = spare == 0
                              ? sellerContract.Airline.Profile.Name + " needs all of its gates for its flights."
                              : sellerContract.Airline.Profile.Name + " can only spare " + spare + " gate(s).";
                return false;
            }

            double price = GetPricePerGate(airport) * gates;

            if (price > buyer.Money)
            {
                message = "You can't afford this: the gates cost " + price.ToString("N0") + ".";
                return false;
            }

            Airline seller = sellerContract.Airline;

            //take the gates from the seller
            var taken = airport.Terminals.GetGates().Where(g => g.Airline == seller).Take(gates).ToList();

            if (taken.Count < gates)
            {
                message = seller.Profile.Name + " doesn't hold that many gates.";
                return false;
            }

            double perGateRent = sellerContract.YearlyPayment / sellerContract.NumberOfGates;
            sellerContract.NumberOfGates -= gates;
            sellerContract.YearlyPayment = perGateRent * sellerContract.NumberOfGates;

            if (sellerContract.NumberOfGates <= 0)
            {
                airport.RemoveAirlineContract(sellerContract);
            }

            AirlineHelpers.AddAirlineInvoice(buyer, now, Invoice.InvoiceType.Purchases, -price);
            AirlineHelpers.AddAirlineInvoice(seller, now, Invoice.InvoiceType.Rents, price);

            //the buyer gets a normal two year contract for them
            bool hasCheckin = airport.GetAirportFacility(buyer, AirportFacility.FacilityType.CheckIn).TypeLevel > 0;

            if (!hasCheckin && type == AirportContract.ContractType.Full)
            {
                AirportFacility checkin =
                    AirportFacilities.GetFacilities(AirportFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);

                airport.AddAirportFacility(buyer, checkin, now);
                AirlineHelpers.AddAirlineInvoice(buyer, now, Invoice.InvoiceType.Purchases, -checkin.Price);
            }

            var contract = new AirportContract(
                buyer,
                airport,
                type,
                Terminal.TerminalType.Passenger,
                now,
                gates,
                2,
                AirportHelpers.GetYearlyContractPayment(airport, type, gates, 2),
                true);

            AirportHelpers.AddAirlineContract(contract);

            foreach (Gate gate in taken)
            {
                gate.Airline = buyer;
            }

            message = string.Format("Bought {0} gate(s) from {1} for {2:N0}.", gates, seller.Profile.Name, price);
            return true;
        }
    }
}
