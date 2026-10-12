using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
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
            Name = pilot.Profile.Name;
            Details = string.Format(
                "age {0}  |  {1}  |  {2}",
                pilot.Profile.Age, pilot.Rating?.Name, string.Join(", ", pilot.Aircrafts ?? new List<string>()));
            Assignment = pilot.Airliner == null ? "not assigned" : "flies " + pilot.Airliner.Name;
        }

        public Pilot Pilot { get; }
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

        public PilotsViewModel(Action changed)
        {
            _changed = changed;
            Available = new ObservableCollection<PilotItem>();
            Hired = new ObservableCollection<PilotItem>();
            Targets = new ObservableCollection<CrewChoice>();
            HireCommand = new RelayCommand(HireSelected, () => _selected != null);
            HireCrewCommand = new RelayCommand(HireCrew, () => _target?.Airliner != null);
            Reload();
        }

        public ObservableCollection<PilotItem> Available { get; }
        public ObservableCollection<PilotItem> Hired { get; }

        /// <summary>The aircraft that still need pilots; "any" shows every pilot for hire.</summary>
        public ObservableCollection<CrewChoice> Targets { get; }

        public ICommand HireCommand { get; }
        public ICommand HireCrewCommand { get; }

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
            (HireCrewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private List<Pilot> Candidates()
        {
            var all = Pilots.GetUnassignedPilots();
            var family = _target?.Airliner?.Airliner.Type.AirlinerFamily;
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
