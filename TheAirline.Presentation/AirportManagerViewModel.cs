using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Environment;
using TheAirline.Models.General.Finances;

namespace TheAirline.Presentation
{
    public class ContractItem
    {
        public AirportContract Contract { get; set; }
        public string Text { get; set; }
    }

    public class FacilityItem
    {
        public AirportFacility.FacilityType Type { get; set; }
        public string Name { get; set; }
        public string Current { get; set; }
        public string Next { get; set; }
        public string Building { get; set; }
        public bool CanBuy { get; set; }
        public ICommand BuyCommand { get; set; }
    }

    /// <summary>Airports tab: your airports (or any airport you search for), gate contracts and facilities.</summary>
    public class AirportManagerViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _search = "";
        private AirportItem _selected;
        private List<Airport> _all;
        private string _status;
        private ContractChoice _contractType;
        private int _gates = 1;
        private bool _confirmEnd;
        private ContractItem _selectedContract;

        public AirportManagerViewModel(Action changed)
        {
            _changed = changed;
            Airports = new ObservableCollection<AirportItem>();
            Contracts = new ObservableCollection<ContractItem>();
            Facilities = new ObservableCollection<FacilityItem>();
            GateChoices = new ObservableCollection<int>();
            ContractTypes = Enum.GetValues(typeof(AirportContract.ContractType))
                                .Cast<AirportContract.ContractType>()
                                .Select(t => new ContractChoice { Type = t, Text = t.ToString() })
                                .ToList();
            _contractType = ContractTypes.First();

            LeaseCommand = new RelayCommand(Lease, () => _selected != null);
            EndContractCommand = new RelayCommand(() => ConfirmEnd = true, () => _selectedContract != null);
            ConfirmEndCommand = new RelayCommand(EndContract);
            CancelEndCommand = new RelayCommand(() => ConfirmEnd = false);

            ApplySearch();
            Selected = Airports.FirstOrDefault();
        }

        public ObservableCollection<AirportItem> Airports { get; }
        public ObservableCollection<ContractItem> Contracts { get; }
        public ObservableCollection<FacilityItem> Facilities { get; }
        public ObservableCollection<int> GateChoices { get; }
        public List<ContractChoice> ContractTypes { get; }

        public ICommand LeaseCommand { get; }
        public ICommand EndContractCommand { get; }
        public ICommand ConfirmEndCommand { get; }
        public ICommand CancelEndCommand { get; }

        public string Search
        {
            get { return _search; }
            set
            {
                if (SetProperty(ref _search, value ?? ""))
                {
                    ApplySearch();
                }
            }
        }

        public AirportItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    ConfirmEnd = false;
                    LoadDetail();
                    (LeaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Title { get; private set; }
        public string Info { get; private set; }
        public string GateSummary { get; private set; }

        public ContractChoice ContractType
        {
            get { return _contractType; }
            set { SetProperty(ref _contractType, value); OnPropertyChanged(nameof(PriceText)); }
        }

        public int Gates
        {
            get { return _gates; }
            set { SetProperty(ref _gates, value); OnPropertyChanged(nameof(PriceText)); }
        }

        public string PriceText
        {
            get
            {
                if (_selected == null || _contractType == null || _gates < 1)
                {
                    return "";
                }

                double yearly = AirportHelpers.GetYearlyContractPayment(_selected.Airport, _contractType.Type, _gates, 2);
                return "Yearly payment for a 2 year contract: " + Money(yearly);
            }
        }

        public ContractItem SelectedContract
        {
            get { return _selectedContract; }
            set
            {
                if (SetProperty(ref _selectedContract, value))
                {
                    ConfirmEnd = false;
                    (EndContractCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(EndText));
                }
            }
        }

        public bool ConfirmEnd
        {
            get { return _confirmEnd; }
            private set
            {
                if (SetProperty(ref _confirmEnd, value))
                {
                    OnPropertyChanged(nameof(NotConfirmEnd));
                    OnPropertyChanged(nameof(EndText));
                }
            }
        }

        public bool NotConfirmEnd
        {
            get { return !_confirmEnd; }
        }

        public string EndText
        {
            get
            {
                return _selectedContract == null
                    ? ""
                    : "Ending this contract costs a penalty of " + Money(Penalty(_selectedContract.Contract)) + ". Continue?";
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static Airline Human
        {
            get { return GameObject.GetInstance().HumanAirline; }
        }

        private static double Penalty(AirportContract c)
        {
            return ((c.YearlyPayment / 12) * c.MonthsLeft) / 10;
        }

        /// <summary>Refreshes the lists after something changed elsewhere (a route, an aircraft).</summary>
        public void Reload()
        {
            LoadDetail();
        }

        private void ApplySearch()
        {
            var own = new HashSet<Airport>(Human.Airports);
            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Airport> list;
            string s = _search.Trim();
            if (s.Length == 0)
            {
                list = Human.Airports.OrderBy(a => a.Profile.Name);
            }
            else
            {
                list = _all.Where(
                    a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (a.Profile.Country != null
                             && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            Airports.Clear();
            foreach (var a in list.Take(150))
            {
                Airports.Add(new AirportItem(a, own.Contains(a)));
            }
        }

        private void LoadDetail()
        {
            Contracts.Clear();
            Facilities.Clear();
            GateChoices.Clear();

            if (_selected == null)
            {
                Title = Info = GateSummary = "";
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Info));
                OnPropertyChanged(nameof(GateSummary));
                return;
            }

            var airport = _selected.Airport;
            var airline = Human;
            var now = GameObject.GetInstance().GameTime;

            Title = airport.Profile.IATACode + "  " + airport.Profile.Name;
            Info = string.Format(
                "{0}, {1}  |  size {2}  |  longest runway {3} m",
                airport.Profile.Town?.Name, airport.Profile.Country?.Name, airport.Profile.Size, airport.GetMaxRunwayLength());

            int total = airport.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
            int free = airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            int mine = airport.GetAirlineContracts(airline)
                              .Where(c => c.TerminalType == Terminal.TerminalType.Passenger)
                              .Sum(c => c.NumberOfGates);
            GateSummary = string.Format("Passenger gates: {0} in total, {1} free, {2} leased by you", total, free, mine);

            foreach (var c in airport.GetAirlineContracts(airline))
            {
                Contracts.Add(
                    new ContractItem
                    {
                        Contract = c,
                        Text = string.Format(
                            "{0}  |  {1} gate(s) ({2})  |  {3} months left  |  {4}/year",
                            c.Type, c.NumberOfGates, c.TerminalType, c.MonthsLeft, Money(c.YearlyPayment))
                    });
            }

            for (int i = 1; i <= Math.Min(free, 8); i++)
            {
                GateChoices.Add(i);
            }

            _gates = GateChoices.Count > 0 ? GateChoices[0] : 0;
            OnPropertyChanged(nameof(Gates));
            OnPropertyChanged(nameof(PriceText));

            // facilities can only be bought where the airline has a contract
            bool hasContract = Contracts.Count > 0;
            foreach (AirportFacility.FacilityType type in Enum.GetValues(typeof(AirportFacility.FacilityType)))
            {
                if (type == AirportFacility.FacilityType.Cargo)
                {
                    continue;
                }

                var current = airport.GetCurrentAirportFacility(airline, type);
                var building = airport.GetAirlineBuildingFacility(airline, type);
                var next = AirportFacilities.GetFacilities(type)
                                            .Where(f => f.TypeLevel > (current?.TypeLevel ?? 0))
                                            .OrderBy(f => f.TypeLevel)
                                            .FirstOrDefault();

                double price = next == null ? 0 : FacilityPrice(airport, next);
                var captured = next;

                Facilities.Add(
                    new FacilityItem
                    {
                        Type = type,
                        Name = type.ToString(),
                        Current = current == null || current.TypeLevel == 0 ? "none" : current.Name,
                        Next = next == null ? "(best level reached)" : next.Name + " for " + Money(price),
                        Building = building == null ? null : "building " + building.Name,
                        CanBuy = hasContract && next != null && building == null,
                        BuyCommand = new RelayCommand(() => BuyFacility(airport, captured))
                    });
            }

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(GateSummary));
        }

        private static double FacilityPrice(Airport airport, AirportFacility facility)
        {
            double price = facility.Price;
            if (airport.Profile.Country != Human.Profile.Country)
            {
                price *= 1.25;
            }

            return price;
        }

        private void BuyFacility(Airport airport, AirportFacility facility)
        {
            var airline = Human;
            double price = FacilityPrice(airport, facility);

            if (airport.GetAirlineBuildingFacility(airline, facility.Type) != null)
            {
                Status = "A " + facility.Type + " facility is already being built here.";
                return;
            }

            if (price > airline.Money)
            {
                Status = "You can't afford " + facility.Name + " (" + Money(price) + ").";
                return;
            }

            AirlineHelpers.AddAirlineInvoice(
                airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);
            airport.AddAirportFacility(airline, facility, GameObject.GetInstance().GameTime.AddDays(facility.BuildingDays));

            Status = facility.Name + " ordered, ready in " + facility.BuildingDays + " days.";
            LoadDetail();
            _changed();
        }

        private void Lease()
        {
            if (_selected == null)
            {
                return;
            }

            var airport = _selected.Airport;
            var airline = Human;

            if (_gates < 1 || airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger) < _gates)
            {
                Status = "There are no free gates to lease at " + airport.Profile.IATACode + ".";
                return;
            }

            double yearly = AirportHelpers.GetYearlyContractPayment(airport, _contractType.Type, _gates, 2);

            if (RouteCreator.NeedsContract(airport))
            {
                // new contract (takes up to two gates, like the route creator does)
                RouteCreator.SignContract(airport, _contractType.Type);
                Status = "Gates leased at " + airport.Profile.IATACode + ".";
            }
            else
            {
                AirportHelpers.RentGates(
                    airport, airline, _contractType.Type, Terminal.TerminalType.Passenger, _gates, 2);
                Status = _gates + " more gate(s) leased at " + airport.Profile.IATACode + " (" + Money(yearly) + "/year).";
            }

            ApplySearch();
            Selected = Airports.FirstOrDefault(a => a.Airport == airport);
            LoadDetail();
            _changed();
        }

        private void EndContract()
        {
            var item = _selectedContract;
            if (item == null || _selected == null)
            {
                return;
            }

            var airport = _selected.Airport;
            var airline = Human;
            var contract = item.Contract;
            double penalty = Penalty(contract);

            var others = airport.AirlineContracts.Where(a => a.Airline == airline && a != contract).ToList();
            if (!AirportHelpers.CanFillRoutesEntries(airport, airline, others, Weather.Season.AllYear))
            {
                Status = "You can't end this contract: your flights at " + airport.Profile.IATACode
                         + " would no longer fit. Remove or move those flights first.";
                ConfirmEnd = false;
                return;
            }

            if (contract.Terminal != null)
            {
                Status = "This contract belongs to your own terminal and can't be ended here.";
                ConfirmEnd = false;
                return;
            }

            AirlineHelpers.AddAirlineInvoice(
                airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -penalty);
            airport.RemoveAirlineContract(contract);

            for (int i = 0; i < contract.NumberOfGates; i++)
            {
                var gate = airport.Terminals.GetGates().FirstOrDefault(g => g.Airline == airline);
                if (gate != null)
                {
                    gate.Airline = null;
                }
            }

            Status = "Contract ended (penalty " + Money(penalty) + ").";
            ConfirmEnd = false;
            ApplySearch();
            Selected = Airports.FirstOrDefault(a => a.Airport == airport);
            LoadDetail();
            _changed();
        }
    }
}
