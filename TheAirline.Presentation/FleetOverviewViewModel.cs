using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;

namespace TheAirline.Presentation
{
    public class OverviewRow
    {
        public FleetAirliner Aircraft { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Cabin { get; set; }
        public string Status { get; set; }
        public string Homebase { get; set; }
        public string Ownership { get; set; }
        public string Age { get; set; }
        public string Condition { get; set; }
        public int Routes { get; set; }
        public bool IsIdle { get; set; }
        public bool IsOnOrder { get; set; }
        public string Crew { get; set; }
        public string Value { get; set; }
        public double ValueNumber { get; set; }
        public int AgeNumber { get; set; }
        public double ConditionNumber { get; set; }
    }

    public class OverviewRouteLine
    {
        public string Name { get; set; }
        public string Balance { get; set; }
        public string Filling { get; set; }
    }

    /// <summary>The list of the aircraft the airline owns, with details and the actions to start, stop, refit and sell them.</summary>
    public class FleetOverviewViewModel : ObservableObject
    {
        public static readonly string[] SortNames = { "Name", "Type", "Age", "Condition", "Value", "Routes" };

        private readonly Action _changed;
        private readonly Action<FleetAirliner> _refit;
        private readonly Action<FleetAirliner> _schedule;
        private string _search = "", _sortBy = "Name", _status, _pendingText, _signature;
        private OverviewRow _selected;
        private Action _pending;

        public FleetOverviewViewModel(Action changed, Action<FleetAirliner> refit, Action<FleetAirliner> schedule = null)
        {
            _changed = changed;
            _refit = refit;
            _schedule = schedule;
            ScheduleCommand = new RelayCommand(() => { if (_selected != null) _schedule?.Invoke(_selected.Aircraft); });
            Rows = new ObservableCollection<OverviewRow>();
            Details = new ObservableCollection<string>();
            RouteLines = new ObservableCollection<OverviewRouteLine>();
            StartCommand = new RelayCommand(() => Do(f => Status = TryStart(f)));
            StopCommand = new RelayCommand(() => Do(f => { f.Status = FleetAirliner.AirlinerStatus.Stopped; Status = f.Name + " stopped."; }));
            CabinCommand = new RelayCommand(() => { if (_selected != null) _refit?.Invoke(_selected.Aircraft); });
            SellCommand = new RelayCommand(AskSell);
            ConfirmCommand = new RelayCommand(() => { var p = _pending; ClearPending(); p?.Invoke(); });
            CancelCommand = new RelayCommand(ClearPending);
            Reload();
        }

        public ObservableCollection<OverviewRow> Rows { get; }
        public ObservableCollection<string> Details { get; }
        public ObservableCollection<OverviewRouteLine> RouteLines { get; }
        public string[] Sorts => SortNames;

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand CabinCommand { get; }
        public ICommand ScheduleCommand { get; }
        public bool CanSchedule => _selected != null && _selected.Aircraft.Airliner.BuiltDate <= GameObject.GetInstance().GameTime;
        public ICommand SellCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Search
        {
            get { return _search; }
            set { if (SetProperty(ref _search, value ?? "")) Reload(); }
        }

        public string SortBy
        {
            get { return _sortBy; }
            set { if (value != null && SetProperty(ref _sortBy, value)) Reload(); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public string PendingText
        {
            get { return _pendingText; }
            private set { if (SetProperty(ref _pendingText, value)) OnPropertyChanged(nameof(HasPending)); }
        }

        public bool HasPending => _pendingText != null;
        public bool HasSelection => _selected != null;
        public bool HasRows => Rows.Count > 0;
        public string Title { get; private set; }

        public bool CanStart => _selected != null && _selected.Aircraft.Status == FleetAirliner.AirlinerStatus.Stopped
                                && _selected.Aircraft.Airliner.BuiltDate <= GameObject.GetInstance().GameTime;
        public bool CanStop => _selected != null && _selected.Aircraft.Status != FleetAirliner.AirlinerStatus.Stopped
                               && _selected.Aircraft.Airliner.BuiltDate <= GameObject.GetInstance().GameTime;
        public bool CanCabin => _selected != null && _selected.Aircraft.Status == FleetAirliner.AirlinerStatus.Stopped
                                && _selected.Aircraft.Airliner.Type is AirlinerPassengerType;
        public bool CanSell => _selected != null;

        /// <summary>An idle aircraft scheduled to fly from another airport gets a ferry flight there first (fuel cost, takes time).</summary>
        public bool AutoFerry
        {
            get { return GameObject.GetInstance().HumanAirline?.AutoFerry ?? true; }
            set
            {
                var airline = GameObject.GetInstance().HumanAirline;
                if (airline == null || airline.AutoFerry == value) return;
                airline.AutoFerry = value;
                OnPropertyChanged(nameof(AutoFerry));
                Status = value ? "Automatic ferry flights are on." : "Automatic ferry flights are off: aircraft just start from the first airport of their schedule.";
            }
        }
        public string SellText { get; private set; } = "Sell";

        public OverviewRow Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    ClearPending();
                    LoadDetails();
                }
            }
        }

        /// <summary>Starts an aircraft; returns what happened as a sentence.</summary>
        public static string TryStart(FleetAirliner f)
        {
            if (f.Airliner.BuiltDate > GameObject.GetInstance().GameTime)
            {
                return f.Name + " has not been delivered yet (" + DateFormatter.Date(f.Airliner.BuiltDate) + ").";
            }
            if (f.Routes.Count == 0)
            {
                return f.Name + " has no route. Create a route for it first.";
            }
            if (f.Pilots.Count < f.Airliner.Type.CockpitCrew)
            {
                return f.Name + " needs " + f.Airliner.Type.CockpitCrew + " pilots (it has " + f.Pilots.Count + "). Hire them under Staff > Pilots.";
            }
            f.Status = GameObject.GetInstance().DayRoundEnabled
                ? FleetAirliner.AirlinerStatus.OnRoute
                : FleetAirliner.AirlinerStatus.ToRouteStart;
            return f.Name + " is flying.";
        }

        private void Do(Action<FleetAirliner> action)
        {
            if (_selected == null) return;
            action(_selected.Aircraft);
            _changed?.Invoke();
        }

        private void ClearPending()
        {
            _pending = null;
            PendingText = null;
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private void AskSell()
        {
            if (_selected == null) return;
            var f = _selected.Aircraft;
            if (AircraftDealService.IsLeasedOut(f))
            {
                Status = f.Name + " is leased out. End the lease under Aircraft deals first.";
                return;
            }
            if (f.Status != FleetAirliner.AirlinerStatus.Stopped)
            {
                Status = "Stop " + f.Name + " first: a flying aircraft can't be sold.";
                return;
            }

            if (f.Purchased == FleetAirliner.PurchasedType.Bought)
            {
                long price = f.Airliner.GetPrice();
                PendingText = "Sell " + f.Name + " (" + f.Airliner.Type.Name + ") for " + Money(price) + "? Its routes are removed.";
                _pending = () => Sell(f, price);
            }
            else if (f.Purchased == FleetAirliner.PurchasedType.Leased)
            {
                PendingText = "Return the leased " + f.Name + " (" + f.Airliner.Type.Name + ") to the lessor? Its routes are removed.";
                _pending = () => Sell(f, 0);
            }
            else
            {
                PendingText = "Give up " + f.Name + " (" + f.Airliner.Type.Name + ")? It is not paid in full, so nothing is paid back. Its routes are removed.";
                _pending = () => Sell(f, 0);
            }
        }

        private void Sell(FleetAirliner f, long price)
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (f.Status != FleetAirliner.AirlinerStatus.Stopped || !airline.Fleet.Contains(f))
            {
                return;
            }

            foreach (var route in f.Routes.ToList())
            {
                f.RemoveRoute(route);
            }

            airline.RemoveAirliner(f);
            if (price > 0)
            {
                AirlineHelpers.AddAirlineInvoice(airline, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, price);
            }

            foreach (var pilot in f.Pilots.ToList())
            {
                f.RemovePilot(pilot);
            }

            Status = price > 0 ? f.Name + " sold for " + Money(price) + "." : f.Name + " is gone.";
            _selected = null;
            _signature = null;
            _changed?.Invoke();
        }

        /// <summary>Read the fleet again (cheap when nothing changed).</summary>
        public void Reload()
        {
            var game = GameObject.GetInstance();
            var airline = game?.HumanAirline;
            if (airline == null) return;

            var keep = _selected?.Aircraft;
            var fleet = airline.Fleet.ToList();
            string term = _search.Trim().ToLowerInvariant();

            var rows = fleet.Select(f => MakeRow(f, game)).Where(
                r => term.Length == 0 || (r.Name + " " + r.Type + " " + r.Homebase).ToLowerInvariant().Contains(term)).ToList();

            switch (_sortBy)
            {
                case "Type": rows = rows.OrderBy(r => r.Type).ThenBy(r => r.Name).ToList(); break;
                case "Age": rows = rows.OrderBy(r => r.AgeNumber).ThenBy(r => r.Name).ToList(); break;
                case "Condition": rows = rows.OrderBy(r => r.ConditionNumber).ThenBy(r => r.Name).ToList(); break;
                case "Value": rows = rows.OrderByDescending(r => r.ValueNumber).ThenBy(r => r.Name).ToList(); break;
                case "Routes": rows = rows.OrderByDescending(r => r.Routes).ThenBy(r => r.Name).ToList(); break;
                default: rows = rows.OrderBy(r => r.Name).ToList(); break;
            }

            string signature = string.Join(";", rows.Select(r => r.Name + r.Status + r.Routes + r.Crew + r.Cabin + r.Condition + r.Value));
            if (signature == _signature) return;
            _signature = signature;

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            _selected = Rows.FirstOrDefault(r => r.Aircraft == keep);
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasRows));
            int idle = rows.Count(r => r.IsIdle);
            Title = fleet.Count + " aircraft, total value " + Money(rows.Sum(r => r.ValueNumber)) + (idle > 0 ? "  |  " + idle + " idle (no route)" : "") + (rows.Count(r => r.IsOnOrder) > 0 ? "  |  " + rows.Count(r => r.IsOnOrder) + " awaiting delivery" : "");
            OnPropertyChanged(nameof(Title));
            LoadDetails();
        }

        private static OverviewRow MakeRow(FleetAirliner f, GameObject game)
        {
            var a = f.Airliner;
            string ownership;
            switch (f.Purchased)
            {
                case FleetAirliner.PurchasedType.Leased: ownership = "Leased, " + Money(a.GetLeasingPrice()) + " / month"; break;
                case FleetAirliner.PurchasedType.BoughtDownPayment:
                    ownership = f.RemainingPayment > 0 ? "Down payment, " + Money(f.RemainingPayment) + " to pay" : "Down payment";
                    break;
                default: ownership = "Bought"; break;
            }

            long value = a.GetPrice();
            return new OverviewRow
            {
                Aircraft = f,
                Name = f.Name,
                Type = a.Type.Name,
                Cabin = a.CabinConfiguration,
                Status = a.BuiltDate > game.GameTime ? "Delivery " + DateFormatter.Date(a.BuiltDate) : FerryService.IsFerrying(f) ? "Ferry to " + f.FerryTo.Profile.IATACode : AircraftDealService.IsLeasedOut(f) ? "Leased to " + f.LeaseOutTo.Profile.IATACode : AircraftDealService.IsListed(f) ? "For sale" : CharterService.IsOnCharter(f) ? "On charter" : f.Status.ToString(),
                Homebase = f.Homebase?.Profile.IATACode,
                Ownership = ownership,
                Age = a.Age + " y",
                AgeNumber = a.Age,
                Condition = string.Format("{0:0}%", a.Condition),
                ConditionNumber = a.Condition,
                Routes = f.Routes.Count,
                IsIdle = f.Routes.Count == 0 && a.BuiltDate <= game.GameTime && !AircraftDealService.IsLeasedOut(f) && !CharterService.IsOnCharter(f),
                IsOnOrder = a.BuiltDate > game.GameTime,
                Crew = f.Pilots.Count + "/" + a.Type.CockpitCrew,
                Value = Money(value),
                ValueNumber = value
            };
        }

        private void LoadDetails()
        {
            Details.Clear();
            RouteLines.Clear();
            var r = _selected;
            if (r != null)
            {
                var f = r.Aircraft;
                var a = f.Airliner;
                Details.Add("Registration: " + a.TailNumber + (a.Registered != null ? " (" + a.Registered.Name + ")" : ""));
                Details.Add("Type: " + a.Type.Manufacturer?.Name + " " + a.Type.Name);
                Details.Add("Built: " + DateFormatter.Date(a.BuiltDate) + ", age " + a.Age + " years");
                Details.Add("Acquired: " + DateFormatter.Date(f.PurchasedDate) + " (" + r.Ownership + ")");
                Details.Add("Homebase: " + f.Homebase?.Profile.Name);
                Details.Add("Condition: " + r.Condition + ", flown " + DistanceFormatter.Km(a.Flown) + ", " + DistanceFormatter.Km(a.Flown - a.LastServiceCheck) + " since the last service");
                Details.Add("Range: " + DistanceFormatter.Km(a.Range) + ", cruising speed " + DistanceFormatter.Speed(a.CruisingSpeed)
                            + ", runway " + DistanceFormatter.Metres(a.MinRunwaylength));
                if (a.EngineType != null) Details.Add("Engine: " + a.EngineType.Model);
                Details.Add("Pilots: " + r.Crew);
                Details.Add("Value: " + r.Value);

                foreach (var c in a.Classes.OrderBy(c => (int) c.Type).ToList())
                {
                    if (c.SeatingCapacity <= 0 && !(a.Type is AirlinerCargoType)) continue;
                    string facilities = string.Join(", ", c.GetFacilities().Select(x => x.Name));
                    Details.Add(c.Type + ": " + c.SeatingCapacity + " seats" + (facilities.Length > 0 ? " (" + facilities + ")" : ""));
                }

                var money = new ValueCurrencyFormatter();
                foreach (var route in f.Routes.ToList())
                {
                    string filling;
                    try { filling = string.Format("{0:0}% full", route.FillingDegree * 100); }
                    catch (Exception) { filling = "-"; }
                    RouteLines.Add(
                        new OverviewRouteLine
                        {
                            Name = route.Destination1.Profile.IATACode + " - " + route.Destination2.Profile.IATACode,
                            Balance = "profit " + Convert.ToString(money.Convert(route.Balance)),
                            Filling = filling
                        });
                }
            }

            SellText = r == null ? "Sell" : r.Aircraft.Purchased == FleetAirliner.PurchasedType.Bought ? "Sell aircraft" : "Return aircraft";
            OnPropertyChanged(nameof(SellText));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanCabin));
            OnPropertyChanged(nameof(CanSchedule));
            OnPropertyChanged(nameof(CanSell));
        }
    }
}
