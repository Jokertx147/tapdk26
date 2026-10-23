using System;
using System.Collections.Generic;
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
                () => { OnPropertyChanged(nameof(CostText)); OnPropertyChanged(nameof(AllText)); });
            ApplyCommand = new RelayCommand(Apply);
            ApplyAllCommand = new RelayCommand(ApplyAll, () => SameType().Count > 1);
            CancelCommand = new RelayCommand(() => _done?.Invoke());
        }

        public string Title { get; }
        public CabinEditorViewModel Editor { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ApplyAllCommand { get; }

        /// <summary>The airline's other aircraft of this type, and this one.</summary>
        private List<FleetAirliner> SameType()
        {
            var airline = _aircraft.Airliner.Airline ?? GameObject.GetInstance().HumanAirline;
            return airline.Fleet.Where(f => f.Airliner.Type == _aircraft.Airliner.Type).ToList();
        }

        private double CostFor(FleetAirliner f)
        {
            return Math.Max(0, Editor.Price - CabinEditorViewModel.PriceOf(f.Airliner.Classes));
        }

        public bool HasOthers => SameType().Count > 1;

        public string AllText
        {
            get
            {
                var all = SameType();
                if (all.Count <= 1) return null;
                var stopped = all.Where(f => f.Status == FleetAirliner.AirlinerStatus.Stopped).ToList();
                return string.Format(
                    "You have {0} {1}. {2} of them stopped and can be refitted now, for {3} together. Flying ones are left as they are.",
                    all.Count, _aircraft.Airliner.Type.Name, stopped.Count,
                    Convert.ToString(new ValueCurrencyFormatter().Convert(stopped.Sum(CostFor))));
            }
        }

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

        private void ApplyAll()
        {
            var stopped = SameType().Where(f => f.Status == FleetAirliner.AirlinerStatus.Stopped).ToList();
            if (stopped.Count == 0)
            {
                Status = "None of them is stopped: stop the aircraft you want refitted first.";
                return;
            }

            var airline = GameObject.GetInstance().HumanAirline;
            double cost = stopped.Sum(CostFor);
            if (cost > airline.Money)
            {
                Status = "You can't afford to refit all " + stopped.Count + ".";
                return;
            }

            if (cost > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -cost);
            }

            foreach (var f in stopped)
            {
                f.Airliner.ClearAirlinerClasses();
                foreach (AirlinerClass c in Editor.BuildClasses())
                {
                    f.Airliner.AddAirlinerClass(c);
                }
            }

            _done?.Invoke();
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
