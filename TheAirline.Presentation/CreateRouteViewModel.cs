using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class AircraftChoice
    {
        public string Name { get; set; }
        public FleetAirliner Airliner { get; set; }
        public override string ToString() { return Name; }
    }

    public class ContractChoice
    {
        public AirportContract.ContractType Type { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>Offer to lease gates at an airport where the airline has none free.</summary>
    public class GateOffer : ObservableObject
    {
        private readonly Airport _airport;
        private readonly Action _signed;
        private ContractChoice _selected;

        public GateOffer(Airport airport, Action signed)
        {
            _airport = airport;
            _signed = signed;
            var f = new ValueCurrencyFormatter();
            int gates = Math.Min(2, RouteCreator.GetFreePassengerGates(airport));
            CanLease = gates > 0;
            Options = new List<ContractChoice>();

            if (CanLease)
            {
                foreach (AirportContract.ContractType t in Enum.GetValues(typeof(AirportContract.ContractType)))
                {
                    double yearly = AirportHelpers.GetYearlyContractPayment(airport, t, gates, 2);
                    Options.Add(new ContractChoice { Type = t, Text = string.Format("{0}  ({1}/year, {2} gate(s), 2 years)", t, f.Convert(yearly), gates) });
                }
                _selected = Options.First();
            }

            Title = airport.Profile.IATACode + " " + airport.Profile.Name;
            Text = CanLease
                ? "You have no free gate at " + Title + ". Lease gates there to create the route."
                : "There are no free gates at " + Title + " to lease.";
            LeaseCommand = new RelayCommand(Lease, () => CanLease);
        }

        public string Title { get; }
        public string Text { get; }
        public bool CanLease { get; }
        public List<ContractChoice> Options { get; }
        public ICommand LeaseCommand { get; }

        public ContractChoice SelectedOption
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); }
        }

        private void Lease()
        {
            if (_selected == null)
            {
                return;
            }

            RouteCreator.SignContract(_airport, _selected.Type);
            _signed();
        }
    }

    /// <summary>Create a passenger route between two airports, with an optional schedule for one aircraft.</summary>
    public class CreateRouteViewModel : ObservableObject
    {
        private readonly Airport _origin, _destination;
        private readonly Action<string> _done;
        private bool _updating;
        private ServiceTier _tier = ServiceTier.Standard;
        private AircraftChoice _aircraft;
        private OpsType _operation = OpsType.Regular;
        private int _turnaround = 60;
        private int _trips = 1;
        private string _startTime;
        private bool _startAirliner = true;
        private string _info;
        private bool _scheduleEnabled, _detailEnabled;

        public CreateRouteViewModel(Airport origin, Airport destination, Action<string> done)
        {
            _origin = origin;
            _destination = destination;
            _done = done;

            Title = string.Format("New route {0} - {1}", origin.Profile.IATACode, destination.Profile.IATACode);
            Tiers = Enum.GetValues(typeof(ServiceTier)).Cast<ServiceTier>().ToList();
            Operations = new List<OpsType> { OpsType.Regular, OpsType.Whole_Day };
            Turnarounds = RouteCreator.TurnaroundOptions;
            Trips = new ObservableCollection<int>();
            StartTimes = new ObservableCollection<string>();
            Offers = new ObservableCollection<GateOffer>();

            Aircraft = new List<AircraftChoice> { new AircraftChoice { Name = "(assign an aircraft later)" } };
            Aircraft.AddRange(
                RouteCreator.GetCandidateAirliners(origin, destination)
                            .Select(f => new AircraftChoice { Name = f.Name + " - " + f.Airliner.Type.Name, Airliner = f }));
            _aircraft = Aircraft.Count > 1 ? Aircraft[1] : Aircraft[0];

            CreateCommand = new RelayCommand(Create);
            CancelCommand = new RelayCommand(() => _done(null));

            SetDefaultTurnaround();
            RefreshSchedule();
            RefreshGates();
        }

        public string Title { get; }
        public List<ServiceTier> Tiers { get; }
        public List<AircraftChoice> Aircraft { get; }
        public List<OpsType> Operations { get; }
        public List<int> Turnarounds { get; }
        public ObservableCollection<int> Trips { get; }
        public ObservableCollection<string> StartTimes { get; }
        public ObservableCollection<GateOffer> Offers { get; }
        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }

        public ServiceTier Tier
        {
            get { return _tier; }
            set { SetProperty(ref _tier, value); }
        }

        public AircraftChoice SelectedAircraft
        {
            get { return _aircraft; }
            set
            {
                if (value != null && SetProperty(ref _aircraft, value))
                {
                    SetDefaultTurnaround();
                    RefreshSchedule();
                }
            }
        }

        public OpsType Operation
        {
            get { return _operation; }
            set
            {
                if (SetProperty(ref _operation, value))
                {
                    RefreshSchedule();
                }
            }
        }

        public int Turnaround
        {
            get { return _turnaround; }
            set
            {
                if (SetProperty(ref _turnaround, value))
                {
                    RefreshSchedule();
                }
            }
        }

        public int RoundTrips
        {
            get { return _trips; }
            set
            {
                if (SetProperty(ref _trips, value))
                {
                    RefreshStartTimes();
                }
            }
        }

        public string StartTime
        {
            get { return _startTime; }
            set { SetProperty(ref _startTime, value); }
        }

        public bool StartAirliner
        {
            get { return _startAirliner; }
            set { SetProperty(ref _startAirliner, value); }
        }

        public string Info
        {
            get { return _info; }
            private set { SetProperty(ref _info, value); }
        }

        /// <summary>True when an aircraft is chosen, so the schedule options apply.</summary>
        public bool ScheduleEnabled
        {
            get { return _scheduleEnabled; }
            private set { SetProperty(ref _scheduleEnabled, value); }
        }

        /// <summary>False on long-haul routes, where the schedule is weekly and made automatically.</summary>
        public bool DetailEnabled
        {
            get { return _detailEnabled; }
            private set { SetProperty(ref _detailEnabled, value); }
        }

        public bool HasOffers
        {
            get { return Offers.Count > 0; }
        }

        private FleetAirliner Airliner
        {
            get { return _aircraft?.Airliner; }
        }

        private void SetDefaultTurnaround()
        {
            _updating = true;
            int minutes = Airliner == null ? 60 : (int)FleetAirlinerHelpers.GetMinTimeBetweenFlights(Airliner).TotalMinutes;
            int best = RouteCreator.TurnaroundOptions.FirstOrDefault(o => o >= minutes);
            _turnaround = best == 0 ? RouteCreator.TurnaroundOptions.Last() : best;
            OnPropertyChanged(nameof(Turnaround));
            _updating = false;
        }

        private void RefreshSchedule()
        {
            if (_updating)
            {
                return;
            }

            ScheduleEnabled = Airliner != null;
            Trips.Clear();
            StartTimes.Clear();

            if (Airliner == null)
            {
                DetailEnabled = false;
                Info = Aircraft.Count == 1
                    ? "None of your aircraft can fly this route (range, runway, or already on a route). The route is created without a schedule; assign an aircraft later."
                    : "The route is created without a schedule; assign an aircraft later.";
                return;
            }

            var type = Airliner.Airliner.Type;
            if (RouteCreator.IsLongRoute(_origin, _destination, type, _turnaround))
            {
                DetailEnabled = false;
                Info = "This is a long-haul route, so a weekly schedule is created.";
                return;
            }

            DetailEnabled = true;
            int max = RouteCreator.GetMaxRoundTrips(_origin, _destination, type, _turnaround, _operation);
            for (int i = 1; i <= max; i++)
            {
                Trips.Add(i);
            }

            _updating = true;
            _trips = max;
            OnPropertyChanged(nameof(RoundTrips));
            _updating = false;

            RefreshStartTimes();
        }

        private void RefreshStartTimes()
        {
            if (_updating || Airliner == null)
            {
                return;
            }

            StartTimes.Clear();
            foreach (TimeSpan t in RouteCreator.GetStartTimes(
                _origin, _destination, Airliner.Airliner.Type, _turnaround, _trips, _operation))
            {
                StartTimes.Add(t.ToString(@"hh\:mm"));
            }

            StartTime = StartTimes.FirstOrDefault();
            Info = "";
        }

        private void RefreshGates()
        {
            Offers.Clear();
            foreach (Airport a in new[] { _origin, _destination }.Distinct())
            {
                if (!AirportHelpers.HasFreeGates(a, GameObject.GetInstance().HumanAirline, Terminal.TerminalType.Passenger))
                {
                    if (RouteCreator.NeedsContract(a))
                    {
                        Offers.Add(new GateOffer(a, RefreshGates));
                    }
                    else
                    {
                        Info = "All your gates at " + a.Profile.Name + " are in use. Free a gate or extend your contract there first.";
                    }
                }
            }

            OnPropertyChanged(nameof(HasOffers));
        }

        private void Create()
        {
            RefreshGates();
            if (Offers.Count > 0)
            {
                Info = "Lease the missing gates first (see above).";
                return;
            }

            var request = new NewRouteRequest
            {
                Origin = _origin,
                Destination = _destination,
                Tier = _tier,
                Airliner = Airliner,
                StartAirliner = _startAirliner,
                Operation = _operation,
                TurnaroundMinutes = _turnaround,
                RoundTripsPerDay = _trips < 1 ? 1 : _trips,
                StartTime = _startTime == null ? new TimeSpan(6, 0, 0) : TimeSpan.Parse(_startTime)
            };

            string message;
            var route = RouteCreator.Create(request, out message);
            if (route == null)
            {
                Info = message;
                return;
            }

            _done(message);
        }
    }
}
