using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>Saving the game under a name, the saved games, and leaving the game.</summary>
    public class OptionsViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private readonly Action _afterSave;
        private string _saveName;
        private SaveItem _selected;
        private string _status;
        private bool _busy;
        private string _pendingText;
        private Func<Task> _pending;

        public OptionsViewModel(ShellViewModel shell, Action afterSave, ICommand mainMenu)
        {
            _shell = shell;
            _afterSave = afterSave;
            MainMenuCommand = mainMenu;
            SaveCommand = new RelayCommand(AskSave, () => !_busy);
            DeleteCommand = new RelayCommand(AskDelete, () => !_busy && _selected != null);
            ExitCommand = new RelayCommand(() => _shell.Exit());
            ConfirmCommand = new AsyncCommand(
                async () =>
                    {
                        var action = _pending;
                        PendingText = null;
                        if (action != null)
                        {
                            await action();
                        }
                    });
            CancelCommand = new RelayCommand(() => PendingText = null);
            SetMoneyCommand = new RelayCommand(SetMoney);
            CleanAreaCommand = new RelayCommand(AskCleanArea, () => !_busy && _area != null && _areaRemovable > 0);

            var airline = GameObject.GetInstance().HumanAirline;
            _saveName = GameSession.SafeFileName(airline?.Profile?.Name ?? "save");
            Reload();
        }

        public ObservableCollection<SaveItem> Saves { get; } = new ObservableCollection<SaveItem>();

        // ---- game area: take out airports outside the continent or region the game is set in ----

        private NamedChoice<GameArea> _area;
        private int _areaRemovable;
        private string _areaStatus;

        public ObservableCollection<NamedChoice<GameArea>> Areas { get; } = new ObservableCollection<NamedChoice<GameArea>>();
        public ICommand CleanAreaCommand { get; }
        public bool HasAreas => Areas.Count > 0;

        public string AreaText
        {
            get
            {
                if (GameObject.GetInstance()?.HumanAirline == null) return "Start or load a game first.";
                int continents = AirportCleanupService.ContinentCount();
                return "This game has " + TheAirline.Models.Airports.Airports.GetAllAirports().Count.ToString("N0") + " airports on " + continents + (continents == 1 ? " continent" : " continents")
                       + ". If you started it in one continent or region, you can take out the airports outside it that nobody uses (a load with an earlier version could add the whole world). Fewer airports make game days, saving and loading faster.";
            }
        }

        public NamedChoice<GameArea> Area
        {
            get { return _area; }
            set
            {
                if (SetProperty(ref _area, value))
                {
                    UpdateAreaPreview();
                }
            }
        }

        public string AreaPreview { get; private set; }
        public string AreaStatus { get => _areaStatus; private set => SetProperty(ref _areaStatus, value); }

        private void LoadAreas()
        {
            Areas.Clear();
            if (GameObject.GetInstance()?.HumanAirline != null)
            {
                try
                {
                    foreach (GameArea area in AirportCleanupService.Areas())
                    {
                        Areas.Add(new NamedChoice<GameArea> {Name = area.Name + ": " + area.Airports.ToString("N0") + " airports", Value = area});
                    }
                }
                catch (Exception ex)
                {
                    StartupLog.Write("Options: listing the game areas failed: " + ex);
                }
            }
            _area = Areas.FirstOrDefault();
            OnPropertyChanged(nameof(Area));
            OnPropertyChanged(nameof(HasAreas));
            OnPropertyChanged(nameof(AreaText));
            UpdateAreaPreview();
        }

        private void UpdateAreaPreview()
        {
            _areaRemovable = 0;
            if (_area == null)
            {
                AreaPreview = null;
            }
            else
            {
                int kept;
                _areaRemovable = AirportCleanupService.Outside(_area.Value, out kept).Count;
                AreaPreview = _areaRemovable == 0
                    ? "Every airport outside it is in use (or there are none): nothing to take out."
                    : _areaRemovable.ToString("N0") + " unused airports outside it would be taken out"
                      + (kept > 0 ? "; " + kept + " outside it are kept because an airline uses them." : ".");
            }
            OnPropertyChanged(nameof(AreaPreview));
            (CleanAreaCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AskCleanArea()
        {
            var area = _area?.Value;
            if (area == null) return;
            int count = _areaRemovable;
            _pending = async () =>
                {
                    SetBusy(true);
                    AreaStatus = "Taking out " + count.ToString("N0") + " airports and building the passenger demand again...";
                    try
                    {
                        bool wasRunning = !TheAirline.Helpers.Workers.GameObjectWorker.GetInstance().IsPaused;
                        int removed = await Task.Run(() =>
                            {
                                GameSession.StopClock();
                                return AirportCleanupService.RemoveOutside(area);
                            });
                        if (wasRunning) TheAirline.Helpers.Workers.GameObjectWorker.GetInstance().Restart();
                        AreaStatus = "Took out " + removed.ToString("N0") + " airports; " + TheAirline.Models.Airports.Airports.GetAllAirports().Count.ToString("N0") + " are left. Save the game to keep the change.";
                    }
                    catch (Exception ex)
                    {
                        StartupLog.Write("Options: taking out airports failed: " + ex);
                        AreaStatus = "That failed: " + ex.Message;
                    }
                    finally
                    {
                        SetBusy(false);
                        LoadAreas();
                    }
                };
            PendingText = "Take out " + count.ToString("N0") + " unused airports outside " + area.Name.Replace(" (the whole continent)", "") + "? Airlines can't fly to them afterwards. This can't be undone, except by loading a save made before.";
        }

        // ---- setting the money ----

        private string _airlineMoney = "", _moneyStatus;

        /// <summary>Called after the money changed, so the top bar and pages update.</summary>
        public Action MoneyChanged { get; set; }

        public ICommand SetMoneyCommand { get; }

        public string AirlineMoney { get => _airlineMoney; set => SetProperty(ref _airlineMoney, value ?? ""); }
        public string MoneyStatus { get => _moneyStatus; private set => SetProperty(ref _moneyStatus, value); }

        public string MoneyText
        {
            get
            {
                var game = GameObject.GetInstance();
                if (game?.HumanAirline == null) return "No game is running.";
                return "Your money: " + Cash(game.HumanAirline.Money) + ". The player's money (human money) always follows your airline's cash.";
            }
        }

        private static string Cash(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        /// <summary>Reads amounts such as 50000000, 50,000,000, $50 000 000, 50m, 2.5b, 750k or -1000000.</summary>
        public static bool TryParseMoney(string text, out double amount)
        {
            amount = 0;
            string t = (text ?? "").Replace(",", "").Replace(" ", "").Replace("$", "").Replace("_", "").Trim().ToLowerInvariant();
            if (t.Length == 0) return false;
            double factor = 1;
            char last = t[t.Length - 1];
            if (last == 'k') factor = 1e3;
            else if (last == 'm') factor = 1e6;
            else if (last == 'b') factor = 1e9;
            else if (last == 't') factor = 1e12;
            if (factor > 1) t = t.Substring(0, t.Length - 1);
            double v;
            if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
            v *= factor;
            if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e15) return false;
            amount = Math.Round(v, 2);
            return true;
        }

        private void SetMoney()
        {
            var game = GameObject.GetInstance();
            if (game?.HumanAirline == null)
            {
                MoneyStatus = "Start or load a game first.";
                return;
            }

            double amount;
            if (!TryParseMoney(_airlineMoney, out amount))
            {
                MoneyStatus = "Type the amount as a number, for example 50000000, 50,000,000 or 50m (k, m and b work, a minus sign too).";
                return;
            }

            game.HumanAirline.Money = amount;
            MoneyStatus = "Your money is now " + Cash(amount) + ".";
            OnPropertyChanged(nameof(MoneyText));
            MoneyChanged?.Invoke();
        }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand MainMenuCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string SaveName
        {
            get { return _saveName; }
            set { SetProperty(ref _saveName, value ?? ""); }
        }

        public SaveItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    if (value != null)
                    {
                        SaveName = value.Name;
                    }

                    (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public bool IsBusy => _busy;

        /// <summary>Dates as MM-DD-YYYY (on) or DD-MM-YYYY (off).</summary>
        public bool UseUsDate
        {
            get { return DateFormatter.UseUs; }
            set
            {
                DateFormatter.UseUs = value;
                OnPropertyChanged(nameof(UseUsDate));
                OnPropertyChanged(nameof(UseInternationalDate));
                OnPropertyChanged(nameof(DateExample));
            }
        }

        public bool UseInternationalDate
        {
            get { return !DateFormatter.UseUs; }
            set { UseUsDate = !value; }
        }

        public string DateExample => "Example: " + DateFormatter.Example;

        /// <summary>Distances and speeds in miles (on) or kilometres (off).</summary>
        public bool UseMiles
        {
            get { return DistanceFormatter.UseMiles; }
            set
            {
                DistanceFormatter.UseMiles = value;
                OnPropertyChanged(nameof(UseMiles));
                OnPropertyChanged(nameof(UseKilometres));
                OnPropertyChanged(nameof(DistanceExample));
            }
        }

        public bool UseKilometres
        {
            get { return !DistanceFormatter.UseMiles; }
            set { UseMiles = !value; }
        }

        /// <summary>How much flights cost: Original, Realistic or Tough (kept in the save).</summary>
        public System.Collections.Generic.List<string> CostLevels => TheAirline.Helpers.FlightCosts.LevelNames.ToList();

        public string CostLevel
        {
            get
            {
                var g = TheAirline.Models.General.GameObject.GetInstance();
                return TheAirline.Helpers.FlightCosts.LevelNames[Math.Max(0, Math.Min(2, g?.CostLevel ?? 1))];
            }
            set
            {
                int i = Array.IndexOf(TheAirline.Helpers.FlightCosts.LevelNames, value);
                var g = TheAirline.Models.General.GameObject.GetInstance();
                if (i < 0 || g == null || g.CostLevel == i) return;
                g.CostLevel = i;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CostLevelText));
            }
        }

        public string CostLevelText => TheAirline.Helpers.FlightCosts.Describe(TheAirline.Models.General.GameObject.GetInstance()?.CostLevel ?? 1);

        public string DistanceExample => "Example: " + DistanceFormatter.Km(1000) + ", " + DistanceFormatter.Speed(800);

        /// <summary>Save automatically (as "Autosave ...") when leaving the game or closing the window.</summary>
        public bool AutoSave
        {
            get { return GameSession.AutoSaveOnExit; }
            set { GameSession.AutoSaveOnExit = value; OnPropertyChanged(nameof(AutoSave)); OnPropertyChanged(nameof(LeaveText)); }
        }

        public string LeaveText => AutoSave
            ? "The game is saved as \"" + GameSession.AutoSaveName() + "\" when you leave."
            : "The game is not saved when you leave. Save it first.";

        public string PendingText
        {
            get { return _pendingText; }
            private set
            {
                if (SetProperty(ref _pendingText, value))
                {
                    OnPropertyChanged(nameof(HasPending));
                }
            }
        }

        public bool HasPending => _pendingText != null;

        public void Reload()
        {
            LoadAreas();
            OnPropertyChanged(nameof(CostLevel));
            OnPropertyChanged(nameof(CostLevelText));
            OnPropertyChanged(nameof(MoneyText));
            string keep = _selected?.Name;
            Saves.Clear();
            try
            {
                foreach (SaveFileInfo info in LoadSaveHelpers.GetSavedGameInfos())
                {
                    Saves.Add(new SaveItem(info));
                }
            }
            catch (Exception ex)
            {
                StartupLog.Write("Options: listing the saves failed: " + ex);
                Status = "Could not read the saves folder: " + ex.Message;
            }

            _selected = Saves.FirstOrDefault(s => s.Name == keep);
            OnPropertyChanged(nameof(Selected));
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AskSave()
        {
            string name = GameSession.SafeFileName(_saveName ?? "");
            if (Saves.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _pending = () => SaveAsync(name);
                PendingText = "A saved game called \"" + name + "\" exists already. Replace it?";
                return;
            }

            // a plain save needs no question
            var ignored = SaveAsync(name);
        }

        private async Task SaveAsync(string name)
        {
            SetBusy(true);
            Status = "Saving...";
            try
            {
                await GameSession.SaveGameAsync(name);
                Status = "Saved as " + name + ".";
                Reload();
                _afterSave?.Invoke();
            }
            catch (Exception ex)
            {
                StartupLog.Write("Options: saving failed: " + ex);
                Status = "Saving failed: " + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void AskDelete()
        {
            var item = _selected;
            if (item == null)
            {
                return;
            }

            _pending = () =>
                {
                    SerializedLoadSaveHelpers.DeleteSavedGame(item.Name);
                    Status = "Deleted " + item.Name + ".";
                    Reload();
                    return Task.CompletedTask;
                };
            PendingText = "Delete the saved game \"" + item.Name + "\"?";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            OnPropertyChanged(nameof(IsBusy));
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CleanAreaCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
