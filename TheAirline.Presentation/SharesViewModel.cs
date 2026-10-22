using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airlines.Subsidiary;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class MarketRow
    {
        public Airline Airline { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
    }

    public class HoldingRow
    {
        public Airline Airline { get; set; }
        public string Text { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Airlines > Shares: your airline's shares, the shares you own and the market, with takeovers.</summary>
    public class SharesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _myText, _status, _pendingText, _search = "", _selectedText, _portfolioText;
        private Action _pending;
        private MarketRow _selected;
        private HoldingRow _selectedHolding;
        private int _issueCount = 1000, _buybackCount = 1000, _buyCount = 1000, _sellCount;

        public SharesViewModel(Action changed)
        {
            _changed = changed;
            Market = new ObservableCollection<MarketRow>();
            PortfolioRows = new ObservableCollection<HoldingRow>();
            IssueCommand = new RelayCommand(AskIssue);
            BuyBackCommand = new RelayCommand(AskBuyBack);
            BuyCommand = new RelayCommand(AskBuy, () => _selected != null);
            SellCommand = new RelayCommand(AskSell, () => _selectedHolding != null);
            MergeCommand = new RelayCommand(() => AskTakeover(false), () => _selected != null);
            SubsidiaryCommand = new RelayCommand(() => AskTakeover(true), () => _selected != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<MarketRow> Market { get; }
        public ObservableCollection<HoldingRow> PortfolioRows { get; }
        public List<int> Counts { get; } = new List<int> { 100, 250, 500, 1000, 2000, 3000, 4000, 5000, 10000 };
        public ICommand IssueCommand { get; }
        public ICommand BuyBackCommand { get; }
        public ICommand BuyCommand { get; }
        public ICommand SellCommand { get; }
        public ICommand MergeCommand { get; }
        public ICommand SubsidiaryCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string MyText { get => _myText; private set => SetProperty(ref _myText, value); }
        public string PortfolioText { get => _portfolioText; private set => SetProperty(ref _portfolioText, value); }
        public string SelectedText { get => _selectedText; private set => SetProperty(ref _selectedText, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string PendingText { get => _pendingText; private set => SetProperty(ref _pendingText, value); }
        public bool HasPending => _pending != null;
        public bool HasSelection => _selected != null;
        public bool HasHoldingSelection => _selectedHolding != null;

        public int IssueCount { get => _issueCount; set { if (SetProperty(ref _issueCount, value)) UpdateMine(); } }
        public int BuybackCount { get => _buybackCount; set { if (SetProperty(ref _buybackCount, value)) UpdateMine(); } }
        public int BuyCount { get => _buyCount; set { if (SetProperty(ref _buyCount, value)) UpdateSelected(); } }
        public int SellCount { get => _sellCount; set => SetProperty(ref _sellCount, value); }
        public List<int> SellCounts { get; private set; } = new List<int>();

        public string IssueText { get; private set; }
        public string BuybackText { get; private set; }

        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) LoadMarket(); }
        }

        public MarketRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    SetPending(null, null);
                    foreach (var c in new[] { BuyCommand, MergeCommand, SubsidiaryCommand }) (c as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasSelection));
                    UpdateSelected();
                }
            }
        }

        public HoldingRow SelectedHolding
        {
            get => _selectedHolding;
            set
            {
                if (SetProperty(ref _selectedHolding, value))
                {
                    (SellCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasHoldingSelection));
                    SellCounts = value == null ? new List<int>() : new[] { value.Count, value.Count/2, 1000, 500, 100 }.Where(n => n > 0 && n <= value.Count).Distinct().ToList();
                    OnPropertyChanged(nameof(SellCounts));
                    SellCount = SellCounts.FirstOrDefault();
                }
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        private static Airline Me => GameObject.GetInstance()?.HumanAirline;

        public void Reload()
        {
            if (Me == null) return;
            UpdateMine();
            LoadPortfolio();
            LoadMarket();
        }

        private void UpdateMine()
        {
            var me = Me;
            if (me == null) return;
            MyText = string.Format(
                "{0}: worth {1}  |  {2:N0} shares at {3} each  |  you own {4:0.#}%, {5:N0} shares are on the market",
                me.Profile.Name, Money(SharesService.Value(me)), SharesService.Total(me), Money(SharesService.Price(me)),
                SharesService.OwnedPercent(me, me)*100, SharesService.Public(me));
            int max = SharesService.MaxIssue(me);
            IssueText = max == 0
                ? "You can't issue more shares without falling below 51% of your airline."
                : string.Format("Issuing {0:N0} new shares brings in {1} (up to {2:N0} can be issued).", Math.Min(_issueCount, max), Money(SharesService.IssueIncome(me, Math.Min(_issueCount, max))), max);
            int pub = SharesService.Public(me);
            BuybackText = pub == 0
                ? "None of your shares are on the market."
                : string.Format("Buying back {0:N0} shares costs {1}.", Math.Min(_buybackCount, pub), Money(SharesService.BuybackCost(me, Math.Min(_buybackCount, pub))));
            OnPropertyChanged(nameof(IssueText));
            OnPropertyChanged(nameof(BuybackText));
        }

        private void LoadPortfolio()
        {
            var me = Me;
            PortfolioRows.Clear();
            double value = 0, cost = 0;
            foreach (var p in SharesService.Portfolio(me).OrderByDescending(x => x.Value.Count*SharesService.Price(x.Key)))
            {
                double v = p.Value.Count*SharesService.Price(p.Key);
                value += v;
                cost += p.Value.Cost;
                PortfolioRows.Add(new HoldingRow
                {
                    Airline = p.Key,
                    Count = p.Value.Count,
                    Text = string.Format(
                        "{0}: {1:N0} shares ({2:0.#}%)  |  worth {3}  |  paid {4}  |  {5} {6}",
                        p.Key.Profile.Name, p.Value.Count, SharesService.OwnedPercent(p.Key, me)*100, Money(v), Money(p.Value.Cost),
                        v >= p.Value.Cost ? "gain" : "loss", Money(Math.Abs(v - p.Value.Cost)))
                });
            }
            PortfolioText = PortfolioRows.Count == 0
                ? "You don't own shares in other airlines."
                : string.Format("Your shares in other airlines are worth {0} (you paid {1}).", Money(value), Money(cost));
            SelectedHolding = null;
        }

        private void LoadMarket()
        {
            var me = Me;
            if (me == null) return;
            string s = _search.Trim();
            var keep = _selected?.Airline;
            Market.Clear();
            foreach (var a in Airlines.GetAllAirlines()
                                      .Where(a => SharesService.GetTradeBlocker(me, a) == null)
                                      .Where(a => s.Length == 0 || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                                  || (a.Profile.IATACode ?? "").StartsWith(s, StringComparison.OrdinalIgnoreCase)
                                                  || (a.Profile.Country?.Name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                                      .OrderByDescending(SharesService.Value)
                                      .Take(80))
            {
                double mine = SharesService.OwnedPercent(a, me);
                Market.Add(new MarketRow
                {
                    Airline = a,
                    LogoPath = AirlineProfileService.GetLogo(a),
                    Name = a.Profile.Name + " (" + a.Profile.IATACode + "), " + a.Profile.Country?.Name,
                    Numbers = string.Format(
                        "worth {0}  |  share {1}  |  {2:N0} on the market{3}",
                        Money(SharesService.Value(a)), Money(SharesService.Price(a)), SharesService.Public(a),
                        mine > 0 ? string.Format("  |  you own {0:0.#}%", mine*100) : "")
                });
            }
            _selected = null;
            Selected = Market.FirstOrDefault(m => m.Airline == keep);
            OnPropertyChanged(nameof(Selected));
        }

        private void UpdateSelected()
        {
            if (_selected == null)
            {
                SelectedText = "";
                return;
            }
            var a = _selected.Airline;
            var me = Me;
            int buy = Math.Min(_buyCount, SharesService.Public(a));
            SelectedText = string.Format(
                "{0}: {1} routes, {2} aircraft, cash {3}" + Environment.NewLine +
                "Its founders hold {4:0.#}%, the market {5:0.#}%, you {6:0.#}%." + Environment.NewLine +
                "Buying {7:N0} shares costs {8}." + Environment.NewLine +
                "Taking it over (all {9:N0} shares you don't own, with a 30% premium) costs {10}.",
                a.Profile.Name, a.Routes.Count, a.Fleet.Count, Money(a.Money),
                SharesService.OwnedPercent(a, a)*100, SharesService.OwnedPercent(a, null)*100, SharesService.OwnedPercent(a, me)*100,
                buy, Money(SharesService.BuyCost(a, buy)),
                SharesService.Total(a) - SharesService.Owned(a, me), Money(SharesService.TakeoverCost(me, a)));
        }

        private void Done(bool ok, string message)
        {
            Reload();
            Status = message;
            if (ok) _changed?.Invoke();
        }

        private void AskIssue()
        {
            var me = Me;
            int n = Math.Min(_issueCount, SharesService.MaxIssue(me));
            if (n < 1)
            {
                Status = IssueText;
                return;
            }
            SetPending(string.Format("Issue {0:N0} new shares for {1}?", n, Money(SharesService.IssueIncome(me, n))),
                () => { string m; Done(SharesService.Issue(me, n, out m), m); });
        }

        private void AskBuyBack()
        {
            var me = Me;
            int n = Math.Min(_buybackCount, SharesService.Public(me));
            if (n < 1)
            {
                Status = BuybackText;
                return;
            }
            SetPending(string.Format("Buy back {0:N0} of your shares for {1}?", n, Money(SharesService.BuybackCost(me, n))),
                () => { string m; Done(SharesService.BuyBack(me, n, out m), m); });
        }

        private void AskBuy()
        {
            var a = _selected?.Airline;
            if (a == null) return;
            int n = Math.Min(_buyCount, SharesService.Public(a));
            SetPending(string.Format("Buy {0:N0} shares of {1} for {2}?", n, a.Profile.Name, Money(SharesService.BuyCost(a, n))),
                () => { string m; Done(SharesService.Buy(Me, a, n, out m), m); });
        }

        private void AskSell()
        {
            var h = _selectedHolding;
            if (h == null) return;
            int n = Math.Min(_sellCount, h.Count);
            SetPending(string.Format("Sell {0:N0} shares of {1} for {2}?", n, h.Airline.Profile.Name, Money(SharesService.SellIncome(h.Airline, n))),
                () => { string m; Done(SharesService.Sell(Me, h.Airline, n, out m), m); });
        }

        private void AskTakeover(bool asSubsidiary)
        {
            var a = _selected?.Airline;
            if (a == null) return;
            string blocker = SharesService.GetTakeoverBlocker(Me, a);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            string what = asSubsidiary
                ? "It keeps its name, logo and money and its management keeps running it as your subsidiary."
                : "Its aircraft, routes, gates, pilots, cash and loans join your airline and its name disappears.";
            SetPending(
                string.Format("Take over {0} for {1}? {2} This can't be undone.", a.Profile.Name, Money(SharesService.TakeoverCost(Me, a)), what),
                () => { string m; Done(SharesService.TakeOver(Me, a, asSubsidiary, out m), m); });
        }
    }
}
