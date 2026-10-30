using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheAirline.Infrastructure;

namespace TheAirline.Models.General.Finances
{
    //the class for a collection of invoices
    [Serializable]
    public class Invoices : BaseModel
    {
        #region Constructors and Destructors

        public Invoices()
        {
            MonthlyInvoices = new List<MonthlyInvoice>();
        }

        private Invoices(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("invoices")]
        public List<MonthlyInvoice> MonthlyInvoices { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public static List<double> GetInvoiceList()
        {
            var invoices = GameObject.GetInstance().HumanAirline.GetInvoices().MonthlyInvoices;
            lock (invoices)
            {
                return invoices.Select(invoice => invoice.Amount).ToList();
            }
        }

        //adds an invoice to the invoices
        public void AddInvoice(Invoice invoice)
        {
            AddInvoice(invoice.Type, invoice.Date.Year, invoice.Date.Month, invoice.Date.Day, invoice.Amount);
        }

        // the day's amount of each type, found without going through every amount the airline ever had (thousands
        // after a few years, for every payment of every flight); made again when the list changed from outside
        [NonSerialized]
        private Dictionary<long, MonthlyInvoice> _index;

        [NonSerialized]
        private int _indexCount = -1;

        [NonSerialized]
        private List<MonthlyInvoice> _indexList;

        private static long Key(Invoice.InvoiceType type, int year, int month, int day)
        {
            return (((long) year*13 + month)*32 + day)*256 + (int) type;
        }

        public void AddInvoice(Invoice.InvoiceType type, int year, int month, int day, double amount)
        {
            lock (MonthlyInvoices)
            {
                if (_index == null || _indexCount != MonthlyInvoices.Count || !ReferenceEquals(_indexList, MonthlyInvoices))
                {
                    _index = new Dictionary<long, MonthlyInvoice>();
                    foreach (MonthlyInvoice m in MonthlyInvoices)
                    {
                        long k = Key(m.Type, m.Year, m.Month, m.Day);
                        if (!_index.ContainsKey(k)) _index[k] = m; // the first, as Find took it
                    }
                    _indexList = MonthlyInvoices;
                }

                MonthlyInvoice mInvoice;
                long key = Key(type, year, month, day);
                if (_index.TryGetValue(key, out mInvoice))
                {
                    mInvoice.Amount += amount;
                }
                else
                {
                    mInvoice = new MonthlyInvoice(type, year, month, day, amount);
                    MonthlyInvoices.Add(mInvoice);
                    _index[key] = mInvoice;
                }
                _indexCount = MonthlyInvoices.Count;
            }
        }

        public Boolean Contains(Invoice.InvoiceType type, int year, int month)
        {
            Boolean contains;
            lock (MonthlyInvoices)
            {
                contains = MonthlyInvoices.Exists(m => m.Month == month && m.Year == year && m.Type == type);
            }

            return contains;
        }

        public Boolean Contains(Invoice.InvoiceType type, int year, int month, int day)
        {
            Boolean contains;
            lock (MonthlyInvoices)
            {
                contains = MonthlyInvoices.Exists(m => m.Month == month && m.Year == year && m.Day == day && m.Type == type);
            }

            return contains;
        }

        //returns the yearly amount for a given type and year

        //returns the total amount for a given type
        public double GetAmount(Invoice.InvoiceType type)
        {
            lock (MonthlyInvoices)
            {
                return MonthlyInvoices.FindAll(i => i.Type == type).Sum(i => i.Amount);
            }
        }

        //returns the total amount for a given year, month and type
        public double GetAmount(Invoice.InvoiceType type, int year, int month)
        {
            lock (MonthlyInvoices)
            {
                // amounts are kept per day: add up every day of the month (it used to return the first day only)
                return MonthlyInvoices.Where(m => m.Month == month && m.Year == year && m.Type == type).Sum(m => m.Amount);
            }
        }

        //returns the total amount for a given year and month
        public double GetAmount(int year, int month)
        {
            lock (MonthlyInvoices)
            {
                return MonthlyInvoices.FindAll(m => m.Month == month && m.Year == year).Sum(m => m.Amount);
            }
        }

        //returns the total amount of invoices
        public double GetAmount()
        {
            lock (MonthlyInvoices)
            {
                return MonthlyInvoices.Sum(m => m.Amount);
            }
        }

        public double GetYearlyAmount(Invoice.InvoiceType type, int year)
        {
            lock (MonthlyInvoices)
            {
                return MonthlyInvoices.FindAll(i => i.Type == type && i.Year == year).Sum(i => i.Amount);
            }
        }

        //return the yearly amount for an year
        public double GetYearlyAmount(int year)
        {
            lock (MonthlyInvoices)
            {
                return MonthlyInvoices.FindAll(i => i.Year == year).Sum(i => i.Amount);
            }
        }

        #endregion

        //returns if the invoices contains a month, year and type element
    }
}