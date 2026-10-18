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
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    /// <summary>One row of the airport list, with the columns of the Windows airport list and a quick lease button.</summary>
    public class AirportRow : AirportItem
    {
        public AirportRow(Airport airport, bool own) : base(airport, own)
        {
            var p = airport.Profile;
            Name = p.Name;
            Codes = p.IATACode + "/" + p.ICAOCode;
            Town = p.Town?.Name;
            Country = p.Country?.Name;
            Type = System.Text.RegularExpressions.Regex.Replace(p.Type.ToString().Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
            Size = p.Size.ToString().Replace('_', ' ');
            int total = airport.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
            FreeGates = airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            Gates = FreeGates + "/" + total;
            Runway = airport.GetMaxRunwayLength();
            Airlines = airport.AirlineContracts.Select(c => c.Airline).Distinct().Count();
            Routes = AirportHelpers.GetAirportRoutes(airport).Count;
            CanLease = !own && FreeGates > 0;
        }

        public string Name { get; }
        public string Codes { get; }
        public string Town { get; }
        public string Country { get; }
        public string Type { get; }
        public string Size { get; }
        public int FreeGates { get; }
        public string Gates { get; }
        public long Runway { get; }
        public int Airlines { get; }
        public int Routes { get; }
        public bool CanLease { get; }
        public ICommand LeaseCommand { get; set; }
    }

    public class ContractItem
    {
        public AirportContract Contract { get; set; }
        public string Text { get; set; }
    }

    /// <summary>A computer airline that has gates it doesn't need at the airport.</summary>
    public class GateSaleOffer
    {
        public AirportContract Contract { get; set; }
        public string Text { get; set; }
        public string BuyOneText { get; set; }
        public string BuyAllText { get; set; }
        public bool CanBuyAll { get; set; }
        public ICommand BuyOneCommand { get; set; }
        public ICommand BuyAllCommand { get; set; }
    }

    public class TerminalItem
    {
        public Terminal Terminal { get; set; }
        public string Text { get; set; }
        public bool CanBuy { get; set; }
        public bool CanSell { get; set; }
        public string ActionText { get; set; }
        public ICommand BuyCommand { get; set; }
        public ICommand SellCommand { get; set; }
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
    /// <summary>A hub of some airline at the selected airport.</summary>
    public class HubItem
    {
        public string Airline { get; set; }
        public string Type { get; set; }
        public string Effect { get; set; }
        public bool IsMine { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    /// <summary>A kind of hub the airline could create at the selected airport.</summary>
    public class HubOffer
    {
        public HubType Type { get; set; }
        public string Name { get; set; }
        public string Price { get; set; }
        public string Effect { get; set; }
        public string Reason { get; set; }
        public bool CanBuild { get; set; }
        public ICommand BuyCommand { get; set; }
    }

    public class AirportManagerViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _search = "";
        private bool _onlyMine;
        private string _sortBy = "Name";
        private AirportItem _selected;
        private List<Airport> _all;
        private string _status;
        private ContractChoice _contractType;
        private int _gates = 1;
        private bool _confirmEnd;
        private ContractItem _selectedContract;
        private string _terminalName = "Terminal";
        private int _terminalGates = 10;
        private bool _terminalCargo;
        private bool _leaseCargo;
        private Action _pending;
        private string _pendingText;

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
            Terminals = new ObservableCollection<TerminalItem>();
            Hubs = new ObservableCollection<HubItem>();
            HubOffers = new ObservableCollection<HubOffer>();
            TerminalGateChoices = new List<int> { 5, 10, 15, 20, 30, 40, 50 };
            BuildTerminalCommand = new RelayCommand(AskBuildTerminal, () => _selected != null);
            ConfirmPendingCommand = new RelayCommand(
                () =>
                    {
                        var action = _pending;
                        PendingText = null;
                        action?.Invoke();
                    });
            CancelPendingCommand = new RelayCommand(() => PendingText = null);
            CancelEndCommand = new RelayCommand(() => ConfirmEnd = false);

            ApplySearch();
            Selected = Airports.FirstOrDefault(a => a.IsOwn) ?? Airports.FirstOrDefault();
        }

        public ObservableCollection<AirportItem> Airports { get; }
        public ObservableCollection<ContractItem> Contracts { get; }
        public ObservableCollection<HubItem> Hubs { get; }
        public ObservableCollection<HubOffer> HubOffers { get; }
        public string HubText { get; private set; }
        public bool HasHubs => Hubs.Count > 0;
        public bool HasHubOffers => HubOffers.Count > 0;
        public ObservableCollection<GateSaleOffer> GateOffers { get; } = new ObservableCollection<GateSaleOffer>();
        public bool HasGateOffers => GateOffers.Count > 0;
        public string GateOffersText { get; private set; }
        public ObservableCollection<FacilityItem> Facilities { get; }
        public ObservableCollection<int> GateChoices { get; }
        public ObservableCollection<TerminalItem> Terminals { get; }
        public List<int> TerminalGateChoices { get; }
        public ICommand BuildTerminalCommand { get; }
        public ICommand ConfirmPendingCommand { get; }
        public ICommand CancelPendingCommand { get; }

        public string TerminalName
        {
            get { return _terminalName; }
            set { SetProperty(ref _terminalName, value); }
        }

        public int TerminalGates
        {
            get { return _terminalGates; }
            set { SetProperty(ref _terminalGates, value); OnPropertyChanged(nameof(TerminalPriceText)); }
        }

        /// <summary>Lease cargo gates instead of passenger gates.</summary>
        public bool LeaseCargo
        {
            get { return _leaseCargo; }
            set
            {
                if (SetProperty(ref _leaseCargo, value))
                {
                    if (_selected != null)
                    {
                        RefreshGateChoices(_selected.Airport);
                    }

                    OnPropertyChanged(nameof(LeaseHeader));
                }
            }
        }

        public string LeaseHeader
        {
            get { return _leaseCargo ? "Lease cargo gates" : "Lease passenger gates"; }
        }

        private Terminal.TerminalType LeaseKind
        {
            get { return _leaseCargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger; }
        }

        private void RefreshGateChoices(Airport airport)
        {
            GateChoices.Clear();
            int free = airport.Terminals.GetFreeGates(LeaseKind);
            for (int i = 1; i <= Math.Min(free, 8); i++)
            {
                GateChoices.Add(i);
            }

            _gates = GateChoices.Count > 0 ? GateChoices[0] : 0;
            OnPropertyChanged(nameof(Gates));
            OnPropertyChanged(nameof(PriceText));
        }

        /// <summary>Build a cargo terminal instead of a passenger terminal.</summary>
        public bool TerminalCargo
        {
            get { return _terminalCargo; }
            set { SetProperty(ref _terminalCargo, value); }
        }

        public string TerminalPriceText
        {
            get
            {
                if (_selected == null)
                {
                    return "";
                }

                var a = _selected.Airport;
                return string.Format(
                    "Price {0}, ready in {1} days. Your own terminal gives you gates at a lower yearly cost once it is finished.",
                    Money(TerminalPrice(a, _terminalGates)), _terminalGates * 10 + 60);
            }
        }

        /// <summary>A question to confirm (build, buy, sell); empty when nothing is pending.</summary>
        public string PendingText
        {
            get { return _pendingText; }
            private set
            {
                if (SetProperty(ref _pendingText, value))
                {
                    if (value == null)
                    {
                        _pending = null;
                    }

                    OnPropertyChanged(nameof(HasPending));
                }
            }
        }

        public bool HasPending
        {
            get { return _pendingText != null; }
        }
        public List<ContractChoice> ContractTypes { get; }

        public ICommand LeaseCommand { get; }
        public ICommand EndContractCommand { get; }
        public ICommand ConfirmEndCommand { get; }
        public ICommand CancelEndCommand { get; }

        /// <summary>Show only the airports where you have a contract.</summary>
        public bool OnlyMine
        {
            get { return _onlyMine; }
            set
            {
                if (SetProperty(ref _onlyMine, value))
                {
                    ApplySearch();
                }
            }
        }

        public List<string> SortChoices { get; } =
            new List<string> { "Name", "Country", "Size", "Runway", "Free gates", "Airlines", "Routes" };

        public string SortBy
        {
            get { return _sortBy; }
            set
            {
                if (SetProperty(ref _sortBy, value ?? "Name"))
                {
                    ApplySearch();
                }
            }
        }

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
                    PendingText = null;
                    LoadDetail();
                    (LeaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (BuildTerminalCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Title { get; private set; }
        public string Info { get; private set; }

        /// <summary>The airport categories: Regional, Domestic and the two international kinds.</summary>
        public List<NamedChoice<AirportProfile.AirportType>> AirportTypes { get; } = new List<NamedChoice<AirportProfile.AirportType>>
        {
            new NamedChoice<AirportProfile.AirportType> { Name = "Regional", Value = AirportProfile.AirportType.Regional },
            new NamedChoice<AirportProfile.AirportType> { Name = "Domestic", Value = AirportProfile.AirportType.Domestic },
            new NamedChoice<AirportProfile.AirportType> { Name = "Short haul international", Value = AirportProfile.AirportType.ShortHaulInternational },
            new NamedChoice<AirportProfile.AirportType> { Name = "Long haul international", Value = AirportProfile.AirportType.LongHaulInternational }
        };

        private NamedChoice<AirportProfile.AirportType> _airportType;
        public bool HasSelection => _selected != null;

        public NamedChoice<AirportProfile.AirportType> AirportType
        {
            get { return _airportType; }
            set
            {
                if (value == null || _selected == null || _airportType == value) return;
                var airport = _selected.Airport;
                if (airport.Profile.Type == value.Value) { SetProperty(ref _airportType, value); return; }
                airport.Profile.Type = value.Value;
                SetProperty(ref _airportType, value);
                ApplySearch();
                Selected = Airports.FirstOrDefault(a => a.Airport == airport);
                Status = airport.Profile.IATACode + " is now a " + value.Name.ToLower() + " airport.";
                _changed();
            }
        }
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

            IEnumerable<Airport> list = _onlyMine ? Human.Airports.OrderBy(a => a.Profile.Name) : _all;
            string s = _search.Trim();
            if (s.Length > 0)
            {
                list = list.Where(
                    a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (a.Profile.Town != null && a.Profile.Town.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                         || (a.Profile.Country != null
                             && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            switch (_sortBy)
            {
                case "Country":
                    list = list.OrderBy(a => a.Profile.Country?.Name).ThenBy(a => a.Profile.Name);
                    break;
                case "Size":
                    list = list.OrderByDescending(a => (int) a.Profile.Size).ThenBy(a => a.Profile.Name);
                    break;
                case "Runway":
                    list = list.OrderByDescending(a => a.GetMaxRunwayLength()).ThenBy(a => a.Profile.Name);
                    break;
                case "Free gates":
                    list = list.OrderByDescending(a => a.Terminals.GetFreeGates(Terminal.TerminalType.Passenger))
                               .ThenBy(a => a.Profile.Name);
                    break;
                case "Airlines":
                    list = list.OrderByDescending(a => a.AirlineContracts.Select(c => c.Airline).Distinct().Count())
                               .ThenBy(a => a.Profile.Name);
                    break;
                case "Routes":
                    list = list.OrderByDescending(a => AirportHelpers.GetAirportRoutes(a).Count).ThenBy(a => a.Profile.Name);
                    break;
            }

            Airports.Clear();
            foreach (var a in list.Take(300))
            {
                var row = new AirportRow(a, own.Contains(a));
                row.LeaseCommand = new RelayCommand(() => QuickLease(row));
                Airports.Add(row);
            }
        }

        //the plus button of the Windows airport list: two gates with the chosen contract type
        private void QuickLease(AirportRow row)
        {
            var airport = row.Airport;
            if (!row.CanLease || airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger) < 1)
            {
                Status = "There are no free gates to lease at " + airport.Profile.IATACode + ".";
                return;
            }

            int gates = Math.Min(2, airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger));
            double yearly = AirportHelpers.GetYearlyContractPayment(airport, _contractType.Type, gates, 2);
            RouteCreator.SignContract(airport, _contractType.Type);
            Status = string.Format(
                "Leased {0} gate(s) at {1} ({2} contract, {3}/year).",
                gates, airport.Profile.Name, _contractType.Text, Money(yearly));

            string status = Status;
            ApplySearch();
            Selected = Airports.FirstOrDefault(a => a.Airport == airport);
            LoadDetail();
            _changed();
            Status = status;
        }

        private void LoadDetail()
        {
            Contracts.Clear();
            GateOffers.Clear();
            Facilities.Clear();
            GateChoices.Clear();
            Terminals.Clear();
            Hubs.Clear();
            HubOffers.Clear();

            if (_selected == null)
            {
                HubText = "";
                OnPropertyChanged(nameof(HubText));
                OnPropertyChanged(nameof(HasHubs));
                OnPropertyChanged(nameof(HasHubOffers));
                Title = Info = GateSummary = "";
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Info));
                OnPropertyChanged(nameof(GateSummary));
                return;
            }

            var airport = _selected.Airport;
            var airline = Human;
            var now = GameObject.GetInstance().GameTime;

            _airportType = AirportTypes.First(t => t.Value == airport.Profile.Type);
            OnPropertyChanged(nameof(AirportType));
            OnPropertyChanged(nameof(HasSelection));

            Title = airport.Profile.IATACode + "  " + airport.Profile.Name;
            Info = string.Format(
                "{0}, {1}  |  size {2}  |  longest runway {3} m",
                airport.Profile.Town?.Name, airport.Profile.Country?.Name, airport.Profile.Size.ToString().Replace('_', ' '), airport.GetMaxRunwayLength());

            int total = airport.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
            int free = airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            int mine = airport.GetAirlineContracts(airline)
                              .Where(c => c.TerminalType == Terminal.TerminalType.Passenger)
                              .Sum(c => c.NumberOfGates);
            GateSummary = string.Format("Passenger gates: {0} in total, {1} free, {2} leased by you", total, free, mine);

            int cargoTotal = airport.Terminals.GetNumberOfGates(Terminal.TerminalType.Cargo);
            if (cargoTotal > 0 || airport.GetAirlineContracts(airline).Any(c => c.TerminalType == Terminal.TerminalType.Cargo))
            {
                GateSummary += string.Format(
                    "{0}Cargo gates: {1} in total, {2} free, {3} leased by you",
                    Environment.NewLine,
                    cargoTotal,
                    airport.Terminals.GetFreeGates(Terminal.TerminalType.Cargo),
                    airport.GetAirlineContracts(airline)
                           .Where(c => c.TerminalType == Terminal.TerminalType.Cargo)
                           .Sum(c => c.NumberOfGates));
            }
            else
            {
                GateSummary += Environment.NewLine + "No cargo terminal here. Build one under Terminals to fly cargo.";
            }

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

            LoadGateOffers(airport);

            RefreshGateChoices(airport);

            LoadTerminals(airport);
            OnPropertyChanged(nameof(TerminalPriceText));

            // facilities can only be bought where the airline has a contract
            bool hasContract = Contracts.Count > 0;
            foreach (AirportFacility.FacilityType type in Enum.GetValues(typeof(AirportFacility.FacilityType)))
            {
                // cargo facilities only make sense where there is a cargo terminal
                if (type == AirportFacility.FacilityType.Cargo
                    && !airport.Terminals.AirportTerminals.Exists(t => t.Type == Terminal.TerminalType.Cargo))
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

            LoadHubs(airport);

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(GateSummary));
        }

        public static string HubEffect(HubType.TypeOfHub type)
        {
            switch (type)
            {
                case HubType.TypeOfHub.Hub: return "+20% passenger demand on flights to this airport";
                case HubType.TypeOfHub.RegionalHub: return "+20% demand on flights from the same world region";
                case HubType.TypeOfHub.FocusCity: return "+15% demand on domestic flights";
                case HubType.TypeOfHub.FortressHub: return "+30% passenger demand on flights to this airport";
                default: return "";
            }
        }

        // why the airline can't create this kind of hub here, or null when it can (the same rules as the game)
        private static string HubReason(Airline airline, Airport airport, HubType type, double price)
        {
            if (airport.HasHub(airline))
            {
                return "You already have a hub here.";
            }

            if ((airport.GetCurrentAirportFacility(airline, AirportFacility.FacilityType.Service)?.TypeLevel ?? 0) <= 0)
            {
                return "Needs a service center of yours here (Facilities).";
            }

            if (airline.Money <= price)
            {
                return "You need more than " + Money(price) + ".";
            }

            var terminalType = airline.AirlineRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            double allGates = airport.Terminals.GetNumberOfGates(terminalType);
            double percent = allGates > 0 ? 100.0 * airport.Terminals.GetNumberOfGates(airline) / allGates : 0;
            int limit = (int) airline.GetAirlineValue() + 1;
            int total = airline.GetHubs().Count;

            switch (type.Type)
            {
                case HubType.TypeOfHub.RegionalHub:
                    if (airport.Profile.Size != GeneralHelpers.Size.Large && airport.Profile.Size != GeneralHelpers.Size.Medium)
                        return "Only at medium or large airports (not smaller or the very biggest ones).";
                    if (airport.GetHubs().Count >= 7)
                        return "This airport already has the most hubs it can take (7).";
                    break;
                case HubType.TypeOfHub.FortressHub:
                    if (airport.Profile.Size <= GeneralHelpers.Size.Medium)
                        return "Only at large airports.";
                    if (percent <= 70)
                        return "You need more than 70% of the gates here (you have " + (int) percent + "%).";
                    if (total >= limit)
                        return "You have as many hubs as your airline value allows (" + limit + ").";
                    break;
                case HubType.TypeOfHub.Hub:
                    if (percent <= 20)
                        return "You need more than 20% of the gates here (you have " + (int) percent + "%).";
                    if (total >= limit)
                        return "You have as many hubs as your airline value allows (" + limit + ").";
                    if (airport.GetHubs(HubType.TypeOfHub.Hub).Count >= (int) airport.Profile.Size)
                        return "This airport has no room for another plain hub.";
                    break;
            }

            return AirlineHelpers.CanCreateHub(airline, airport, type) ? null : "You can't create this kind of hub here.";
        }

        private void LoadHubs(Airport airport)
        {
            var airline = Human;
            foreach (var h in airport.GetHubs())
            {
                var hub = h;
                bool mine = hub.Airline == airline;
                Hubs.Add(
                    new HubItem
                    {
                        Airline = hub.Airline.Profile.Name,
                        Type = hub.Type.Name,
                        Effect = HubEffect(hub.Type.Type),
                        IsMine = mine,
                        RemoveCommand = mine ? new RelayCommand(() => AskRemoveHub(airport, hub)) : null
                    });
            }

            foreach (var t in HubTypes.GetHubTypes().OrderBy(t => t.Price))
            {
                var type = t;
                double price = AirportHelpers.GetHubPrice(airport, type);
                string reason = HubReason(airline, airport, type, price);
                HubOffers.Add(
                    new HubOffer
                    {
                        Type = type,
                        Name = type.Name,
                        Price = Money(price),
                        Effect = HubEffect(type.Type),
                        Reason = reason,
                        CanBuild = reason == null,
                        BuyCommand = new RelayCommand(() => AskCreateHub(airport, type, price))
                    });
            }

            HubText = Hubs.Count == 0
                ? "No airline has a hub here."
                : airport.HasHub(airline) ? "You have a hub here." : "You have no hub here.";
            OnPropertyChanged(nameof(HubText));
            OnPropertyChanged(nameof(HasHubs));
            OnPropertyChanged(nameof(HasHubOffers));
        }

        private void AskCreateHub(Airport airport, HubType type, double price)
        {
            string reason = HubReason(Human, airport, type, price);
            if (reason != null)
            {
                Status = reason;
                return;
            }

            _pending = () =>
                {
                    if (HubReason(Human, airport, type, price) != null)
                    {
                        return;
                    }

                    airport.AddHub(new Hub(Human, type));
                    AirlineHelpers.AddAirlineInvoice(
                        Human, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);
                    Status = type.Name + " created at " + airport.Profile.IATACode + ". " + HubEffect(type.Type) + ".";
                    LoadDetail();
                    _changed();
                };
            PendingText = string.Format("Create a {0} at {1} for {2}? {3}.", type.Name.ToLower(), airport.Profile.IATACode, Money(price), HubEffect(type.Type));
        }

        private void AskRemoveHub(Airport airport, Hub hub)
        {
            _pending = () =>
                {
                    airport.RemoveHub(hub);
                    Status = "The " + hub.Type.Name.ToLower() + " at " + airport.Profile.IATACode + " was closed (no refund).";
                    LoadDetail();
                    _changed();
                };
            PendingText = "Close your " + hub.Type.Name.ToLower() + " at " + airport.Profile.IATACode + "? You get no money back.";
        }

        private static double TerminalPrice(Airport airport, int gates)
        {
            return gates * airport.GetTerminalGatePrice() + airport.GetTerminalPrice();
        }

        private void LoadTerminals(Airport airport)
        {
            var airline = Human;
            var now = GameObject.GetInstance().GameTime;

            foreach (var t in airport.Terminals.GetTerminals())
            {
                bool mine = t.Airline == airline;
                string owner = mine ? "yours" : t.Airline == null ? "airport" : t.Airline.Profile.Name;
                string state = t.DeliveryDate > now ? "ready " + DateFormatter.Date(t.DeliveryDate) : "in use";
                var terminal = t;

                bool buyable = t.DeliveryDate <= now && t.IsBuyable;

                int totalRented = airport.AirlineContracts.Sum(c => c.NumberOfGates);
                bool free = airport.Terminals.GetNumberOfGates(t.Type) - t.Gates.NumberOfGates >= totalRented;
                bool constructing = t.DeliveryDate > now;

                Terminals.Add(
                    new TerminalItem
                    {
                        Terminal = t,
                        Text = string.Format("{0}  |  {1}  |  {2} gates  |  {3}  |  {4}", t.Name, t.Type, t.Gates.NumberOfGates, owner, state),
                        CanBuy = buyable,
                        CanSell = mine && (constructing || free),
                        ActionText = constructing ? "Cancel" : "Sell",
                        BuyCommand = new RelayCommand(() => AskBuyTerminal(airport, terminal)),
                        SellCommand = new RelayCommand(() => AskSellTerminal(airport, terminal))
                    });
            }
        }

        // the computer airlines' spare gates at the airport
        private void LoadGateOffers(Airport airport)
        {
            double perGate = GateTrading.GetPricePerGate(airport);

            foreach (var contract in airport.AirlineContracts.Where(c => GateTrading.IsTradable(airport, c)).ToList())
            {
                int spare = GateTrading.GetSpareGates(airport, contract);
                if (spare < 1)
                {
                    continue;
                }

                var captured = contract;
                var offer = new GateSaleOffer
                {
                    Contract = contract,
                    Text = string.Format(
                        "{0} can spare {1} of its {2} gate(s)  |  {3} per gate",
                        contract.Airline.Profile.Name, spare, contract.NumberOfGates, Money(perGate)),
                    BuyOneText = "Buy 1",
                    BuyAllText = "Buy " + spare,
                    CanBuyAll = spare > 1
                };
                offer.BuyOneCommand = new RelayCommand(() => AskBuyGates(airport, captured, 1));
                offer.BuyAllCommand = new RelayCommand(() => AskBuyGates(airport, captured, spare));
                GateOffers.Add(offer);
            }

            GateOffersText = GateOffers.Count == 0
                ? "No airline has gates to spare here."
                : "You pay the airline a transfer fee per gate, then pay the normal rent for a " + _contractType.Text + " contract (the type chosen under Lease gates).";
            OnPropertyChanged(nameof(HasGateOffers));
            OnPropertyChanged(nameof(GateOffersText));
        }

        private void AskBuyGates(Airport airport, AirportContract contract, int gates)
        {
            double price = GateTrading.GetPricePerGate(airport) * gates;
            if (price > Human.Money)
            {
                Status = "You can't afford " + gates + " gate(s) (" + Money(price) + ").";
                return;
            }

            var type = _contractType.Type;
            _pending = () =>
                {
                    string message;
                    GateTrading.BuyGates(airport, contract, gates, type, out message);
                    Status = message;
                    ApplySearch();
                    Selected = Airports.FirstOrDefault(a => a.Airport == airport);
                    LoadDetail();
                    _changed();
                    Status = message;
                };
            PendingText = string.Format(
                "Buy {0} gate(s) from {1} at {2} for {3}? You get a {4} contract for them.",
                gates, contract.Airline.Profile.Name, airport.Profile.IATACode, Money(price), type);
        }

        private void AskBuildTerminal()
        {
            if (_selected == null)
            {
                return;
            }

            var airport = _selected.Airport;
            double price = TerminalPrice(airport, _terminalGates);

            if (price > Human.Money)
            {
                Status = "You can't afford a terminal here (" + Money(price) + ").";
                return;
            }

            string name = string.IsNullOrWhiteSpace(_terminalName) ? "Terminal" : _terminalName.Trim();
            var type = _terminalCargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            int gates = _terminalGates;
            _pending = () => BuildTerminal(airport, name, gates, type, price);
            PendingText = string.Format("Build {0} ({1} gates, {2}) at {3} for {4}?", name, gates, type, airport.Profile.IATACode, Money(price));
        }

        private void BuildTerminal(Airport airport, string name, int gates, Terminal.TerminalType type, double price)
        {
            var airline = Human;
            var delivery = GameObject.GetInstance().GameTime.AddDays(gates * 10 + 60);
            airport.AddTerminal(new Terminal(airport, airline, name, gates, delivery, type));
            AirlineHelpers.AddAirlineInvoice(
                airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);

            Status = name + " ordered, ready " + DateFormatter.Date(delivery) + ".";
            LoadDetail();
            _changed();
        }

        private void AskBuyTerminal(Airport airport, Terminal terminal)
        {
            double price = terminal.Gates.NumberOfGates * airport.GetTerminalGatePrice() + airport.GetTerminalPrice();
            if (price > Human.Money)
            {
                Status = "You can't afford this terminal (" + Money(price) + ").";
                return;
            }

            _pending = () =>
                {
                    terminal.PurchaseTerminal(Human);
                    AirlineHelpers.AddAirlineInvoice(
                        Human, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);
                    Status = "You bought " + terminal.Name + ".";
                    LoadDetail();
                    _changed();
                };
            PendingText = "Buy " + terminal.Name + " (" + terminal.Gates.NumberOfGates + " gates) for " + Money(price) + "?";
        }

        private void AskSellTerminal(Airport airport, Terminal terminal)
        {
            var now = GameObject.GetInstance().GameTime;

            if (terminal.DeliveryDate > now)
            {
                _pending = () =>
                    {
                        airport.RemoveTerminal(terminal);
                        Status = "Construction of " + terminal.Name + " cancelled.";
                        LoadDetail();
                        _changed();
                    };
                PendingText = "Cancel the construction of " + terminal.Name + "? The money is not refunded.";
                return;
            }

            double price = (terminal.Gates.NumberOfGates * airport.GetTerminalGatePrice() + airport.GetTerminalPrice()) / 2;
            _pending = () =>
                {
                    var contract = airport.AirlineContracts.FirstOrDefault(
                        c => c.Airline == terminal.Airline && c.NumberOfGates == terminal.Gates.NumberOfGates);

                    AirlineHelpers.AddAirlineInvoice(Human, now, Invoice.InvoiceType.Purchases, price);
                    terminal.Airline = null;

                    if (contract != null)
                    {
                        contract.YearlyPayment = AirportHelpers.GetYearlyContractPayment(
                            airport, AirportContract.ContractType.Full, terminal.Gates.NumberOfGates, 20);
                    }

                    Status = "Sold " + terminal.Name + " for " + Money(price) + ". You keep the gates on a lease.";
                    LoadDetail();
                    _changed();
                };
            PendingText = "Sell " + terminal.Name + " for " + Money(price) + "? You keep the gates, but now pay a lease for them.";
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

            var kind = LeaseKind;
            if (_gates < 1 || airport.Terminals.GetFreeGates(kind) < _gates)
            {
                Status = "There are no free " + (_leaseCargo ? "cargo " : "") + "gates to lease at " + airport.Profile.IATACode + ".";
                return;
            }

            double yearly = AirportHelpers.GetYearlyContractPayment(airport, _contractType.Type, _gates, 2);

            if (RouteCreator.NeedsContract(airport, kind))
            {
                // new contract (takes up to two gates, like the route creator does)
                RouteCreator.SignContract(airport, _contractType.Type, kind);
                Status = (_leaseCargo ? "Cargo gates" : "Gates") + " leased at " + airport.Profile.IATACode + ".";
            }
            else
            {
                AirportHelpers.RentGates(
                    airport, airline, _contractType.Type, kind, _gates, 2);
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
