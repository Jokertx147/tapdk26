using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class NamedChoice<T>
    {
        public string Name { get; set; }
        public T Value { get; set; }
        public override string ToString() { return Name; }
    }

    public class PolicyItem
    {
        public AirlineInsurance Policy { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ICommand CancelCommand { get; set; }
    }

    /// <summary>The insurance policies of the airline: what is covered, what they cost and buying or cancelling them.</summary>
    public class InsuranceViewModel : ObservableObject
    {
        private readonly Action _changed;
        private NamedChoice<AirlineInsurance.InsuranceType> _type;
        private NamedChoice<AirlineInsurance.InsuranceScope> _scope;
        private NamedChoice<AirlineInsurance.PaymentTerms> _terms;
        private int _years = 3;
        private string _amountText = "50000000";
        private string _status, _pendingText;
        private Action _pending;

        public InsuranceViewModel(Action changed)
        {
            _changed = changed;
            Policies = new ObservableCollection<PolicyItem>();
            Types = new List<NamedChoice<AirlineInsurance.InsuranceType>>
            {
                new NamedChoice<AirlineInsurance.InsuranceType> { Name = "Public liability", Value = AirlineInsurance.InsuranceType.PublicLiability },
                new NamedChoice<AirlineInsurance.InsuranceType> { Name = "Passenger liability", Value = AirlineInsurance.InsuranceType.PassengerLiability },
                new NamedChoice<AirlineInsurance.InsuranceType> { Name = "Combined single limit", Value = AirlineInsurance.InsuranceType.CombinedSingleLimit },
                new NamedChoice<AirlineInsurance.InsuranceType> { Name = "Full coverage", Value = AirlineInsurance.InsuranceType.FullCoverage }
            };
            Scopes = new List<NamedChoice<AirlineInsurance.InsuranceScope>>
            {
                new NamedChoice<AirlineInsurance.InsuranceScope> { Name = "Airport", Value = AirlineInsurance.InsuranceScope.Airport },
                new NamedChoice<AirlineInsurance.InsuranceScope> { Name = "Domestic", Value = AirlineInsurance.InsuranceScope.Domestic },
                new NamedChoice<AirlineInsurance.InsuranceScope> { Name = "Hub", Value = AirlineInsurance.InsuranceScope.Hub },
                new NamedChoice<AirlineInsurance.InsuranceScope> { Name = "Global", Value = AirlineInsurance.InsuranceScope.Global }
            };
            Terms = new List<NamedChoice<AirlineInsurance.PaymentTerms>>
            {
                new NamedChoice<AirlineInsurance.PaymentTerms> { Name = "Monthly", Value = AirlineInsurance.PaymentTerms.Monthly },
                new NamedChoice<AirlineInsurance.PaymentTerms> { Name = "Quarterly", Value = AirlineInsurance.PaymentTerms.Quarterly },
                new NamedChoice<AirlineInsurance.PaymentTerms> { Name = "Every six months", Value = AirlineInsurance.PaymentTerms.Biannual },
                new NamedChoice<AirlineInsurance.PaymentTerms> { Name = "Yearly", Value = AirlineInsurance.PaymentTerms.Annual }
            };
            YearChoices = new List<int> { 1, 2, 3, 5, 10, 20 };
            _type = Types[3];
            _scope = Scopes[3];
            _terms = Terms[0];
            BuyCommand = new RelayCommand(AskBuy);
            ConfirmCommand = new RelayCommand(Confirm);
            CancelPendingCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<PolicyItem> Policies { get; }
        public List<NamedChoice<AirlineInsurance.InsuranceType>> Types { get; }
        public List<NamedChoice<AirlineInsurance.InsuranceScope>> Scopes { get; }
        public List<NamedChoice<AirlineInsurance.PaymentTerms>> Terms { get; }
        public List<int> YearChoices { get; }
        public ICommand BuyCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelPendingCommand { get; }
        public string Summary { get; private set; }
        public bool HasPending => _pending != null;

        public NamedChoice<AirlineInsurance.InsuranceType> Type
        {
            get { return _type; }
            set { if (SetProperty(ref _type, value)) Changed(); }
        }

        public NamedChoice<AirlineInsurance.InsuranceScope> Scope
        {
            get { return _scope; }
            set { if (SetProperty(ref _scope, value)) Changed(); }
        }

        public NamedChoice<AirlineInsurance.PaymentTerms> PaymentTerm
        {
            get { return _terms; }
            set { if (SetProperty(ref _terms, value)) Changed(); }
        }

        public int Years
        {
            get { return _years; }
            set { if (SetProperty(ref _years, value)) Changed(); }
        }

        public string AmountText
        {
            get { return _amountText; }
            set { if (SetProperty(ref _amountText, value)) Changed(); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        public string CoverText
        {
            get
            {
                if (_type == null || _scope == null) return "";
                return InsuranceService.Describe(_type.Value) + " " + InsuranceService.Describe(_scope.Value);
            }
        }

        public string QuoteText
        {
            get
            {
                int amount;
                var airline = GameObject.GetInstance().HumanAirline;
                if (airline == null || !TryAmount(out amount))
                {
                    return "Enter the limit per aircraft as a number between 500,000 and 1,000,000,000.";
                }

                var p = InsuranceService.Quote(airline, _type.Value, _scope.Value, _terms.Value, _years, amount);
                return string.Format(
                    "{0} per payment ({1} payments over {2} years, {3} in total)  |  deductible {4}  |  cancelling costs {5}",
                    Money(p.PaymentAmount), p.RemainingPayments, _years, Money(p.PaymentAmount*p.RemainingPayments), Money(p.Deductible), Money(p.PaymentAmount*2));
            }
        }

        private void Changed()
        {
            OnPropertyChanged(nameof(QuoteText));
            OnPropertyChanged(nameof(CoverText));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private bool TryAmount(out int amount)
        {
            string t = (_amountText ?? "").Replace(",", "").Replace("$", "").Trim();
            double d;
            amount = 0;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d < 500000 || d > 1000000000)
            {
                return false;
            }
            amount = (int) d;
            return true;
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

        public void Reload()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null)
            {
                return;
            }

            Policies.Clear();
            foreach (var policy in airline.InsurancePolicies.ToList())
            {
                var captured = policy;
                Policies.Add(new PolicyItem
                {
                    Policy = policy,
                    Text = string.Format(
                        "{0}, {1} scope  |  limit {2} per aircraft  |  {3} {4}",
                        policy.InsType, policy.InsScope, Money(policy.InsuredAmount), Money(policy.PaymentAmount), policy.InsTerms.ToString().ToLowerInvariant()),
                    Detail = string.Format(
                        "Runs until {0}  |  {1} payments left, next {2}  |  deductible {3}  |  {4}",
                        policy.InsuranceExpires.ToString("d MMM yyyy", CultureInfo.InvariantCulture), policy.RemainingPayments,
                        policy.RemainingPayments > 0 ? policy.NextPaymentDue.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "none",
                        Money(policy.Deductible),
                        InsuranceService.CoversAircraft(policy.InsType) ? "covers breakdown repairs" : "liability only, no repairs"),
                    CancelCommand = new RelayCommand(() => AskCancel(captured))
                });
            }

            bool covered = airline.InsurancePolicies.Any(p => InsuranceService.CoversAircraft(p.InsType));
            double yearly = airline.InsurancePolicies.Sum(p => p.PaymentAmount*InsuranceService.PaymentsPerYear(p.InsTerms));
            Summary = string.Format(
                "{0} policies  |  about {1} a year in premiums  |  rating factor {2:0.00} (better maintenance, safety and security make premiums cheaper)  |  {3}",
                airline.InsurancePolicies.Count, Money(yearly), AirlineInsuranceHelpers.GetRatingModifier(airline),
                covered ? "breakdown repairs are covered" : "no policy covers breakdown repairs: combined single limit or full coverage does");
            OnPropertyChanged(nameof(Summary));
            Changed();
        }

        private void AskBuy()
        {
            int amount;
            var airline = GameObject.GetInstance().HumanAirline;
            if (!TryAmount(out amount))
            {
                Status = "Enter the limit per aircraft as a number between 500,000 and 1,000,000,000.";
                return;
            }

            var p = InsuranceService.Quote(airline, _type.Value, _scope.Value, _terms.Value, _years, amount);
            SetPending(
                string.Format("Buy {0} insurance ({1}) for {2} years? The first payment of {3} is charged now.", _type.Name.ToLowerInvariant(), _scope.Name.ToLowerInvariant(), _years, Money(p.PaymentAmount)),
                () =>
                {
                    string message;
                    InsuranceService.Purchase(airline, p, out message);
                    Status = message;
                    Reload();
                    _changed();
                });
        }

        private void AskCancel(AirlineInsurance policy)
        {
            SetPending(
                "Cancel this " + policy.InsType + " policy? The cancellation fee is " + Money(policy.CancellationFee) + ".",
                () =>
                {
                    string message;
                    InsuranceService.Cancel(GameObject.GetInstance().HumanAirline, policy, out message);
                    Status = message;
                    Reload();
                    _changed();
                });
        }
    }
}
