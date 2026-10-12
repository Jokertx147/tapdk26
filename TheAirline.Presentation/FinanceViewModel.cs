using System;
using System.Collections.ObjectModel;
using System.Linq;
using TheAirline.Helpers;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Presentation
{
    public class FinanceRow
    {
        public string Label { get; set; }
        public string[] Values { get; set; }
        public bool IsTotal { get; set; }
    }

    /// <summary>Income and expenses of the airline per month (the last six months and the year so far).</summary>
    public class FinanceViewModel : ObservableObject
    {
        public ObservableCollection<FinanceRow> Rows { get; } = new ObservableCollection<FinanceRow>();
        public string[] Headers { get; private set; } = new string[0];

        public void Refresh()
        {
            var game = GameObject.GetInstance();
            var airline = game?.HumanAirline;
            if (airline == null)
            {
                return;
            }

            var now = game.GameTime;
            var months = Enumerable.Range(0, 6).Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-i)).ToList();
            var fmt = new ValueCurrencyFormatter();

            Func<double, string> money = v => Convert.ToString(fmt.Convert(v));

            Headers = months.Select(m => m.ToString("MMM yyyy")).Concat(new[] { "Year " + now.Year }).ToArray();
            OnPropertyChanged(nameof(Headers));

            var types = new[]
            {
                Invoice.InvoiceType.Tickets, Invoice.InvoiceType.OnFlightIncome, Invoice.InvoiceType.Wages,
                Invoice.InvoiceType.FlightExpenses, Invoice.InvoiceType.Maintenances, Invoice.InvoiceType.Fees,
                Invoice.InvoiceType.Rents, Invoice.InvoiceType.Loans, Invoice.InvoiceType.AirlineExpenses,
                Invoice.InvoiceType.Purchases, Invoice.InvoiceType.Total
            };

            Rows.Clear();
            foreach (var type in types)
            {
                var values = months.Select(m => money(airline.GetInvoicesAmountMonth(m.Year, m.Month, type))).ToList();

                double year = type == Invoice.InvoiceType.Total
                    ? airline.Invoices.GetYearlyAmount(now.Year)
                    : airline.Invoices.GetYearlyAmount(type, now.Year);
                values.Add(money(year));

                Rows.Add(
                    new FinanceRow
                    {
                        Label = type == Invoice.InvoiceType.Total ? "Result" : type.ToString(),
                        Values = values.ToArray(),
                        IsTotal = type == Invoice.InvoiceType.Total
                    });
            }
        }
    }
}
