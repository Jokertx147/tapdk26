using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Pilots;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class ServiceFacilityItem
    {
        public AirlineFacility Facility { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public bool Owned { get; set; }
        public string ActionText { get; set; }
        public ICommand Command { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>
    ///     The facilities of the airline itself (frequent flyer program, booking systems, Wi-Fi, newspapers...): what they cost,
    ///     what they unlock on the flights and buying or selling them. Pilot training facilities are bought here too.
    /// </summary>
    public class AirlineServicesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status, _pendingText;
        private Action _pending;
        private ServiceFacilityItem _training;

        public AirlineServicesViewModel(Action changed)
        {
            _changed = changed;
            Facilities = new ObservableCollection<ServiceFacilityItem>();
            Owned = new ObservableCollection<ServiceFacilityItem>();
            TrainingChoices = new ObservableCollection<ServiceFacilityItem>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            BuyTrainingCommand = new RelayCommand(() => { if (_training != null) AskBuy(_training.Facility); });
            Reload();
        }

        public ObservableCollection<ServiceFacilityItem> Facilities { get; }
        public ObservableCollection<ServiceFacilityItem> Owned { get; }
        public ObservableCollection<ServiceFacilityItem> TrainingChoices { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BuyTrainingCommand { get; }
        public string Summary { get; private set; }
        public bool HasPending => _pending != null;
        public bool HasOwned => Owned.Count > 0;

        public ServiceFacilityItem SelectedTraining
        {
            get { return _training; }
            set { SetProperty(ref _training, value); OnPropertyChanged(nameof(TrainingText)); }
        }

        public string TrainingText
        {
            get
            {
                return _training == null
                    ? "Pick an aircraft family to train pilots for."
                    : "Price " + Money(_training.Facility.Price) + ", " + Money(_training.Facility.MonthlyCost) + " a month. Pilots of your airline can then be trained for this family.";
            }
        }

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

        public static List<string> Unlocks(AirlineFacility facility)
        {
            return RouteFacilities.GetAllFacilities()
                .Where(r => r.Requires != null && r.Requires.Uid == facility.Uid)
                .Select(r => r.Type + ": " + r.Name)
                .ToList();
        }

        public void Reload()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null)
            {
                return;
            }

            int year = GameObject.GetInstance().GameTime.Year;
            Facilities.Clear();
            Owned.Clear();
            TrainingChoices.Clear();

            foreach (var f in AirlineFacilities.GetFacilities(x => x.FromYear <= year && !AirlineFacilities.IsDiscontinued(x, year)).OrderBy(x => x.Name))
            {
                var facility = f;
                bool owned = airline.Facilities.Exists(x => x.Uid == f.Uid);
                var unlocks = Unlocks(f);
                var item = new ServiceFacilityItem
                {
                    Facility = f,
                    Name = f.Name,
                    Owned = owned,
                    Text = f.Name + (owned ? "   (yours)" : ""),
                    Detail = string.Format(
                        "Price {0}  |  {1} a month  |  luxury {2}{3}",
                        Money(f.Price), Money(f.MonthlyCost), f.LuxuryLevel, unlocks.Count > 0 ? "  |  unlocks " + string.Join(", ", unlocks) : ""),
                    ActionText = owned ? "Sell" : "Buy",
                    Command = new RelayCommand(() => { if (owned) AskSell(facility); else AskBuy(facility); })
                };

                if (f is PilotTrainingFacility)
                {
                    if (!owned)
                    {
                        TrainingChoices.Add(item);
                    }
                }
                else
                {
                    Facilities.Add(item);
                }

                if (owned)
                {
                    Owned.Add(item);
                }
            }

            double monthly = airline.Facilities.Sum(f => f.MonthlyCost);
            Summary = string.Format("{0} facilities  |  {1} a month in running costs", airline.Facilities.Count, Money(monthly));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasOwned));
            _training = TrainingChoices.FirstOrDefault(t => _training != null && t.Facility.Uid == _training.Facility.Uid) ?? TrainingChoices.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedTraining));
            OnPropertyChanged(nameof(TrainingText));
        }

        private void AskBuy(AirlineFacility f)
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (f.Price > GameObject.GetInstance().HumanMoney)
            {
                Status = "Not enough money: " + f.Name + " costs " + Money(f.Price) + ".";
                return;
            }

            SetPending(
                "Buy " + f.Name + " for " + Money(f.Price) + "? It costs " + Money(f.MonthlyCost) + " a month to run.",
                () =>
                {
                    airline.AddFacility(f);
                    AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -f.Price);
                    Status = f.Name + " bought.";
                    Reload();
                    _changed();
                });
        }

        private void AskSell(AirlineFacility f)
        {
            var airline = GameObject.GetInstance().HumanAirline;
            SetPending(
                "Sell " + f.Name + "? You get no money back, and routes that use a service which needs it go back to the basic service.",
                () =>
                {
                    var own = airline.Facilities.Find(x => x.Uid == f.Uid);
                    if (own != null)
                    {
                        airline.RemoveFacility(own);
                    }

                    DropServicesThatNeed(airline, f);
                    Status = f.Name + " sold.";
                    Reload();
                    _changed();
                });
        }

        /// <summary>Routes that used a service made possible by a facility fall back to the basic service of that kind.</summary>
        public static int DropServicesThatNeed(Airline airline, AirlineFacility facility)
        {
            int changed = 0;
            foreach (var route in airline.Routes.OfType<PassengerRoute>())
            {
                foreach (var rc in route.Classes)
                {
                    foreach (var fac in rc.Facilities.ToList())
                    {
                        if (fac.Requires != null && fac.Requires.Uid == facility.Uid)
                        {
                            rc.AddFacility(RouteFacilities.GetBasicFacility(fac.Type));
                            changed++;
                        }
                    }
                }
            }

            return changed;
        }
    }
}
