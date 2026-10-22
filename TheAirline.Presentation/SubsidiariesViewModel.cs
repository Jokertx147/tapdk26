using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airlines.Subsidiary;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class SubsidiaryRow
    {
        public SubsidiaryAirline Airline { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
    }

    /// <summary>Airlines > Subsidiaries: start one, move money in and out, sell it or fold it into your airline.</summary>
    public class SubsidiariesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private SubsidiaryRow _selected;
        private string _status, _pendingText, _transfer = "1000000", _name = "", _code = "", _money = "", _summary;
        private Action _pending;
        private AirportItem _homebase;
        private NamedChoice<Airline.AirlineMentality> _mentality;
        private NamedChoice<AirlineFocus> _market;
        private NamedChoice<Route.RouteType> _focus;

        public SubsidiariesViewModel(Action changed)
        {
            _changed = changed;
            Rows = new ObservableCollection<SubsidiaryRow>();
            Homebases = new ObservableCollection<AirportItem>();
            Mentalities = new List<NamedChoice<Airline.AirlineMentality>>
            {
                new NamedChoice<Airline.AirlineMentality> { Name = "Careful", Value = Airline.AirlineMentality.Safe },
                new NamedChoice<Airline.AirlineMentality> { Name = "Balanced", Value = Airline.AirlineMentality.Moderate },
                new NamedChoice<Airline.AirlineMentality> { Name = "Fast growing", Value = Airline.AirlineMentality.Aggressive }
            };
            Markets = new List<NamedChoice<AirlineFocus>>
            {
                new NamedChoice<AirlineFocus> { Name = "Local", Value = AirlineFocus.Local },
                new NamedChoice<AirlineFocus> { Name = "Domestic", Value = AirlineFocus.Domestic },
                new NamedChoice<AirlineFocus> { Name = "Regional", Value = AirlineFocus.Regional },
                new NamedChoice<AirlineFocus> { Name = "Global", Value = AirlineFocus.Global }
            };
            Focuses = new List<NamedChoice<Route.RouteType>>
            {
                new NamedChoice<Route.RouteType> { Name = "Passengers", Value = Route.RouteType.Passenger },
                new NamedChoice<Route.RouteType> { Name = "Cargo", Value = Route.RouteType.Cargo }
            };
            _mentality = Mentalities[1];
            _market = Markets[1];
            _focus = Focuses[0];
            _money = ((long) SubsidiaryService.MinStartMoney*5).ToString();
            CreateCommand = new RelayCommand(AskCreate);
            ToSubCommand = new RelayCommand(() => AskTransfer(true), () => _selected != null);
            FromSubCommand = new RelayCommand(() => AskTransfer(false), () => _selected != null);
            SellCommand = new RelayCommand(AskSell, () => _selected != null);
            FoldCommand = new RelayCommand(AskFold, () => _selected != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<SubsidiaryRow> Rows { get; }
        public ObservableCollection<AirportItem> Homebases { get; }
        public List<NamedChoice<Airline.AirlineMentality>> Mentalities { get; }
        public List<NamedChoice<AirlineFocus>> Markets { get; }
        public List<NamedChoice<Route.RouteType>> Focuses { get; }
        public ICommand CreateCommand { get; }
        public ICommand ToSubCommand { get; }
        public ICommand FromSubCommand { get; }
        public ICommand SellCommand { get; }
        public ICommand FoldCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Explanation =>
            "A subsidiary is a second airline you own. It flies under its own name, code and money and is run by its own " +
            "management: it buys aircraft, opens routes and keeps what it earns. You decide how much money it has, and you can " +
            "sell it or fold it into your airline at any time. Its value counts toward yours.";

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string PendingText { get => _pendingText; private set => SetProperty(ref _pendingText, value); }
        public bool HasPending => _pending != null;
        public bool HasSelection => _selected != null;

        public string Transfer { get => _transfer; set => SetProperty(ref _transfer, value ?? ""); }
        public string NewName { get => _name; set => SetProperty(ref _name, value ?? ""); }
        public string NewCode { get => _code; set => SetProperty(ref _code, value ?? ""); }
        public string NewMoney { get => _money; set => SetProperty(ref _money, value ?? ""); }
        public AirportItem Homebase { get => _homebase; set => SetProperty(ref _homebase, value); }
        public NamedChoice<Airline.AirlineMentality> Mentality { get => _mentality; set => SetProperty(ref _mentality, value); }
        public NamedChoice<AirlineFocus> Market { get => _market; set => SetProperty(ref _market, value); }
        public NamedChoice<Route.RouteType> Focus { get => _focus; set => SetProperty(ref _focus, value); }

        public string MinMoneyText => "At least " + Money(SubsidiaryService.MinStartMoney) + ". It comes out of your airline's cash.";

        public SubsidiaryRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    SetPending(null, null);
                    foreach (var c in new[] { ToSubCommand, FromSubCommand, SellCommand, FoldCommand }) (c as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasSelection));
                }
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static double Parse(string text)
        {
            double v;
            return double.TryParse((text ?? "").Replace(",", "").Replace("$", "").Trim(), out v) ? v : 0;
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        public void Reload()
        {
            var me = GameObject.GetInstance()?.HumanAirline;
            if (me == null) return;
            var keep = _selected?.Airline;
            Rows.Clear();
            foreach (var s in SubsidiaryService.Mine(me))
            {
                Rows.Add(new SubsidiaryRow
                {
                    Airline = s,
                    LogoPath = AirlineProfileService.GetLogo(s),
                    Name = s.Profile.Name + " (" + s.Profile.IATACode + ")",
                    Numbers = string.Format(
                        "cash {0}  |  {1} aircraft, {2} routes, {3} airports  |  worth {4}  |  last month {5}",
                        Money(s.Money), s.Fleet.Count, s.Routes.Count, s.Airports.Count, Money(SharesService.Value(s)), Money(SubsidiaryService.LastMonthProfit(s)))
                });
            }
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Airline == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            Summary = Rows.Count == 0
                ? "You have no subsidiaries."
                : string.Format("{0} subsidiar{1}, worth {2} together.", Rows.Count, Rows.Count == 1 ? "y" : "ies", Money(Rows.Sum(r => SharesService.Value(r.Airline))));

            var home = _homebase?.Airport;
            Homebases.Clear();
            foreach (var a in me.Airports.OrderBy(a => a.Profile.Name)) Homebases.Add(new AirportItem(a, true));
            Homebase = Homebases.FirstOrDefault(h => h.Airport == home) ?? Homebases.FirstOrDefault();
        }

        private void Done(bool ok, string message)
        {
            Reload();
            Status = message;
            if (ok) _changed?.Invoke();
        }

        private void AskCreate()
        {
            var me = GameObject.GetInstance().HumanAirline;
            double money = Parse(_money);
            string blocker = SubsidiaryService.GetCreateBlocker(me, _name, _code, _homebase?.Airport, money);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            string name = _name, code = _code;
            var home = _homebase.Airport;
            var mentality = _mentality.Value;
            var market = _market.Value;
            var focus = _focus.Value;
            SetPending(
                string.Format("Start {0} ({1}) at {2} with {3} of your money?", name.Trim(), code.Trim().ToUpperInvariant(), home.Profile.IATACode, Money(money)),
                () =>
                    {
                        string m;
                        var sub = SubsidiaryService.Create(me, name, code, home, money, mentality, market, focus, out m);
                        if (sub != null)
                        {
                            NewName = "";
                            NewCode = "";
                        }
                        Done(sub != null, m);
                    });
        }

        private void AskTransfer(bool toSub)
        {
            var s = _selected?.Airline;
            if (s == null) return;
            double amount = Parse(_transfer);
            if (amount <= 0)
            {
                Status = "Type an amount.";
                return;
            }
            var me = GameObject.GetInstance().HumanAirline;
            SetPending(
                toSub ? "Move " + Money(amount) + " to " + s.Profile.Name + "?" : "Take " + Money(amount) + " out of " + s.Profile.Name + "?",
                () => { string m; Done(SubsidiaryService.Transfer(me, s, toSub ? amount : -amount, out m), m); });
        }

        private void AskSell()
        {
            var s = _selected?.Airline;
            if (s == null) return;
            var me = GameObject.GetInstance().HumanAirline;
            SetPending(
                "Sell " + s.Profile.Name + " for " + Money(SubsidiaryService.SalePrice(s)) + " (90% of its value)? It becomes an independent airline.",
                () => { string m; SubsidiaryService.Sell(me, s, out m); Done(true, m); });
        }

        private void AskFold()
        {
            var s = _selected?.Airline;
            if (s == null) return;
            var me = GameObject.GetInstance().HumanAirline;
            SetPending(
                "Fold " + s.Profile.Name + " into your airline? Its aircraft, routes, gates, pilots and " + Money(s.Money) + " come over and its name disappears.",
                () => { string m; SubsidiaryService.FoldIn(me, s, out m); Done(true, m); });
        }
    }
}
