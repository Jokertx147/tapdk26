using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>One airport contract in the list.</summary>
    public class GateContractRow
    {
        public GateContractRow(AirportContract c)
        {
            Contract = c;
            int months = GateContractService.MonthsLeft(c);
            var p = c.Airport.Profile;
            Title = p.IATACode + "  " + p.Name + ", " + p.Country?.Name;
            Numbers = string.Format(
                "{0}  |  {1} {2} gate(s)  |  {3}/year  |  {4} route(s)",
                GateContractService.TypeName(c.Type), c.NumberOfGates, c.TerminalType == Terminal.TerminalType.Cargo ? "cargo" : "passenger",
                new ValueCurrencyFormatter().Convert(c.YearlyPayment), GateContractService.RoutesUsing(c));
            Expiry = (c.Terminal != null ? "Your own terminal  |  " : "")
                     + "Ends " + c.ExpireDate.ToShortDateString() + " (" + months + " month(s))"
                     + (c.AutoRenew ? "  |  renews by itself" : "  |  NOT renewed");
            IsExpiringSoon = months <= 6;
            Warning = !c.AutoRenew && months <= 6 ? "Expires soon and is not renewed: your routes there would be cancelled." : null;
        }

        public AirportContract Contract { get; }
        public string Title { get; }
        public string Numbers { get; }
        public string Expiry { get; }
        public bool IsExpiringSoon { get; }
        public string Warning { get; }
        public bool HasWarning => Warning != null;
    }

    /// <summary>Airports > Contracts: all the airline's gate contracts with extend, auto renew, gates and end.</summary>
    public class GateContractsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _filter = "All";
        private string _sortBy = "Ends first";
        private GateContractRow _selected;
        private int _years = 5;
        private int _gates = 1;
        private string _status, _pendingText, _summary;
        private Action _pending;

        public GateContractsViewModel(Action changed)
        {
            _changed = changed;
            Rows = new ObservableCollection<GateContractRow>();
            Filters = new List<string> { "All", "Ending within 6 months", "Not renewed", "Passenger", "Cargo" };
            SortOptions = new List<string> { "Ends first", "Airport", "Most expensive", "Most gates" };
            ExtendCommand = new RelayCommand(AskExtend, () => _selected != null);
            AddGatesCommand = new RelayCommand(AskAddGates, () => _selected != null);
            RemoveGatesCommand = new RelayCommand(AskRemoveGates, () => _selected != null);
            EndCommand = new RelayCommand(AskEnd, () => _selected != null);
            RenewAllCommand = new RelayCommand(() =>
                {
                    int n = GateContractService.SetAutoRenewAll(GameObject.GetInstance().HumanAirline, true);
                    Status = n == 0 ? "All contracts already renew by themselves." : n + " contract(s) now renew by themselves.";
                    Reload();
                });
            OpenAirportCommand = new RelayCommand(() => { if (_selected != null) OpenAirport?.Invoke(_selected.Contract.Airport); }, () => _selected != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        /// <summary>Set by the airline page: shows the airport on the Manage tab.</summary>
        public Action<Airport> OpenAirport { get; set; }

        public ObservableCollection<GateContractRow> Rows { get; }
        public List<string> Filters { get; }
        public List<string> SortOptions { get; }
        public List<int> YearChoices { get; } = GateContractService.Lengths.ToList();
        public List<int> GateChoices { get; } = new List<int> { 1, 2, 3, 4, 5, 10, 15, 20 };

        public ICommand ExtendCommand { get; }
        public ICommand AddGatesCommand { get; }
        public ICommand RemoveGatesCommand { get; }
        public ICommand EndCommand { get; }
        public ICommand RenewAllCommand { get; }
        public ICommand OpenAirportCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Filter
        {
            get => _filter;
            set { if (SetProperty(ref _filter, value ?? "All")) Reload(); }
        }

        public string SortBy
        {
            get => _sortBy;
            set { if (SetProperty(ref _sortBy, value ?? "Ends first")) Reload(); }
        }

        public GateContractRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    SetPending(null, null);
                    foreach (var c in new[] { ExtendCommand, AddGatesCommand, RemoveGatesCommand, EndCommand, OpenAirportCommand })
                        (c as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(AutoRenew));
                    OnPropertyChanged(nameof(Detail));
                    OnPropertyChanged(nameof(ExtendText));
                    OnPropertyChanged(nameof(GatesText));
                }
            }
        }

        public bool HasSelection => _selected != null;

        public bool AutoRenew
        {
            get => _selected?.Contract.AutoRenew ?? false;
            set
            {
                if (_selected == null || _selected.Contract.AutoRenew == value) return;
                GateContractService.SetAutoRenew(_selected.Contract, value);
                Status = value ? "This contract now renews by itself." : "This contract will end on " + _selected.Contract.ExpireDate.ToShortDateString() + ".";
                Reload();
            }
        }

        public string Detail
        {
            get
            {
                if (_selected == null) return "";
                var c = _selected.Contract;
                return string.Format(
                    "{0}{1}Signed {2}, {3} year term. Ending it early costs {4}.",
                    _selected.Title, Environment.NewLine, c.ContractDate.ToShortDateString(), c.Length,
                    new ValueCurrencyFormatter().Convert(GateContractService.EndPenalty(c)));
            }
        }

        public int Years
        {
            get => _years;
            set { if (SetProperty(ref _years, value)) OnPropertyChanged(nameof(ExtendText)); }
        }

        public int Gates
        {
            get => _gates;
            set { if (SetProperty(ref _gates, value)) OnPropertyChanged(nameof(GatesText)); }
        }

        public string ExtendText
        {
            get
            {
                if (_selected == null) return "";
                var c = _selected.Contract;
                var now = GameObject.GetInstance().GameTime;
                var until = (c.ExpireDate > now ? c.ExpireDate : now).AddYears(_years);
                return string.Format(
                    "Runs until {0} at {1} a year (now {2}). Longer terms cost less per year.",
                    until.ToShortDateString(), Money(GateContractService.ExtendedPayment(c, _years)), Money(c.YearlyPayment));
            }
        }

        public string GatesText
        {
            get
            {
                if (_selected == null) return "";
                var c = _selected.Contract;
                int free = c.Airport.Terminals.GetFreeGates(c.TerminalType);
                return string.Format(
                    "{0} free gate(s) at {1}. {2} more cost {3} a year.",
                    free, c.Airport.Profile.IATACode, _gates, Money(GateContractService.AddGatesPrice(c, _gates)));
            }
        }

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string PendingText
        {
            get => _pendingText;
            private set => SetProperty(ref _pendingText, value);
        }

        public bool HasPending => _pending != null;

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

        public void Reload()
        {
            var airline = GameObject.GetInstance()?.HumanAirline;
            if (airline == null) return;
            var all = GateContractService.GetContracts(airline);

            IEnumerable<AirportContract> list = all;
            switch (_filter)
            {
                case "Ending within 6 months": list = list.Where(c => GateContractService.MonthsLeft(c) <= 6); break;
                case "Not renewed": list = list.Where(c => !c.AutoRenew); break;
                case "Passenger": list = list.Where(c => c.TerminalType == Terminal.TerminalType.Passenger); break;
                case "Cargo": list = list.Where(c => c.TerminalType == Terminal.TerminalType.Cargo); break;
            }
            switch (_sortBy)
            {
                case "Airport": list = list.OrderBy(c => c.Airport.Profile.IATACode); break;
                case "Most expensive": list = list.OrderByDescending(c => c.YearlyPayment); break;
                case "Most gates": list = list.OrderByDescending(c => c.NumberOfGates); break;
                default: list = list.OrderBy(c => c.ExpireDate); break;
            }

            var keep = _selected?.Contract;
            Rows.Clear();
            foreach (var c in list) Rows.Add(new GateContractRow(c));
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Contract == keep);
            OnPropertyChanged(nameof(Selected));

            int soon = all.Count(c => !c.AutoRenew && GateContractService.MonthsLeft(c) <= 6);
            Summary = string.Format(
                "{0} contract(s) at {1} airport(s)  |  {2} gates  |  {3} a year{4}",
                all.Count, all.Select(c => c.Airport).Distinct().Count(), all.Sum(c => c.NumberOfGates),
                Money(all.Sum(c => c.YearlyPayment)),
                soon > 0 ? "  |  " + soon + " ending soon without renewal" : "");
        }

        private void After(bool ok, string message)
        {
            Status = message;
            if (ok)
            {
                string m = message;
                Reload();
                _changed?.Invoke();
                Status = m;
            }
        }

        private void AskExtend()
        {
            var row = _selected;
            if (row == null) return;
            int years = _years;
            SetPending(
                "Extend the contract at " + row.Contract.Airport.Profile.IATACode + " by " + years + " year(s)? " + ExtendText,
                () => { string m; After(GateContractService.Extend(row.Contract, years, out m), m); });
        }

        private void AskAddGates()
        {
            var row = _selected;
            if (row == null) return;
            int gates = _gates;
            string blocker = GateContractService.GetAddGatesBlocker(row.Contract, gates);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            SetPending(
                "Add " + gates + " gate(s) at " + row.Contract.Airport.Profile.IATACode + " for " + Money(GateContractService.AddGatesPrice(row.Contract, gates)) + " more a year?",
                () => { string m; After(GateContractService.AddGates(row.Contract, gates, out m), m); });
        }

        private void AskRemoveGates()
        {
            var row = _selected;
            if (row == null) return;
            int gates = _gates;
            string blocker = GateContractService.GetRemoveGatesBlocker(row.Contract, gates);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            SetPending(
                "Give back " + gates + " gate(s) at " + row.Contract.Airport.Profile.IATACode + "? The rent goes down with them.",
                () => { string m; After(GateContractService.RemoveGates(row.Contract, gates, out m), m); });
        }

        private void AskEnd()
        {
            var row = _selected;
            if (row == null) return;
            string blocker = GateContractService.GetEndBlocker(row.Contract);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            SetPending(
                "End the contract at " + row.Contract.Airport.Profile.IATACode + "? The penalty is " + Money(GateContractService.EndPenalty(row.Contract)) + ".",
                () => { string m; After(GateContractService.End(row.Contract, out m), m); });
        }
    }
}
