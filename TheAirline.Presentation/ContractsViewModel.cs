using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class SpecialContractItem
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public string Terms { get; set; }
        public List<string> Lines { get; set; }
        public string AcceptText { get; set; }
        public bool IsOffer { get; set; }
        public ICommand AcceptCommand { get; set; }
        public ICommand DeclineCommand { get; set; }
    }

    /// <summary>Special contracts: offers from companies and the contracts the airline has taken.</summary>
    public class ContractsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status, _pendingText;
        private Action _pending;

        public ContractsViewModel(Action changed)
        {
            _changed = changed;
            Offers = new ObservableCollection<SpecialContractItem>();
            Active = new ObservableCollection<SpecialContractItem>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<SpecialContractItem> Offers { get; }
        public ObservableCollection<SpecialContractItem> Active { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public string OffersTitle { get; private set; }
        public string ActiveTitle { get; private set; }
        public bool HasPending => _pending != null;

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
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

        private static string Date(DateTime d)
        {
            return d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        private void Confirm()
        {
            var action = _pending;
            SetPending(null, null);
            action?.Invoke();
        }

        private static List<string> TypeLines(SpecialContractType t)
        {
            var lines = new List<string>();
            foreach (var r in t.Routes)
            {
                lines.Add("Route: " + ContractService.DescribeRoute(r));
            }

            foreach (var c in t.Requirements.Where(x => x.Type == ContractRequirement.RequirementType.ClassType))
            {
                lines.Add("Needs: " + ContractService.DescribeRequirement(c));
            }

            return lines;
        }

        public void Reload()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null)
            {
                return;
            }

            Offers.Clear();
            foreach (var type in GameObject.GetInstance().Contracts.ToList())
            {
                var t = type;
                string terms = t.IsFixedDate
                    ? string.Format("From {0} to {1}  |  pays {2} if you serve every route the whole time, {3} penalty if not", Date(t.Period.From), Date(t.Period.To), Money(t.Payment), Money(t.Penalty))
                    : string.Format("Offer open until {0}  |  you get {1} days  |  pays {2} if done, {3} penalty if not", Date(t.LastDate.AddMonths(1)), ContractService.DaysToFulfil, Money(t.Payment), Money(t.Penalty));
                Offers.Add(
                    new SpecialContractItem
                    {
                        Name = t.Name,
                        Text = t.Text,
                        Terms = terms,
                        Lines = TypeLines(t),
                        IsOffer = true,
                        AcceptText = "Accept",
                        AcceptCommand = new RelayCommand(() => AskAccept(t)),
                        DeclineCommand = new RelayCommand(() => AskDecline(t))
                    });
            }

            Active.Clear();
            foreach (var sc in airline.SpecialContracts.ToList())
            {
                var lines = new List<string>();
                foreach (var scr in sc.Type.Routes)
                {
                    bool served = ContractService.IsServed(sc, scr);
                    string state;
                    if (served)
                    {
                        state = "flown now";
                    }
                    else if (ContractService.FindOwnRoutes(airline, scr).Count > 0)
                    {
                        state = "you have this route, but no aircraft flies it";
                    }
                    else
                    {
                        state = "create this route under Routes, it counts automatically";
                    }

                    lines.Add(ContractService.DescribeRoute(scr) + "  -  " + state);
                }

                foreach (var c in sc.Type.Requirements.Where(x => x.Type == ContractRequirement.RequirementType.ClassType))
                {
                    lines.Add("Needs: " + ContractService.DescribeRequirement(c));
                }

                var deadline = ContractService.GetDeadline(sc);
                Active.Add(
                    new SpecialContractItem
                    {
                        Name = sc.Type.Name,
                        Text = sc.Type.Text,
                        Terms = string.Format(
                            "{0} {1}  |  pays {2}, penalty {3}",
                            sc.Type.IsFixedDate && sc.Date > GameObject.GetInstance().GameTime ? "Starts " + Date(sc.Date) + ", judged" : "Judged",
                            Date(deadline), Money(sc.Type.Payment), Money(sc.Type.Penalty)),
                        Lines = lines
                    });
            }

            OffersTitle = Offers.Count == 0 ? "No offers right now. Companies ask now and then." : "Offers";
            ActiveTitle = Active.Count == 0 ? "You have no contracts." : "Your contracts";
            OnPropertyChanged(nameof(OffersTitle));
            OnPropertyChanged(nameof(ActiveTitle));
        }

        private void AskAccept(SpecialContractType t)
        {
            SetPending(
                string.Format("Accept \"{0}\"? You get {1} if the routes are flown when it is judged, and pay {2} if not.", t.Name, Money(t.Payment), Money(t.Penalty)),
                () =>
                {
                    var airline = GameObject.GetInstance().HumanAirline;
                    ContractService.Accept(airline, t);
                    Status = "You took \"" + t.Name + "\". Routes you already have that fit it count right away.";
                    Reload();
                    _changed();
                });
        }

        private void AskDecline(SpecialContractType t)
        {
            SetPending(
                "Decline \"" + t.Name + "\"?",
                () =>
                {
                    ContractService.Decline(t);
                    Status = "You declined \"" + t.Name + "\".";
                    Reload();
                    _changed();
                });
        }
    }
}
