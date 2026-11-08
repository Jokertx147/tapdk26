using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>An idle aircraft in the list, with its best suggestion.</summary>
    public class IdleAircraftRow
    {
        public FleetAirliner Airliner { get; set; }
        public string Title { get; set; }
        public string Line { get; set; }
        public string Best { get; set; }
        public List<RouteSuggestion> Suggestions { get; set; }
    }

    /// <summary>A route suggested for the selected aircraft.</summary>
    public class SuggestionRow
    {
        public RouteSuggestion Suggestion { get; set; }
        public string Title { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string Gates { get; set; }
        public bool HasGates => !string.IsNullOrEmpty(Gates);
        public bool Loses { get; set; }
        public ICommand OpenCommand { get; set; }
        public ICommand AdjustCommand { get; set; }
    }

    /// <summary>
    ///     Routes > Suggestions: the player's idle passenger aircraft, and for each the routes forecast to earn the most,
    ///     opened in one click (gates leased, the best number of round trips scheduled) or taken to the planner first.
    /// </summary>
    public class RouteSuggestionsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private readonly Action<Airport, Airport, FleetAirliner> _plan;
        private IdleAircraftRow _selected;
        private bool _busy;
        private string _status, _summary;
        private int _loadId;

        /// <param name="changed">Called after a route was opened (the route list is read again).</param>
        /// <param name="plan">Opens the route planner for two airports with an aircraft picked.</param>
        public RouteSuggestionsViewModel(Action changed, Action<Airport, Airport, FleetAirliner> plan)
        {
            _changed = changed;
            _plan = plan;
            RefreshCommand = new RelayCommand(() => { var _ = Reload(); });
        }

        public ObservableCollection<IdleAircraftRow> Aircraft { get; } = new ObservableCollection<IdleAircraftRow>();
        public ObservableCollection<SuggestionRow> Suggestions { get; } = new ObservableCollection<SuggestionRow>();
        public ICommand RefreshCommand { get; }

        public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public bool HasAircraft => Aircraft.Count > 0;
        public bool NoSuggestions => _selected != null && Suggestions.Count == 0;

        public IdleAircraftRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value)) ShowSuggestions();
            }
        }

        private static string M(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        /// <summary>Works the suggestions out again (in the background: each aircraft forecasts a few dozen routes).</summary>
        public async Task Reload()
        {
            var airline = GameObject.GetInstance()?.HumanAirline;
            if (airline == null) return;
            int id = ++_loadId;
            IsBusy = true;
            Summary = "Looking for routes...";
            FleetAirliner keep = _selected?.Airliner;
            Dictionary<FleetAirliner, List<RouteSuggestion>> all;
            try
            {
                all = await Task.Run(() => RouteSuggestionService.ForIdle(airline));
            }
            catch (Exception ex)
            {
                if (id != _loadId) return;
                IsBusy = false;
                Summary = "The suggestions couldn't be worked out: " + ex.GetBaseException().Message;
                return;
            }
            if (id != _loadId) return;
            IsBusy = false;
            Show(all, keep);
        }

        private void Show(Dictionary<FleetAirliner, List<RouteSuggestion>> all, FleetAirliner keep)
        {
            Aircraft.Clear();
            foreach (var kv in all.OrderByDescending(x => x.Value.Count == 0 ? double.MinValue : x.Value[0].GainPerDay))
            {
                FleetAirliner f = kv.Key;
                RouteSuggestion best = kv.Value.FirstOrDefault();
                Aircraft.Add(new IdleAircraftRow
                {
                    Airliner = f, Suggestions = kv.Value,
                    Title = f.Name + "  " + f.Airliner.Type.Name,
                    Line = string.Format("{0} seats, {1:#,0} km range, based at {2}", f.Airliner.GetTotalSeatCapacity(), f.Airliner.Range, f.Homebase?.Profile.IATACode ?? "?"),
                    Best = best == null ? "no route found" : string.Format("Best: {0}, {1} a day", best.Name, Signed(best.GainPerDay))
                });
            }
            int n = Aircraft.Count;
            Summary = n == 0
                ? "Every aircraft of yours has a route."
                : n == 1 ? "1 aircraft has no route. It still costs " + M(OwnershipService.MonthlyCost(all.Keys.First())) + " a month to own or lease."
                : n + " aircraft have no route. Together they cost " + M(all.Keys.Sum(OwnershipService.MonthlyCost)) + " a month to own or lease.";
            OnPropertyChanged(nameof(HasAircraft));
            _selected = Aircraft.FirstOrDefault(a => a.Airliner == keep) ?? Aircraft.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            ShowSuggestions();
        }

        private static string Signed(double v)
        {
            return (v >= 0 ? "+" : "-") + M(Math.Abs(v));
        }

        private void ShowSuggestions()
        {
            Suggestions.Clear();
            if (_selected != null)
            {
                foreach (RouteSuggestion s in _selected.Suggestions)
                {
                    RouteSuggestion x = s;
                    RouteForecast fc = s.Forecast;
                    Suggestions.Add(new SuggestionRow
                    {
                        Suggestion = s, Loses = s.GainPerDay < 0,
                        Title = s.Name + "  " + s.To.Profile.Name + (s.From != _selected.Airliner.Homebase ? " (from " + s.From.Profile.Name + ")" : ""),
                        Line1 = string.Format("{0:#,0} km, {1} round trip{2} a day, {3:0}% full, {4}, about {5:#,0} passengers a day want to fly it",
                                              s.Km, fc.RoundTrips, fc.RoundTrips == 1 ? "" : "s", fc.Load*100,
                                              fc.Rivals == 0 ? "no rivals" : fc.Rivals == 1 ? "1 rival" : fc.Rivals + " rivals", s.DailyDemand),
                        Line2 = string.Format("{0} a day from its flights{1}; {2} after the aircraft's own cost",
                                              Signed(fc.ProfitPerDay), s.GatesPerDay > 0 ? ", " + Signed(s.GainPerDay) + " after the new gates" : "", Signed(s.NetPerDay)),
                        Gates = s.GatesToLease.Count == 0 ? null : "Leases gates at " + string.Join(" and ", s.GatesToLease.Select(a => a.Profile.IATACode)) + " (about " + M(s.GatesPerDay) + " a day for 2 years)",
                        OpenCommand = new RelayCommand(() => Open(x)),
                        AdjustCommand = new RelayCommand(() => _plan?.Invoke(x.From, x.To, x.Airliner))
                    });
                }
            }
            OnPropertyChanged(nameof(NoSuggestions));
        }

        /// <summary>Opens the route in one go: leases the missing gates, then schedules the aircraft for the best number of round trips.</summary>
        public bool Open(RouteSuggestion s)
        {
            string done = null;
            bool finished = false;
            var create = new CreateRouteViewModel(s.From, s.To, m => { done = m; finished = true; });
            AircraftChoice choice = create.Aircraft.FirstOrDefault(a => a.Airliner == s.Airliner);
            if (choice == null)
            {
                Status = s.Airliner.Name + " can't fly " + s.Name + " any more.";
                return false;
            }
            create.SelectedAircraft = choice;
            foreach (GateOffer o in create.Offers.ToList())
            {
                if (o.CanLease) o.LeaseCommand.Execute(null);
            }
            if (create.RoundTrips != s.Forecast.RoundTrips && create.Trips.Contains(s.Forecast.RoundTrips)) create.RoundTrips = s.Forecast.RoundTrips;
            create.CreateCommand.Execute(null);
            if (!finished || done == null)
            {
                Status = "Couldn't open " + s.Name + ": " + (create.Info ?? "the planner refused it.");
                return false;
            }
            Status = done;
            _changed?.Invoke();
            var reload = Reload();
            return true;
        }
    }
}
