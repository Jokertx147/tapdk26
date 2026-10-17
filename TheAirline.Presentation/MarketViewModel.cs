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
    public class AircraftItem
    {
        public AircraftItem(Airliner airliner)
        {
            Airliner = airliner;
            var f = new ValueCurrencyFormatter();
            Name = airliner.Type.Name;
            Year = airliner.BuiltDate.Year;
            Range = airliner.Type.Range;
            Seats = (airliner.Type as AirlinerPassengerType)?.MaxSeatingCapacity ?? 0;
            PriceText = Convert.ToString(f.Convert(airliner.GetPrice()));
            LeaseText = Convert.ToString(f.Convert(airliner.GetLeasingPrice())) + " / month";
            Details = string.Format("built {0}  |  {1}  |  range {2}  |  {3}", Year, airliner.Type is AirlinerCargoType ? "cargo " + ((AirlinerCargoType) airliner.Type).CargoSize + " units" : Seats + " seats", DistanceFormatter.Km(Range), PriceText);
        }

        public Airliner Airliner { get; }
        public string Name { get; }
        public int Year { get; }
        public long Range { get; }
        public int Seats { get; }
        public string PriceText { get; }
        public string LeaseText { get; }
        public string Details { get; }
    }

    /// <summary>Used aircraft market: buy an aircraft or lease it, placed at one of the airline's home airports.</summary>
    public class MarketViewModel : ObservableObject
    {
        private AircraftItem _selected;
        private Airport _homebase;
        private List<Airport> _homebases = new List<Airport>();
        private string _status;
        private string _filter = "";
        private List<AircraftItem> _all = new List<AircraftItem>();

        private readonly Action _changed;

        public MarketViewModel(Action changed = null)
        {
            _changed = changed;
            Aircraft = new ObservableCollection<AircraftItem>();
            BuyCommand = new RelayCommand(() => Place(false), () => _selected != null);
            LeaseCommand = new RelayCommand(() => Place(true), () => _selected != null);
            Reload();
        }

        public ObservableCollection<AircraftItem> Aircraft { get; }
        public ICommand BuyCommand { get; }
        public ICommand LeaseCommand { get; }

        public AircraftItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    LoadHomebases();
                    (BuyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (LeaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
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

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>Text filter on the aircraft name.</summary>
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

        public void Reload()
        {
            _all = Airliners.GetAirlinersForSale()
                            .Where(
                                a => a.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger
                                     || a.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Mixed
                                     || a.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo)
                            .OrderByDescending(a => a.BuiltDate.Year)
                            .ThenBy(a => a.Type.Name)
                            .Select(a => new AircraftItem(a))
                            .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var keep = _selected?.Airliner;
            Aircraft.Clear();
            foreach (var a in _all.Where(a => a.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).Take(400))
            {
                Aircraft.Add(a);
            }
            _selected = Aircraft.FirstOrDefault(a => a.Airliner == keep);
            OnPropertyChanged(nameof(Selected));
        }

        private void LoadHomebases()
        {
            if (_selected == null)
            {
                Homebases = new List<Airport>();
                Homebase = null;
                return;
            }

            Airline airline = GameObject.GetInstance().HumanAirline;
            long minRunway = _selected.Airliner.Type.MinRunwaylength;
            Homebases = AirlineHelpers.GetHomebases(airline, minRunway);
            Homebase = Homebases.FirstOrDefault();
            Status = Homebases.Count == 0
                ? "None of your airports can take this aircraft (runway, or no free service capacity). Get a contract at a bigger airport first."
                : null;
        }

        private void Place(bool lease)
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
            Airliner airliner = _selected.Airliner;

            if (airline.Contract != null && airline.Contract.Manufacturer != airliner.Type.Manufacturer)
            {
                Status = "You have a contract with " + airline.Contract.Manufacturer.Name
                         + ". End it first, or pick an aircraft from that manufacturer.";
                return;
            }

            double discount = airline.Contract?.Discount ?? 0;

            if (lease)
            {
                double deposit = airliner.GetLeasingPrice() * 2;
                if (deposit > airline.Money)
                {
                    Status = "You can't afford the leasing deposit (two months).";
                    return;
                }

                if (AirlineHelpers.NeedsNewRegistration(airline, airliner.TailNumber))
                {
                    airliner.TailNumber = airline.Profile.Country.TailNumbers.GetNextTailNumber(airline);
                }

                airline.AddAirliner(FleetAirliner.PurchasedType.Leased, airliner, _homebase);
                AirlineHelpers.AddAirlineInvoice(
                    airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Rents, -deposit);
                Status = "Leased " + _selected.Name + ".";
            }
            else
            {
                double price = airliner.GetPrice() * ((100 - discount) / 100);
                if (price > airline.Money)
                {
                    Status = "You can't afford this aircraft.";
                    return;
                }

                AirlineHelpers.BuyAirliner(airline, airliner, _homebase, discount);
                Status = "Bought " + _selected.Name + ".";
            }

            if (airline.Contract != null)
            {
                airline.Contract.PurchasedAirliners++;
            }

            string status = Status;
            _all.Remove(_selected);
            Selected = null;
            ApplyFilter();
            Status = status;
            _changed?.Invoke();
        }
    }
}
