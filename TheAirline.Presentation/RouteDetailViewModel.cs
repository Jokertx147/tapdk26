using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.General.Statistics;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class FareItem : ObservableObject
    {
        private string _price;

        public string ClassName { get; set; }
        public AirlinerClass.ClassType Type { get; set; }
        public string Seats { get; set; }

        public string Price
        {
            get { return _price; }
            set { SetProperty(ref _price, value); }
        }
    }

    /// <summary>One route of the airline: numbers, fares (editable) and delete.</summary>
    public class RouteDetailViewModel : ObservableObject
    {
        private readonly Route _route;
        private readonly Action _changed;
        private string _status;
        private bool _confirmDelete;
        private CreateRouteViewModel _assign;

        public RouteDetailViewModel(Route route, Action changed)
        {
            _route = route;
            _changed = changed;
            Fares = new ObservableCollection<FareItem>();

            Title = route.Destination1.Profile.Name + " - " + route.Destination2.Profile.Name;
            Code = route.Destination1.Profile.IATACode + " - " + route.Destination2.Profile.IATACode;
            IsPassenger = route is PassengerRoute;
            IsCargo = route is CargoRoute;
            if (IsCargo)
            {
                _cargoPrice = ((CargoRoute) route).PricePerUnit.ToString("0.00", CultureInfo.InvariantCulture);
            }

            SaveFaresCommand = new RelayCommand(SaveFares, () => IsPassenger);
            SaveCargoPriceCommand = new RelayCommand(SaveCargoPrice, () => IsCargo);
            AssignCommand = new RelayCommand(() => Assign = new CreateRouteViewModel(_route, OnAssignDone), () => CanAssign);
            MoveCommand = new RelayCommand(() => Assign = new CreateRouteViewModel(_route, OnAssignDone, true), () => CanMove);
            DeleteCommand = new RelayCommand(() => ConfirmDelete = true);
            ConfirmDeleteCommand = new RelayCommand(Delete);
            CancelDeleteCommand = new RelayCommand(() => ConfirmDelete = false);

            RefreshNumbers();
            LoadFares();
        }

        public string Title { get; }
        public string Code { get; }
        public bool IsPassenger { get; }
        public bool IsCargo { get; }
        public ICommand SaveCargoPriceCommand { get; }

        private string _cargoPrice;

        /// <summary>The price per unit of cargo of a cargo route.</summary>
        public string CargoPrice
        {
            get { return _cargoPrice; }
            set { SetProperty(ref _cargoPrice, value); }
        }

        private void SaveCargoPrice()
        {
            double price;
            var cr = _route as CargoRoute;
            if (cr == null)
            {
                return;
            }

            if (!double.TryParse((_cargoPrice ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out price) || price <= 0)
            {
                Status = "The price per unit must be a number above 0 (for example 8.50).";
                return;
            }

            cr.PricePerUnit = price;
            Status = "Price saved.";
        }
        public string Numbers { get; private set; }
        public string AircraftText { get; private set; }
        public ObservableCollection<FareItem> Fares { get; }

        public ICommand SaveFaresCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelDeleteCommand { get; }

        public bool ConfirmDelete
        {
            get { return _confirmDelete; }
            private set
            {
                if (SetProperty(ref _confirmDelete, value))
                {
                    OnPropertyChanged(nameof(NotConfirmDelete));
                }
            }
        }

        public bool NotConfirmDelete
        {
            get { return !_confirmDelete; }
        }

        public ICommand AssignCommand { get; }
        public ICommand MoveCommand { get; }

        /// <summary>The form to put an aircraft on this route, or null when it is closed.</summary>
        public CreateRouteViewModel Assign
        {
            get { return _assign; }
            private set
            {
                if (SetProperty(ref _assign, value))
                {
                    OnPropertyChanged(nameof(HasAssign));
                    OnPropertyChanged(nameof(ShowAssignButton));
                    OnPropertyChanged(nameof(ShowMoveButton));
                }
            }
        }

        public bool CanMove => _route.HasAirliner;

        public bool ShowMoveButton => CanMove && _assign == null;

        public bool HasAssign => _assign != null;

        /// <summary>True when no aircraft flies this route.</summary>
        public bool CanAssign => !_route.HasAirliner;

        public bool ShowAssignButton => CanAssign && _assign == null;

        private void OnAssignDone(string message)
        {
            Assign = null;
            if (message != null)
            {
                Status = message;
                _changed();
            }

            RefreshNumbers();
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

        /// <summary>Updates the figures (passengers, profit, aircraft); fares being edited are left alone.</summary>
        public void RefreshNumbers()
        {
            double passengers = 0, capacity = 0;
            try
            {
                passengers = Convert.ToDouble(
                    _route.Statistics.GetTotalValue(StatisticsTypes.GetStatisticsType(IsCargo ? "Cargo" : "Passengers")));
                capacity = Convert.ToDouble(_route.Statistics.GetTotalValue(StatisticsTypes.GetStatisticsType("Capacity")));
            }
            catch (Exception)
            {
                // no statistics yet
            }

            string load = capacity > 0 ? string.Format("{0:0}%", 100 * passengers / capacity) : "no flights yet";
            Numbers = string.Format(
                "{0:0} km  |  {1:0} {2} carried  |  load factor {3}  |  profit {4}",
                _route.GetDistance(), passengers, IsCargo ? "units of cargo" : "passengers", load, Money(_route.Balance));

            var airliners = _route.GetAirliners();
            AircraftText = airliners.Count == 0
                ? "No aircraft is scheduled on this route."
                : "Aircraft: " + string.Join(", ", airliners.Select(a => a.Name + " (" + a.Status + ")"));

            OnPropertyChanged(nameof(Numbers));
            OnPropertyChanged(nameof(AircraftText));
            OnPropertyChanged(nameof(CanAssign));
            OnPropertyChanged(nameof(ShowAssignButton));
            OnPropertyChanged(nameof(CanMove));
            OnPropertyChanged(nameof(ShowMoveButton));
        }

        private void LoadFares()
        {
            Fares.Clear();
            var pr = _route as PassengerRoute;
            if (pr != null)
            {
                foreach (var c in pr.Classes)
                {
                    Fares.Add(
                        new FareItem
                        {
                            Type = c.Type,
                            ClassName = c.Type.ToString(),
                            Seats = c.Seating.ToString(),
                            Price = c.FarePrice.ToString("0.00", CultureInfo.InvariantCulture)
                        });
                }
            }
        }

        private void SaveFares()
        {
            var pr = _route as PassengerRoute;
            if (pr == null)
            {
                return;
            }

            foreach (var f in Fares)
            {
                double price;
                if (!double.TryParse(f.Price.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out price) || price < 0)
                {
                    Status = "The " + f.ClassName + " fare must be a number (for example 129.50).";
                    return;
                }
            }

            foreach (var f in Fares)
            {
                pr.GetRouteAirlinerClass(f.Type).FarePrice =
                    double.Parse(f.Price.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            Status = "Fares saved.";
        }

        private void Delete()
        {
            var airline = GameObject.GetInstance().HumanAirline;

            var contract = airline.SpecialContracts.Find(s => s.Routes.Contains(_route));
            contract?.Routes.Remove(_route);

            var airliners = _route.GetAirliners();
            airline.RemoveRoute(_route);
            foreach (var a in airliners)
            {
                a.RemoveRoute(_route);
                if (a.Routes.Count == 0)
                {
                    a.Status = FleetAirliner.AirlinerStatus.Stopped;
                }
            }

            _changed();
        }
    }
}
