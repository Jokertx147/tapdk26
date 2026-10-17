using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    public class ServiceChoice
    {
        public RouteFacility Facility { get; set; }
        public string Label { get; set; }
        public override string ToString() { return Label; }
    }

    public class FacilityRow : ObservableObject
    {
        private ServiceChoice _selected;

        public RouteFacility.FacilityType Type { get; set; }
        public string TypeName { get; set; }
        public List<ServiceChoice> Options { get; set; }

        public ServiceChoice Selected
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); }
        }
    }

    public class ClassServiceItem
    {
        public AirlinerClass.ClassType Type { get; set; }
        public string ClassName { get; set; }
        public List<FacilityRow> Rows { get; set; }
    }

    public class ScoreLine
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public double Number { get; set; }
    }

    /// <summary>The meals, drinks, newspapers and Wi-Fi on the flights of a passenger route, per class, and how passengers rate the route.</summary>
    public class RouteServiceViewModel : ObservableObject
    {
        private readonly Route _route;
        private string _status;

        public RouteServiceViewModel(Route route)
        {
            _route = route;
            Classes = new ObservableCollection<ClassServiceItem>();
            Scores = new ObservableCollection<ScoreLine>();
            SaveCommand = new RelayCommand(Save);
            BasicCommand = new RelayCommand(() => Preset(ServiceTier.Basic));
            StandardCommand = new RelayCommand(() => Preset(ServiceTier.Standard));
            PremiumCommand = new RelayCommand(() => Preset(ServiceTier.Premium));
            Load();
            LoadScores();
        }

        public ObservableCollection<ClassServiceItem> Classes { get; }
        public ObservableCollection<ScoreLine> Scores { get; }
        public ICommand SaveCommand { get; }
        public ICommand BasicCommand { get; }
        public ICommand StandardCommand { get; }
        public ICommand PremiumCommand { get; }
        public string CostText { get; private set; }
        public string ScoreTitle { get; private set; }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private static string Money(double v)
        {
            return "$" + v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Describe(RouteFacility f)
        {
            string cost;
            if (f.EType == RouteFacility.ExpenseType.Fixed)
            {
                cost = f.ExpensePerPassenger > 0 ? "costs you " + Money(f.ExpensePerPassenger) + " per passenger" : "no cost";
            }
            else
            {
                cost = "passengers pay for it";
            }

            return f.Name + "  (" + cost + ", service " + f.ServiceLevel + ")";
        }

        private static string NameOf(RouteFacility.FacilityType t)
        {
            switch (t)
            {
                case RouteFacility.FacilityType.AlcoholicDrinks: return "Alcoholic drinks";
                case RouteFacility.FacilityType.WiFi: return "Wi-Fi";
                default: return t.ToString();
            }
        }

        private void Load()
        {
            Classes.Clear();
            var pr = _route as PassengerRoute;
            if (pr == null)
            {
                return;
            }

            var airline = GameObject.GetInstance().HumanAirline;
            int year = GameObject.GetInstance().GameTime.Year;
            foreach (var rc in pr.Classes)
            {
                var item = new ClassServiceItem { Type = rc.Type, ClassName = rc.Type.ToString(), Rows = new List<FacilityRow>() };
                foreach (RouteFacility.FacilityType type in Enum.GetValues(typeof(RouteFacility.FacilityType)))
                {
                    if (year < (int) type || RouteFacilities.IsDiscontinued(type, year))
                    {
                        continue;
                    }

                    var current = rc.GetFacility(type);
                    var available = AirlineHelpers.GetRouteFacilities(airline, type).OrderBy(f => f.ServiceLevel).ToList();
                    if (current != null && !available.Contains(current))
                    {
                        available.Add(current);
                    }

                    if (available.Count == 0)
                    {
                        continue;
                    }

                    var options = available.Select(f => new ServiceChoice { Facility = f, Label = Describe(f) }).ToList();
                    item.Rows.Add(
                        new FacilityRow
                        {
                            Type = type,
                            TypeName = NameOf(type),
                            Options = options,
                            Selected = options.FirstOrDefault(o => o.Facility == current) ?? options[0]
                        });
                }

                Classes.Add(item);
            }

            UpdateCost();
        }

        private void UpdateCost()
        {
            var parts = new List<string>();
            foreach (var c in Classes)
            {
                double cost = c.Rows.Where(r => r.Selected != null && r.Selected.Facility.EType == RouteFacility.ExpenseType.Fixed)
                    .Sum(r => r.Selected.Facility.ExpensePerPassenger);
                parts.Add(c.ClassName + " " + Money(cost));
            }

            CostText = parts.Count == 0 ? "" : "Cost to you per passenger (saved service): " + string.Join("   |   ", parts);
            OnPropertyChanged(nameof(CostText));
        }

        private void Save()
        {
            var pr = _route as PassengerRoute;
            if (pr == null)
            {
                return;
            }

            foreach (var c in Classes)
            {
                var rc = pr.GetRouteAirlinerClass(c.Type);
                rc.Facilities.Clear();
                foreach (var row in c.Rows)
                {
                    if (row.Selected != null)
                    {
                        rc.AddFacility(row.Selected.Facility);
                    }
                }
            }

            UpdateCost();
            LoadScores();
            Status = "Service saved. Passengers notice it from the next flights.";
        }

        private void Preset(ServiceTier tier)
        {
            var airline = GameObject.GetInstance().HumanAirline;
            foreach (var c in Classes)
            {
                foreach (var row in c.Rows)
                {
                    var f = RouteCreator.PickFacility(airline, row.Type, tier);
                    var choice = f == null ? null : row.Options.FirstOrDefault(o => o.Facility == f);
                    if (choice != null)
                    {
                        row.Selected = choice;
                    }
                }
            }

            UpdateCost();
            Status = tier + " service chosen. Press Save service to use it.";
        }

        private bool _hadAirliner;

        /// <summary>Reads the scores again only when an aircraft was added or removed (the list is not rebuilt on every tick).</summary>
        public void RefreshScores()
        {
            if (_route.HasAirliner != _hadAirliner)
            {
                LoadScores();
            }
        }

        public void LoadScores()
        {
            _hadAirliner = _route.HasAirliner;
            Scores.Clear();
            if (!_route.HasAirliner)
            {
                ScoreTitle = "Passenger opinion appears once an aircraft flies this route.";
                OnPropertyChanged(nameof(ScoreTitle));
                return;
            }

            ScoreTitle = "What passengers think of this route (0 to 10)";
            OnPropertyChanged(nameof(ScoreTitle));
            Add("Price", () => RouteHelpers.GetRoutePriceScore(_route));
            Add("Seats", () => RouteHelpers.GetRouteSeatsScore(_route));
            Add("Meals", () => RouteHelpers.GetRouteMealScore(_route));
            Add("In-flight entertainment", () => RouteHelpers.GetRouteInflightScore(_route));
            if ((int) RouteFacility.FacilityType.WiFi <= GameObject.GetInstance().GameTime.Year)
            {
                Add("Wi-Fi", () => RouteHelpers.GetRouteWifiScore(_route));
            }

            Add("Aircraft type", () => RouteHelpers.GetRoutePlaneTypeScore(_route));
            Add("Aircraft age", () => RouteHelpers.GetPlaneAgeScore(_route));
            Add("Luggage", () => RouteHelpers.GetRouteLuggageScore(_route));
            Add("Overall", () => RouteHelpers.GetRouteTotalScore(_route));
        }

        private void Add(string name, Func<double> score)
        {
            try
            {
                double s = score();
                Scores.Add(new ScoreLine { Name = name, Number = s, Value = string.Format("{0:0.0}  ({1})", s, s >= 7 ? "good" : s >= 4 ? "fair" : "poor") });
            }
            catch (Exception)
            {
                // the score needs data the route does not have yet
            }
        }
    }
}
