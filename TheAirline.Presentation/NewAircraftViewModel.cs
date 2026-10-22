using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Presentation
{
    public class NewTypeItem
    {
        public NewTypeItem(AirlinerType type)
        {
            Type = type;
            Name = type.Name;
            Seats = (type as AirlinerPassengerType)?.MaxSeatingCapacity ?? 0;
            Details = string.Format(
                "{0}  |  {1}  |  range {2}  |  {3}",
                type.Manufacturer?.Name,
                type is AirlinerCargoType ? "cargo " + ((AirlinerCargoType) type).CargoSize + " units" : Seats + " seats",
                DistanceFormatter.Km(type.Range),
                Convert.ToString(new ValueCurrencyFormatter().Convert((double) type.Price)));
        }

        public AirlinerType Type { get; }
        public string Name { get; }
        public int Seats { get; }
        public string Details { get; }
    }

    public class ManufacturerItem
    {
        public string Name { get; set; }
        public string Text { get; set; }
    }

    public class EngineChoice
    {
        public EngineType Engine { get; set; }
        public string Text { get; set; }
    }

    /// <summary>Order new aircraft from the manufacturers, delivered at a later date.</summary>
    public class NewAircraftViewModel : ObservableObject
    {
        private readonly Action _changed;
        private List<NewTypeItem> _all = new List<NewTypeItem>();
        private NewTypeItem _selected;
        private string _filter = "";
        private int _amount = 1;
        private bool _downPayment;
        private bool _terminateContract;
        private Airport _homebase;
        private List<Airport> _homebases = new List<Airport>();
        private EngineChoice _engine;
        private List<EngineChoice> _engines = new List<EngineChoice>();
        private string _status;
        private string _summary;
        private CabinEditorViewModel _cabin;

        public NewAircraftViewModel(Action changed = null)
        {
            _changed = changed;
            Types = new ObservableCollection<NewTypeItem>();
            Manufacturers = new ObservableCollection<ManufacturerItem>();
            OrderCommand = new RelayCommand(Order, () => _selected != null);
            Reload();
        }

        public ObservableCollection<NewTypeItem> Types { get; }
        public ObservableCollection<ManufacturerItem> Manufacturers { get; }
        private ManufacturerItem _manufacturer;

        /// <summary>Only the types of this manufacturer are listed (null or the first entry means all).</summary>
        public ManufacturerItem Manufacturer
        {
            get { return _manufacturer; }
            set
            {
                if (SetProperty(ref _manufacturer, value))
                {
                    ApplyFilter();
                }
            }
        }
        public ICommand OrderCommand { get; }

        /// <summary>Set by the airline page: puts a type into the comparison.</summary>
        public Action<AirlinerType> CompareRequested { get; set; }

        private ICommand _compare;
        public ICommand CompareCommand => _compare ?? (_compare = new RelayCommand(() => { if (_selected != null) CompareRequested?.Invoke(_selected.Type); }));

        public NewTypeItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    LoadOptions();
                    (OrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value ?? ""))
                {
                    ApplyFilter();
                }
            }
        }

        public int Amount
        {
            get { return _amount; }
            set
            {
                if (SetProperty(ref _amount, Math.Max(1, Math.Min(100, value))))
                {
                    UpdateSummary();
                }
            }
        }

        public bool DownPayment
        {
            get { return _downPayment; }
            set
            {
                if (SetProperty(ref _downPayment, value))
                {
                    UpdateSummary();
                }
            }
        }

        public bool TerminateContract
        {
            get { return _terminateContract; }
            set { SetProperty(ref _terminateContract, value); }
        }

        public List<Airport> Homebases
        {
            get { return _homebases; }
            private set { SetProperty(ref _homebases, value); }
        }

        public Airport Homebase
        {
            get { return _homebase; }
            set { SetProperty(ref _homebase, value); }
        }

        public List<EngineChoice> Engines
        {
            get { return _engines; }
            private set { SetProperty(ref _engines, value); }
        }

        public EngineChoice Engine
        {
            get { return _engine; }
            set
            {
                if (SetProperty(ref _engine, value))
                {
                    UpdateSummary();
                }
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>The cabin layout of the selected type (null for types without passenger seats).</summary>
        public CabinEditorViewModel Cabin
        {
            get { return _cabin; }
            private set
            {
                if (SetProperty(ref _cabin, value))
                {
                    OnPropertyChanged(nameof(HasCabin));
                }
            }
        }

        public bool HasCabin => _cabin != null;

        /// <summary>Price, discount and delivery date of the order as it stands.</summary>
        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        /// <summary>Set when the airline's manufacturer contract blocks ordering this type.</summary>
        public string ContractWarning
        {
            get
            {
                Airline airline = GameObject.GetInstance().HumanAirline;
                if (_selected == null || airline.Contract == null || airline.Contract.Manufacturer == _selected.Type.Manufacturer)
                {
                    return null;
                }

                return string.Format(
                    "You have a contract with {0}. Tick the box to end it (fee {1}) and order anyway.",
                    airline.Contract.Manufacturer.Name,
                    Convert.ToString(new ValueCurrencyFormatter().Convert(airline.Contract.GetTerminationFee())));
            }
        }

        public bool HasContractWarning => ContractWarning != null;

        public void Reload()
        {
            DateTime now = GameObject.GetInstance().GameTime;
            _all = AirlinerTypes.GetTypes(
                                    t => t.Produced.From <= now && t.Produced.To > now
                                         && (t.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger
                                             || t.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed
                                             || t.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo))
                                .OrderBy(t => t.Manufacturer?.Name)
                                .ThenBy(t => t.Name)
                                .Select(t => new NewTypeItem(t))
                                .ToList();

            string keepName = _manufacturer?.Name;
            Manufacturers.Clear();
            Manufacturers.Add(new ManufacturerItem { Name = null, Text = "All manufacturers (" + _all.Count + ")" });
            foreach (var g in _all.GroupBy(t => t.Type.Manufacturer?.Name ?? "Other").OrderBy(g => g.Key))
            {
                Manufacturers.Add(new ManufacturerItem { Name = g.Key, Text = g.Key + " (" + g.Count() + ")" });
            }

            _manufacturer = Manufacturers.FirstOrDefault(m => m.Name == keepName) ?? Manufacturers[0];
            OnPropertyChanged(nameof(Manufacturer));
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var keep = _selected?.Type;
            Types.Clear();
            foreach (var t in _all.Where(
                t => (_manufacturer?.Name == null || (t.Type.Manufacturer?.Name ?? "Other") == _manufacturer.Name)
                     && (t.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0
                         || (t.Details ?? "").IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                Types.Add(t);
            }

            _selected = Types.FirstOrDefault(t => t.Type == keep);
            OnPropertyChanged(nameof(Selected));
        }

        private void LoadOptions()
        {
            OnPropertyChanged(nameof(ContractWarning));
            OnPropertyChanged(nameof(HasContractWarning));
            TerminateContract = false;

            if (_selected == null)
            {
                Homebases = new List<Airport>();
                Homebase = null;
                Engines = new List<EngineChoice>();
                Engine = null;
                Cabin = null;
                Summary = null;
                return;
            }

            Airline airline = GameObject.GetInstance().HumanAirline;
            AirlinerType type = _selected.Type;
            Homebases = AirlineHelpers.GetHomebases(airline, type.MinRunwaylength);
            Homebase = Homebases.FirstOrDefault();
            Status = Homebases.Count == 0
                ? "None of your airports can take this aircraft (runway, or no free service capacity)."
                : null;

            int year = GameObject.GetInstance().GameTime.Year;
            EngineType standard = EngineTypes.GetStandardEngineType(type, year);
            var list = EngineTypes.GetEngineTypes(type, year)
                                  .OrderBy(e => e.Price)
                                  .Select(
                                      e => new EngineChoice
                                      {
                                          Engine = e,
                                          Text = e.Model + (e == standard ? " (standard)" : " (+" + Convert.ToString(
                                                     new ValueCurrencyFormatter().Convert((double) (e.Price - (standard?.Price ?? 0)))) + ")")
                                      })
                                  .ToList();
            Engines = list;
            Engine = list.FirstOrDefault(e => e.Engine == standard) ?? list.FirstOrDefault();
            var passengerType = type as AirlinerPassengerType;
            Cabin = passengerType != null ? new CabinEditorViewModel(passengerType, UpdateSummary) : null;
            UpdateSummary();
        }

        //the delivery date from the production rate, as in the Windows version
        public static DateTime GetDeliveryDate(AirlinerType type, int amount)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            var date = new DateTime(now.Year, now.Month, now.Day);
            int rate = Math.Max(1, type.ProductionRate);
            if (amount <= rate / 4)
            {
                return date.AddMonths(3);
            }

            for (int i = (rate / 4) + 1; i <= amount; i++)
            {
                date = date.AddDays(Math.Round((double) (365 / rate), 0, MidpointRounding.AwayFromZero));
            }

            return date;
        }

        private double UnitPrice()
        {
            double price = _selected.Type.Price;
            if (_engine?.Engine != null)
            {
                EngineType standard = EngineTypes.GetStandardEngineType(_selected.Type, GameObject.GetInstance().GameTime.Year);
                price += _engine.Engine.Price - (standard?.Price ?? _engine.Engine.Price);
            }

            return price + (_cabin?.Price ?? 0);
        }

        private double TotalPrice(bool contracted)
        {
            double total = UnitPrice() * _amount * (1 - GeneralHelpers.GetAirlinerOrderDiscount(_amount));
            if (contracted)
            {
                total *= (100 - GameObject.GetInstance().HumanAirline.Contract.Discount) / 100;
            }

            return total;
        }

        private bool IsContracted()
        {
            Airline airline = GameObject.GetInstance().HumanAirline;
            return _selected != null && airline.Contract != null && airline.Contract.Manufacturer == _selected.Type.Manufacturer;
        }

        private void UpdateSummary()
        {
            if (_selected == null)
            {
                Summary = null;
                return;
            }

            var f = new ValueCurrencyFormatter();
            double total = TotalPrice(IsContracted());
            double down = total * (GameObject.GetInstance().Difficulty.PriceLevel / 10);
            DateTime delivery = GetDeliveryDate(_selected.Type, _amount);
            double discount = 1 - total / (UnitPrice() * _amount);

            Summary = string.Format(
                "{0} x {1}: total {2}{3}.  Delivery {4}.{5}" + (_cabin != null ? "  Cabin " + _cabin.Layout + "." : ""),
                _amount,
                _selected.Name,
                Convert.ToString(f.Convert(total)),
                discount > 0.0001 ? string.Format(" (incl. {0:0.0}% discount)", discount * 100) : "",
                DateFormatter.Date(delivery),
                _downPayment
                    ? "  Pay " + Convert.ToString(f.Convert(down)) + " now, the aircraft price on delivery day."
                    : "  Paid in full now.");
        }

        private void Order()
        {
            if (_selected == null)
            {
                return;
            }

            if (_homebase == null)
            {
                Status = "Pick a home airport for the aircraft first.";
                return;
            }

            Airline airline = GameObject.GetInstance().HumanAirline;
            AirlinerType type = _selected.Type;
            DateTime now = GameObject.GetInstance().GameTime;

            bool contracted = IsContracted();
            double fee = 0;
            if (airline.Contract != null && !contracted)
            {
                if (!_terminateContract)
                {
                    OnPropertyChanged(nameof(ContractWarning));
                    Status = ContractWarning;
                    return;
                }

                fee = airline.Contract.GetTerminationFee();
            }

            double total = TotalPrice(contracted);
            double down = total * (GameObject.GetInstance().Difficulty.PriceLevel / 10);
            double now_pay = _downPayment ? down : total;

            if (now_pay + fee > airline.Money)
            {
                Status = "You can't afford this order" + (_downPayment ? " (down payment)." : ".");
                return;
            }

            if (fee > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -fee);
                airline.Contract = null;
            }

            DateTime delivery = GetDeliveryDate(type, _amount);
            for (int i = 0; i < _amount; i++)
            {
                var airliner = new Airliner(
                    Guid.NewGuid().ToString(),
                    type,
                    airline.Profile.Country.TailNumbers.GetNextTailNumber(airline),
                    delivery);
                airliner.EngineType = _engine?.Engine;
                if (_cabin != null)
                {
                    airliner.ClearAirlinerClasses();
                    foreach (AirlinerClass aClass in _cabin.BuildClasses())
                    {
                        airliner.AddAirlinerClass(aClass);
                    }
                }

                Airliners.AddAirliner(airliner);
                airline.AddAirliner(
                    new FleetAirliner(
                        _downPayment ? FleetAirliner.PurchasedType.BoughtDownPayment : FleetAirliner.PurchasedType.Bought,
                        now,
                        airline,
                        airliner,
                        _homebase)
                    {
                        RemainingPayment = _downPayment ? Math.Max(0, (total - down) / _amount) : 0
                    });
            }

            if (contracted)
            {
                airline.Contract.PurchasedAirliners += _amount;
            }

            AirlineHelpers.AddAirlineInvoice(airline, now, Invoice.InvoiceType.Purchases, -now_pay);

            string text = string.Format("Ordered {0} x {1}, delivery {2}.", _amount, _selected.Name, DateFormatter.Date(delivery));
            Amount = 1;
            DownPayment = false;
            LoadOptions();
            Status = text;
            _changed?.Invoke();
        }
    }
}
