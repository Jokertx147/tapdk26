using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Presentation
{
    public class LoanItem : ObservableObject
    {
        private string _payText = "";

        public Loan Loan { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ICommand PayCommand { get; set; }
        public ICommand PayOffCommand { get; set; }

        /// <summary>The amount typed in to pay back early.</summary>
        public string PayText
        {
            get { return _payText; }
            set { SetProperty(ref _payText, value); }
        }
    }

    /// <summary>Take out loans and pay them back early. Monthly payments are taken by the game.</summary>
    public class LoansViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _amountText = "10000000";
        private int _years = 5;
        private string _status;

        public LoansViewModel(Action changed)
        {
            _changed = changed;
            Loans = new ObservableCollection<LoanItem>();
            YearChoices = new List<int> { 1, 2, 5, 10, 15, 20, 25 };
            TakeLoanCommand = new RelayCommand(TakeLoan);
            Reload();
        }

        public ObservableCollection<LoanItem> Loans { get; }
        public List<int> YearChoices { get; }
        public ICommand TakeLoanCommand { get; }
        public string Summary { get; private set; }

        public string AmountText
        {
            get { return _amountText; }
            set { SetProperty(ref _amountText, value); OnPropertyChanged(nameof(OfferText)); }
        }

        public int Years
        {
            get { return _years; }
            set { SetProperty(ref _years, value); OnPropertyChanged(nameof(OfferText)); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>What the loan in the form would cost.</summary>
        public string OfferText
        {
            get
            {
                double amount;
                if (!TryAmount(out amount))
                {
                    return "Enter an amount, for example 10000000.";
                }

                var loan = NewLoan(amount);
                double monthly = loan.GetMonthlyPayment();
                return string.Format(
                    "Monthly payment {0} for {1} months, {2} in total ({3} interest).",
                    Money(monthly), _years * 12, Money(loan.PaymentLeft), Money(loan.PaymentLeft - amount));
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static Models.Airlines.Airline Human
        {
            get { return GameObject.GetInstance().HumanAirline; }
        }

        private bool TryAmount(out double amount)
        {
            string t = (_amountText ?? "").Replace(",", "").Replace("$", "").Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out amount) && amount > 0;
        }

        private Loan NewLoan(double amount)
        {
            return new Loan(GameObject.GetInstance().GameTime, amount, _years * 12, GeneralHelpers.GetAirlineLoanRate(Human));
        }

        public void Reload()
        {
            var airline = Human;
            if (airline == null)
            {
                return;
            }

            double rate = GeneralHelpers.GetAirlineLoanRate(airline);
            double max = AirlineHelpers.GetMaxLoanAmount(airline);
            double debt = airline.Loans.Where(l => l.IsActive).Sum(l => l.Balance);
            double total = airline.Loans.Sum(l => l.Amount);

            Summary = string.Format(
                "Interest rate {0:0.0}%  |  you can borrow up to {1} in total (loans taken so far: {2})  |  still to pay back: {3}",
                rate, Money(max), Money(total), Money(debt));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(OfferText));

            var keep = Loans.ToDictionary(l => l.Loan, l => l.PayText);
            Loans.Clear();
            foreach (var loan in airline.Loans.Where(l => l.IsActive))
            {
                var captured = loan;
                var item = new LoanItem
                {
                    Loan = loan,
                    Text = string.Format(
                        "{0} borrowed {1}  |  {2}/month  |  {3} months left",
                        loan.Date.ToString("MMM yyyy"), Money(loan.Amount), Money(loan.GetMonthlyPayment()), loan.MonthsLeft),
                    Detail = "Still to pay: " + Money(loan.PaymentLeft) + "  |  pay off today for " + Money(loan.Balance) + " (no further interest)"
                };
                item.PayCommand = new RelayCommand(() => Pay(item, captured));
                item.PayOffCommand = new RelayCommand(() => PayAmount(captured, captured.Balance));
                string typed;
                if (keep.TryGetValue(loan, out typed))
                {
                    item.PayText = typed;
                }

                Loans.Add(item);
            }
        }

        private void TakeLoan()
        {
            double amount;
            if (!TryAmount(out amount))
            {
                Status = "Enter the amount to borrow as a number.";
                return;
            }

            if (amount < 5000000)
            {
                Status = "The smallest loan is " + Money(5000000) + ".";
                return;
            }

            var airline = Human;
            var loan = NewLoan(amount);

            if (!AirlineHelpers.CanApplyForLoan(airline, loan))
            {
                Status = "The bank refuses: your total loans would pass " + Money(AirlineHelpers.GetMaxLoanAmount(airline))
                         + ". Borrow less or pay back existing loans first.";
                return;
            }

            airline.AddLoan(loan);
            AirlineHelpers.AddAirlineInvoice(
                airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Loans, loan.Amount);

            Status = "You borrowed " + Money(amount) + " for " + _years + " years.";
            Reload();
            _changed();
        }

        private void Pay(LoanItem item, Loan loan)
        {
            double amount;
            string t = (item.PayText ?? "").Replace(",", "").Replace("$", "").Trim();
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            {
                Status = "Enter how much to pay back, as a number.";
                return;
            }

            PayAmount(loan, amount);
        }

        private void PayAmount(Loan loan, double amount)
        {
            var airline = Human;
            double paying = Math.Min(amount, loan.Balance);

            if (paying > airline.Money)
            {
                Status = "You don't have " + Money(paying) + " available.";
                return;
            }

            paying = loan.PayPrincipal(paying);
            AirlineHelpers.AddAirlineInvoice(
                airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Loans, -paying);

            if (loan.PaymentLeft <= 0)
            {
                airline.Loans.Remove(loan);
                Status = "Loan paid off.";
            }
            else
            {
                Status = "Paid back " + Money(paying) + ".";
            }

            Reload();
            _changed();
        }
    }
}
