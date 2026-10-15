using System;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Presentation
{
    /// <summary>Changes the cabin of an aircraft the airline owns. Only an aircraft that is not flying can be refitted.</summary>
    public class CabinRefitViewModel : ObservableObject
    {
        private readonly FleetAirliner _aircraft;
        private readonly Action _done;
        private readonly double _oldPrice;
        private string _status;

        public CabinRefitViewModel(FleetAirliner aircraft, Action done)
        {
            _aircraft = aircraft;
            _done = done;
            _oldPrice = CabinEditorViewModel.PriceOf(aircraft.Airliner.Classes);
            Title = "Cabin of " + aircraft.Name + " (" + aircraft.Airliner.Type.Name + ")";
            Editor = new CabinEditorViewModel(
                (AirlinerPassengerType) aircraft.Airliner.Type,
                aircraft.Airliner.Classes,
                () => OnPropertyChanged(nameof(CostText)));
            ApplyCommand = new RelayCommand(Apply);
            CancelCommand = new RelayCommand(() => _done?.Invoke());
        }

        public string Title { get; }
        public CabinEditorViewModel Editor { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }

        private double Cost => Editor == null ? 0 : Math.Max(0, Editor.Price - _oldPrice);

        public string CostText
        {
            get
            {
                return "Refit cost " + Convert.ToString(new ValueCurrencyFormatter().Convert(Cost))
                       + " (you pay for the extras you add, nothing is paid back for what you take out).";
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private void Apply()
        {
            if (_aircraft.Status != FleetAirliner.AirlinerStatus.Stopped)
            {
                Status = "Stop the aircraft first: a flying aircraft can't be refitted.";
                return;
            }

            var airline = GameObject.GetInstance().HumanAirline;
            double cost = Cost;
            if (cost > airline.Money)
            {
                Status = "You can't afford the refit.";
                return;
            }

            if (cost > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -cost);
            }

            _aircraft.Airliner.ClearAirlinerClasses();
            foreach (AirlinerClass c in Editor.BuildClasses())
            {
                _aircraft.Airliner.AddAirlinerClass(c);
            }

            _done?.Invoke();
        }
    }
}
