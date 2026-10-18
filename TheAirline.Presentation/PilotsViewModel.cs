using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.Pilots;

namespace TheAirline.Presentation
{
    public class PilotItem
    {
        public PilotItem(Pilot pilot)
        {
            Pilot = pilot;
            IsIdle = pilot.Airline != null && pilot.Airliner == null;
            IsTraining = pilot.Training != null;
            Name = pilot.Profile.Name;
            Details = string.Format(
                "age {0}  |  {1}  |  {2}",
                pilot.Profile.Age, pilot.Rating?.Name, string.Join(", ", pilot.Aircrafts ?? new List<string>()));
            Assignment = (pilot.Airliner == null ? "not assigned" : "flies " + pilot.Airliner.Name)
                         + (pilot.Training != null ? "  |  training for the " + pilot.Training.AirlinerFamily + " until " + DateFormatter.Date(pilot.Training.EndDate) : "");
        }

        public Pilot Pilot { get; }

        /// <summary>One of your pilots who has no aircraft.</summary>
        public bool IsIdle { get; }

        public bool IsTraining { get; }
        public string Name { get; }
        public string Details { get; }
        public string Assignment { get; }
    }

    public class CrewChoice
    {
        public string Name { get; set; }
        public FleetAirliner Airliner { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>Hire pilots and put them on aircraft. An aircraft needs a full cockpit crew before it can fly.</summary>
    public class PilotsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private CrewChoice _target;
        private PilotItem _selected;
        private string _status;
        private string _pendingText;
        private Action _pending;
        private bool _includeAssigned;
        private NamedChoice<Pilot> _trainPilot;
        private NamedChoice<string> _trainFamily;

        public PilotsViewModel(Action changed)
        {
            _changed = changed;
            Available = new ObservableCollection<PilotItem>();
            Hired = new ObservableCollection<PilotItem>();
            Targets = new ObservableCollection<CrewChoice>();
            HireCommand = new RelayCommand(HireSelected, () => _selected != null);
            HireCrewCommand = new RelayCommand(HireCrew, () => _target?.Airliner != null);
            AssignIdleCommand = new RelayCommand(AssignIdle);
            TrainPilots = new ObservableCollection<NamedChoice<Pilot>>();
            TrainFamilies = new ObservableCollection<NamedChoice<string>>();
            TrainCommand = new RelayCommand(AskTrain);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<PilotItem> Available { get; }
        public ObservableCollection<PilotItem> Hired { get; }

        /// <summary>The aircraft that still need pilots; "any" shows every pilot for hire.</summary>
        public ObservableCollection<CrewChoice> Targets { get; }

        public ICommand HireCommand { get; }
        public ICommand HireCrewCommand { get; }
        public ICommand AssignIdleCommand { get; }
        public ICommand TrainCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Pilots who can be sent to training for another aircraft family: by default those without an aircraft.</summary>
        public ObservableCollection<NamedChoice<Pilot>> TrainPilots { get; }

        /// <summary>The aircraft families the chosen pilot cannot fly yet, with the days and the price.</summary>
        public ObservableCollection<NamedChoice<string>> TrainFamilies { get; }

        public bool HasPending => _pending != null;
        public string TrainHint { get; private set; }

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        /// <summary>Also show pilots who fly an aircraft. A substitute pilot is paid while they are away.</summary>
        public bool IncludeAssigned
        {
            get { return _includeAssigned; }
            set
            {
                if (SetProperty(ref _includeAssigned, value))
                {
                    LoadTrainPilots();
                }
            }
        }

        public NamedChoice<Pilot> TrainPilot
        {
            get { return _trainPilot; }
            set
            {
                if (SetProperty(ref _trainPilot, value))
                {
                    LoadTrainFamilies();
                }
            }
        }

        public NamedChoice<string> TrainFamily
        {
            get { return _trainFamily; }
            set
            {
                if (SetProperty(ref _trainFamily, value))
                {
                    UpdateTrainHint();
                }
            }
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        private static string Money(double v)
        {
            return v.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        }

        private void LoadTrainPilots()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            var keep = _trainPilot?.Value;
            TrainPilots.Clear();
            if (airline != null)
            {
                foreach (var p in airline.Pilots.Where(p => p.Training == null && (_includeAssigned || p.Airliner == null)).OrderBy(p => p.Profile.Name))
                {
                    TrainPilots.Add(
                        new NamedChoice<Pilot>
                        {
                            Name = p.Profile.Name + "  (" + (p.Airliner == null ? "no aircraft" : "flies " + p.Airliner.Name) + ", " + string.Join(", ", p.Aircrafts ?? new List<string>()) + ")",
                            Value = p
                        });
                }
            }

            _trainPilot = TrainPilots.FirstOrDefault(c => c.Value == keep) ?? TrainPilots.FirstOrDefault();
            OnPropertyChanged(nameof(TrainPilot));
            LoadTrainFamilies();
        }

        private void LoadTrainFamilies()
        {
            var pilot = _trainPilot?.Value;
            var keep = _trainFamily?.Value;
            TrainFamilies.Clear();
            if (pilot != null)
            {
                var fleetFamilies = GameObject.GetInstance().HumanAirline.Fleet.Select(f => f.Airliner.Type.AirlinerFamily).ToList();
                // the families of your own fleet come first
                foreach (var f in FlightSchoolService.TrainingFamilies(pilot).OrderByDescending(f => fleetFamilies.Contains(f)).ThenBy(f => f))
                {
                    int days = AirlineHelpers.GetTrainingDays(pilot, f);
                    TrainFamilies.Add(
                        new NamedChoice<string>
                        {
                            Name = f + (fleetFamilies.Contains(f) ? " (in your fleet)" : "") + "  -  " + days + " days, " + Money(FlightSchoolService.GetTrainingCost(pilot, f)),
                            Value = f
                        });
                }
            }

            _trainFamily = TrainFamilies.FirstOrDefault(c => c.Value == keep) ?? TrainFamilies.FirstOrDefault();
            OnPropertyChanged(nameof(TrainFamily));
            UpdateTrainHint();
        }

        private void UpdateTrainHint()
        {
            var pilot = _trainPilot?.Value;
            TrainHint = pilot == null
                ? (_includeAssigned ? "You have no pilot who can be trained." : "None of your pilots is without an aircraft. Tick the box to also train pilots who fly one.")
                : "Training takes 2 days for a family of a manufacturer the pilot knows and 14 days otherwise. A training facility for the family (under Facilities) brings the price down to one day."
                  + (pilot.Airliner != null ? " " + pilot.Airliner.Name + " gets a substitute pilot meanwhile, which is paid extra." : "");
            OnPropertyChanged(nameof(TrainHint));
        }

        private void AskTrain()
        {
            var pilot = _trainPilot?.Value;
            var family = _trainFamily?.Value;
            if (pilot == null || family == null)
            {
                Status = "Choose a pilot and an aircraft family.";
                return;
            }

            SetPending(
                "Send " + pilot.Profile.Name + " to train for the " + family + " for " + Money(FlightSchoolService.GetTrainingCost(pilot, family)) + "? It takes "
                + AirlineHelpers.GetTrainingDays(pilot, family) + " days.",
                () =>
                {
                    string m;
                    FlightSchoolService.TrainPilot(GameObject.GetInstance().HumanAirline, pilot, family, out m);
                    Status = m;
                    Reload();
                    _changed?.Invoke();
                });
        }


        public CrewChoice Target
        {
            get { return _target; }
            set
            {
                if (value != null && SetProperty(ref _target, value))
                {
                    LoadAvailable();
                    (HireCrewCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public PilotItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    (HireCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>Refreshes the lists after the fleet or crews changed.</summary>
        public void Reload()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null)
            {
                return;
            }

            var keep = _target?.Airliner;
            Targets.Clear();
            Targets.Add(new CrewChoice { Name = "(any aircraft)" });
            foreach (var f in airline.Fleet.Where(f => f.Pilots.Count < f.Airliner.Type.CockpitCrew).OrderBy(f => f.Name))
            {
                Targets.Add(
                    new CrewChoice
                    {
                        Name = string.Format("{0} - {1} ({2}/{3} pilots)", f.Name, f.Airliner.Type.Name, f.Pilots.Count, f.Airliner.Type.CockpitCrew),
                        Airliner = f
                    });
            }

            _target = Targets.FirstOrDefault(t => t.Airliner == keep && keep != null) ?? Targets[0];
            OnPropertyChanged(nameof(Target));

            Hired.Clear();
            foreach (var p in airline.Pilots.OrderBy(p => p.Profile.Name))
            {
                Hired.Add(new PilotItem(p));
            }

            LoadAvailable();
            LoadTrainPilots();
            (HireCrewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private List<Pilot> Candidates()
        {
            var all = Pilots.GetUnassignedPilots();
            var family = _target?.Airliner?.Airliner.Type.AirlinerFamily;
            //there are always pilots to hire: the market fills up when it runs short
            if (family != null)
            {
                if (PilotSupply.EnsureFamily(family) > 0)
                {
                    all = Pilots.GetUnassignedPilots();
                }
            }
            else
            {
                PilotSupply.EnsureAnyPilots();
                all = Pilots.GetUnassignedPilots();
            }
            return all.Where(p => family == null || (p.Aircrafts != null && p.Aircrafts.Contains(family)))
                      .OrderBy(p => p.Profile.Name)
                      .ToList();
        }

        private void LoadAvailable()
        {
            Available.Clear();
            foreach (var p in Candidates().Take(300))
            {
                Available.Add(new PilotItem(p));
            }

            Selected = null;
        }

        private void Hire(Pilot pilot, FleetAirliner airliner)
        {
            var airline = GameObject.GetInstance().HumanAirline;
            airline.AddPilot(pilot);
            pilot.AirlineSignedDate = GameObject.GetInstance().GameTime;

            // with no aircraft chosen the pilot goes to the first aircraft of his type that needs a pilot
            airliner = airliner ?? airline.Fleet.FirstOrDefault(
                f => f.Pilots.Count < f.Airliner.Type.CockpitCrew
                     && pilot.Aircrafts != null && pilot.Aircrafts.Contains(f.Airliner.Type.AirlinerFamily));

            if (airliner != null && airliner.Pilots.Count < airliner.Airliner.Type.CockpitCrew)
            {
                airliner.AddPilot(pilot);
            }
        }

        private void AssignIdle()
        {
            int n = FlightSchoolService.AssignIdlePilots(GameObject.GetInstance().HumanAirline);
            Status = n > 0 ? n + " pilot(s) without an aircraft were put on aircraft that needed them." : "No idle pilot fits an aircraft that needs a pilot.";
            Reload();
            _changed?.Invoke();
        }

        private void HireSelected()
        {
            if (_selected == null)
            {
                return;
            }

            var pilot = _selected.Pilot;
            Hire(pilot, _target?.Airliner);
            Status = "Hired " + pilot.Profile.Name + (pilot.Airliner != null ? " for " + pilot.Airliner.Name : "") + ".";
            Reload();
            _changed?.Invoke();
        }

        private void HireCrew()
        {
            var airliner = _target?.Airliner;
            if (airliner == null)
            {
                return;
            }

            int needed = airliner.Airliner.Type.CockpitCrew - airliner.Pilots.Count;
            var candidates = Candidates().Take(needed).ToList();
            foreach (var p in candidates)
            {
                Hire(p, airliner);
            }

            Status = candidates.Count < needed
                ? "Only " + candidates.Count + " matching pilot(s) were available for " + airliner.Name + "."
                : "Hired a full crew for " + airliner.Name + ".";
            Reload();
            _changed?.Invoke();
        }
    }
}
