using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class CharterAircraftItem
    {
        public string Text { get; set; }
        public ICommand SendCommand { get; set; }
    }

    public class CharterOfferItem
    {
        public string Name { get; set; }
        public string Terms { get; set; }
        public string NoAircraft { get; set; }
        public bool HasNoAircraft => NoAircraft != null;
        public List<CharterAircraftItem> Aircraft { get; set; }
        public ICommand DeclineCommand { get; set; }
    }

    /// <summary>Charter flights: one-off jobs for aircraft that have no routes.</summary>
    public class CharterViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status, _pendingText;
        private Action _pending;

        public CharterViewModel(Action changed)
        {
            _changed = changed;
            Offers = new ObservableCollection<CharterOfferItem>();
            Active = new ObservableCollection<string>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<CharterOfferItem> Offers { get; }
        public ObservableCollection<string> Active { get; }
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
            if (airline == null) return;

            CharterService.Refresh();
            Offers.Clear();
            foreach (var offer in CharterService.Offers.ToList())
            {
                var o = offer;
                var quotes = CharterService.Candidates(airline, o).Take(6).ToList();
                var item = new CharterOfferItem
                {
                    Name = o.Title,
                    Terms = string.Format(
                        "{0} {1} for {2}  |  {3} to {4}, {5}  |  pays {6}  |  answer by {7}",
                        o.Amount, o.IsCargo ? "cargo units" : "passengers", o.Customer, o.From.Profile.Name, o.To.Profile.Name,
                        DistanceFormatter.Km(o.Distance), Money(o.Fee), DateFormatter.Date(o.Deadline)),
                    Aircraft = new List<CharterAircraftItem>(),
                    DeclineCommand = new RelayCommand(() => { CharterService.Decline(o); Status = "You turned down " + o.Title + "."; Reload(); })
                };
                foreach (var q in quotes)
                {
                    var qq = q;
                    item.Aircraft.Add(
                        new CharterAircraftItem
                        {
                            Text = string.Format(
                                "{0} ({1}) at {2}{3}  |  {4} days  |  costs about {5}  |  profit about {6}",
                                qq.Aircraft.Name, qq.Aircraft.Airliner.Type.Name, qq.Aircraft.CurrentPosition.Profile.IATACode,
                                qq.FerryDistance > 1 ? ", ferry " + DistanceFormatter.Km(qq.FerryDistance) : "", qq.Days, Money(qq.Cost), Money(qq.Profit)),
                            SendCommand = new RelayCommand(() => AskSend(o, qq))
                        });
                }
                if (quotes.Count == 0)
                {
                    item.NoAircraft = "None of your aircraft can do this: it needs " + (o.IsCargo ? "a cargo aircraft" : "a passenger aircraft")
                                      + " without routes, with a full crew, enough room and range.";
                }
                Offers.Add(item);
            }

            Active.Clear();
            var game = GameObject.GetInstance();
            foreach (var f in airline.Fleet.Where(x => CharterService.IsOnCharter(x)))
            {
                Active.Add(string.Format("{0}: {1}, back {2}, pays {3}", f.Name, f.CharterText, DateFormatter.Date(f.CharterUntil), Money(f.CharterPay)));
            }

            OffersTitle = Offers.Count == 0 ? "No charter offers right now. New ones come about every week." : "Charter offers";
            ActiveTitle = Active.Count == 0 ? "No aircraft are out on a charter." : "Out on charter";
            OnPropertyChanged(nameof(OffersTitle));
            OnPropertyChanged(nameof(ActiveTitle));
        }

        private void AskSend(CharterOffer o, CharterQuote q)
        {
            SetPending(
                string.Format(
                    "Send {0} on \"{1}\"? It is away {2} days. You get {3} when it lands, and the flight costs about {4}.",
                    q.Aircraft.Name, o.Title, q.Days, Money(o.Fee), Money(q.Cost)),
                () =>
                {
                    string message;
                    CharterService.Accept(GameObject.GetInstance().HumanAirline, o, q.Aircraft, out message);
                    Status = message;
                    Reload();
                    _changed();
                });
        }
    }
}
