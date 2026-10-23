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

            var airline = GameObject.GetInstance().HumanAirline;
            _saveName = GameSession.SafeFileName(airline?.Profile?.Name ?? "save");
            Reload();
        }

        public ObservableCollection<SaveItem> Saves { get; } = new ObservableCollection<SaveItem>();

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
        }
    }
}
