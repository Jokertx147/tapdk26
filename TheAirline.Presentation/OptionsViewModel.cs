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

            var airline = GameObject.GetInstance().HumanAirline;
            _saveName = GameSession.SafeFileName(airline?.Profile?.Name ?? "save");
            Reload();
        }

        public ObservableCollection<SaveItem> Saves { get; } = new ObservableCollection<SaveItem>();
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
