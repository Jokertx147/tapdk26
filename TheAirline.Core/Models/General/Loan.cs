using System;
using System.Runtime.Serialization;
using TheAirline.Helpers;
using TheAirline.Infrastructure;

namespace TheAirline.Models.General
{
    //the class for a loan
    [Serializable]
    public class Loan : BaseModel
    {
        #region Constructors and Destructors

        public Loan(DateTime date, double amount, int length, double rate)
        {
            Amount = amount;
            Rate = rate;
            Length = length;
            Date = date;
            // everything still to be paid, interest included
            PaymentLeft = GetMonthlyPayment()*length;
        }

        private Loan(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("amount")]
        public double Amount { get; set; }

        [Versioning("date")]
        public DateTime Date { get; set; }

        public bool IsActive => HasPaymentLeft();

        [Versioning("length")]
        public int Length { get; set; }

        public double MonthlyPayment => GetMonthlyPayment();

        public int MonthsLeft => GetMonthsLeft();

        [Versioning("paymentleft")]
        public double PaymentLeft { get; set; }

        [Versioning("rate")]
        public double Rate { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //returns the monthly payment for the loan

        public double GetMonthlyPayment()
        {
            // the difficulty already scales the rate (GeneralHelpers.GetAirlineLoanRate), so it is not applied again here
            return MathHelpers.GetMonthlyPayment(Amount, Rate, Length);
        }

        private double MonthlyRate => Rate/1200;

        /// <summary>The principal still owed: what it costs to pay the loan off today, without the future interest.</summary>
        public double Balance
        {
            get
            {
                if (PaymentLeft <= 0)
                {
                    return 0;
                }

                double monthly = GetMonthlyPayment();
                double r = MonthlyRate;
                if (r <= 0 || monthly <= 0)
                {
                    return PaymentLeft;
                }

                double months = PaymentLeft/monthly;
                return Math.Min(PaymentLeft, monthly*(1 - Math.Pow(1 + r, -months))/r);
            }
        }

        /// <summary>Pays an extra amount off the principal; the loan gets shorter. Returns the amount actually used.</summary>
        public double PayPrincipal(double amount)
        {
            double balance = Balance;
            double paying = Math.Max(0, Math.Min(amount, balance));
            if (paying >= balance - 0.005)
            {
                PaymentLeft = 0;
                return balance;
            }

            double monthly = GetMonthlyPayment();
            double r = MonthlyRate;
            if (r <= 0 || monthly <= 0)
            {
                PaymentLeft -= paying;
            }
            else
            {
                double remaining = balance - paying;
                PaymentLeft = -monthly*Math.Log(1 - remaining*r/monthly)/Math.Log(1 + r);
            }

            return paying;
        }

        #endregion

        #region Methods

        private int GetMonthsLeft()
        {
            return (int) Math.Ceiling(PaymentLeft/MonthlyPayment);
        }

        private bool HasPaymentLeft()
        {
            return PaymentLeft > 0;
        }

        #endregion

        //checks if there is still payment left on the loan

        //returns the amount of months left on the loan
    }
}