using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Infrastructure;

namespace TheAirline.Presentation
{
    /// <summary>The saved games, newest first, with their dates.</summary>
    public class LoadGameViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private SaveItem _selected;
        private bool _isBusy;
        private string _status = "";

        public LoadGameViewModel(ShellViewModel shell)
        {
            _shell = shell;

            LoadCommand = new AsyncCommand(LoadAsync, () => !_isBusy && Selected != null);
            DeleteCommand = new RelayCommand(Delete, () => !_isBusy && Selected != null);
            BackCommand = new RelayCommand(shell.ShowMainMenu, () => !_isBusy);

            Refresh();
        }

        public ObservableCollection<SaveItem> Saves { get; } = new ObservableCollection<SaveItem>();

        public ICommand LoadCommand { get; }

        public ICommand DeleteCommand { get; }

        public ICommand BackCommand { get; }

        public bool IsBusy => _isBusy;

        public SaveItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    (LoadCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                    (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private void Refresh()
        {
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
                StartupLog.Write("Load game: listing the saves failed: " + ex);
                Status = "Could not read the saves folder: " + ex.Message;
            }

            Selected = Saves.FirstOrDefault();
            if (Saves.Count == 0 && string.IsNullOrEmpty(Status))
            {
                Status = "There are no saved games yet.";
            }
        }

        private void Delete()
        {
            if (Selected == null)
            {
                return;
            }

            SerializedLoadSaveHelpers.DeleteSavedGame(Selected.Name);
            Refresh();
        }

        private async Task LoadAsync()
        {
            string name = Selected.Name;
            SetBusy(true);
            Status = "Loading " + name + "...";

            try
            {
                await GameSession.LoadGameAsync(name);
                _shell.ShowAirline();
            }
            catch (Exception ex)
            {
                StartupLog.Write("Load game failed: " + ex);
                Status = "Loading the game failed: " + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            OnPropertyChanged(nameof(IsBusy));
            (LoadCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public class SaveItem
    {
        public SaveItem(SaveFileInfo info)
        {
            Name = info.Name;
            SavedAt = info.SavedAt;
        }

        public string Name { get; }

        public DateTime SavedAt { get; }

        public string SavedText => "saved " + SavedAt.ToString("g");
    }
}
