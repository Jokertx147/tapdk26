using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Models.General;
using TheAirline.Models.General.Scenarios;

namespace TheAirline.Presentation
{
    public class ScenarioItem
    {
        public Scenario Scenario { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Problem { get; set; }
        public override string ToString() { return Name; }
    }

    public class ScenarioGoalRow
    {
        public string Goal { get; set; }
        public string When { get; set; }
        public string Now { get; set; }
        public string Status { get; set; }
        public double Opacity { get; set; } = 1;
    }

    /// <summary>Main menu > Scenarios: the scenarios in the game data, their story and goals, and starting one.</summary>
    public class ScenariosViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private ScenarioItem _selected;
        private string _status = "Loading the game data...";
        private bool _busy = true;

        public ScenariosViewModel(ShellViewModel shell)
        {
            _shell = shell;
            Items = new ObservableCollection<ScenarioItem>();
            Goals = new ObservableCollection<ScenarioGoalRow>();
            StartCommand = new AsyncCommand(StartAsync, () => !_busy && _selected != null && _selected.Problem == null);
            BackCommand = new RelayCommand(shell.ShowMainMenu, () => !_busy || Items.Count > 0);
            var ignore = LoadAsync();
        }

        public ObservableCollection<ScenarioItem> Items { get; }
        public ObservableCollection<ScenarioGoalRow> Goals { get; }
        public ICommand StartCommand { get; }
        public ICommand BackCommand { get; }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public bool IsBusy => _busy;

        public string Description { get; private set; }
        public string Details { get; private set; }

        public ScenarioItem Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    Show();
                    (StartCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private async Task LoadAsync()
        {
            try
            {
                await GameSession.EnsureDataLoadedAsync();
                foreach (var s in Scenarios.GetScenarios().OrderBy(x => x.StartYear))
                {
                    Items.Add(new ScenarioItem
                    {
                        Scenario = s,
                        Name = s.Name,
                        Summary = string.Format("{0} to {1} | {2} | {3}", s.StartYear, s.EndYear, s.Airline?.Profile.Name ?? "?", s.Difficulty?.Name ?? ""),
                        Problem = ScenarioService.Problem(s)
                    });
                }
                Status = Items.Count == 0 ? "No scenarios found in the game data." : null;
            }
            catch (Exception ex)
            {
                StartupLog.Write("Scenarios: loading failed: " + ex);
                Status = "Loading the scenarios failed: " + ex.Message;
            }
            finally
            {
                // pick the first scenario before the page says it is ready
                Selected = Items.FirstOrDefault();
                _busy = false;
                OnPropertyChanged(nameof(IsBusy));
                (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void Show()
        {
            Goals.Clear();
            var s = _selected?.Scenario;
            if (s == null)
            {
                Description = Details = null;
            }
            else
            {
                Description = s.Description;
                var lines = new List<string>
                {
                    string.Format("{0} to {1} ({2} years), difficulty {3}.", s.StartYear, s.EndYear, s.EndYear - s.StartYear, s.Difficulty?.Name),
                    string.Format("You run {0} ({1}) from {2}, {3}.", s.Airline?.Profile.Name, AirlineTypeRules.Kind(s.Airline), s.Homebase?.Profile.Name, s.Homebase?.Profile.Country?.Name),
                    "Starting cash " + Money(s.StartCash) + (s.StartDebt > 0 ? ", and a debt of " + Money(s.StartDebt) + " to pay off." : ".")
                };
                if (s.Fleet.Count > 0) lines.Add("Fleet: " + string.Join(", ", s.Fleet.Select(f => f.Value + " x " + f.Key.Name)) + ".");
                if (s.Destinations.Count > 0) lines.Add("Gates at " + s.Destinations.Count + " airports: " + string.Join(", ", s.Destinations.Take(12).Select(a => a.Profile.IATACode)) + (s.Destinations.Count > 12 ? " and more" : "") + ".");
                if (s.OpponentAirlines.Count > 0) lines.Add("Rivals: " + string.Join(", ", s.OpponentAirlines.Select(o => o.Airline?.Profile.Name)) + ".");
                if (_selected.Problem != null) lines.Add("This scenario can't be played: " + _selected.Problem + ".");
                Details = string.Join(Environment.NewLine, lines);
                foreach (var f in s.Failures)
                {
                    bool measured = ScenarioService.IsMeasured(f.Type);
                    Goals.Add(new ScenarioGoalRow { Goal = ScenarioService.Describe(s, f), When = ScenarioService.Timing(s, f), Opacity = measured ? 1 : 0.5 });
                }
            }
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(Details));
        }

        private async Task StartAsync()
        {
            var s = _selected?.Scenario;
            if (s == null) return;
            _busy = true;
            OnPropertyChanged(nameof(IsBusy));
            (StartCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            Status = "Setting up " + s.Name + ", this takes a little while...";
            try
            {
                await Task.Run(() => ScenarioService.Start(s));
                StartupLog.Write("Scenarios: started " + s.Name);
                _shell.ShowAirline();
            }
            catch (Exception ex)
            {
                StartupLog.Write("Scenarios: starting failed: " + ex);
                Status = "Starting the scenario failed: " + ex.Message;
                _busy = false;
                OnPropertyChanged(nameof(IsBusy));
                (StartCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>My Airline > Scenario: the scenario's story, years left, every goal and where the airline stands.</summary>
    public class ScenarioViewModel : ObservableObject
    {
        public ScenarioViewModel()
        {
            Goals = new ObservableCollection<ScenarioGoalRow>();
            Reload();
        }

        public ObservableCollection<ScenarioGoalRow> Goals { get; }
        public bool HasScenario { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string YearsText { get; private set; }
        public string ResultText { get; private set; }
        public bool HasResult => ResultText != null;

        public void Reload()
        {
            var so = GameObject.GetInstance()?.Scenario;
            HasScenario = so?.Scenario != null;
            Goals.Clear();
            if (HasScenario)
            {
                var s = so.Scenario;
                var now = GameObject.GetInstance().GameTime;
                Name = s.Name;
                Description = s.Description;
                int left = Math.Max(0, s.EndYear - now.Year);
                YearsText = so.IsEnded
                    ? string.Format("{0} to {1}. The scenario is over; you can keep playing.", s.StartYear, s.EndYear)
                    : string.Format("{0} to {1}: you win by meeting the goals until January {1} ({2} year{3} to go).", s.StartYear, s.EndYear, left, left == 1 ? "" : "s");
                ResultText = so.ScenarioFailed != null
                    ? "Lost in " + so.EndedOn.ToString("MMMM yyyy") + ": " + so.ScenarioFailed.FailureText
                    : so.IsSuccess ? "Won in " + so.EndedOn.ToString("MMMM yyyy") + ": " + (s.SuccessText ?? "you met every goal.") : null;
                foreach (var g in ScenarioService.Goals(so))
                {
                    Goals.Add(new ScenarioGoalRow
                    {
                        Goal = g.Goal, When = g.When,
                        Now = string.IsNullOrEmpty(g.Now) || g.Ok == null ? (g.Now ?? "") : "now " + g.Now,
                        Status = g.Status,
                        Opacity = g.Ok == null ? 0.55 : 1
                    });
                }
            }
            foreach (var p in new[] { nameof(HasScenario), nameof(Name), nameof(Description), nameof(YearsText), nameof(ResultText), nameof(HasResult) }) OnPropertyChanged(p);
        }

        /// <summary>The result to show once when the scenario ends, or null.</summary>
        public static string TakeEndMessage(out string title)
        {
            title = null;
            var so = GameObject.GetInstance()?.Scenario;
            if (so == null || !so.IsEnded || so.EndShown) return null;
            so.EndShown = true;
            title = so.IsSuccess && so.ScenarioFailed == null ? "Scenario won: " + so.Scenario.Name : "Scenario lost: " + so.Scenario.Name;
            string text = so.ScenarioFailed != null ? so.ScenarioFailed.FailureText : (so.Scenario.SuccessText ?? "You met every goal.");
            return text + Environment.NewLine + Environment.NewLine + "The game is paused. You can keep playing this airline as a normal game, or go back to the main menu (Options).";
        }
    }
}
