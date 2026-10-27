using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>
    ///     Fleet renewal: replace every aircraft of one type by another type, route by route. Shows what would happen to
    ///     each aircraft before anything is done.
    /// </summary>
    public class FleetRenewalViewModel : ObservableObject
    {
        private readonly Action _changed;
        private NamedChoice<AirlinerType> _oldType, _newType;
        private NamedChoice<SlowerChoice> _slower;
        private NamedChoice<OldAircraftChoice> _after;
        private bool _buyMissing = true, _lease, _busy, _loading;
        private string _summary, _status, _pendingText;
        private RenewalPlan _plan;

        public FleetRenewalViewModel(Action changed)
        {
            _changed = changed;
            SlowerChoices.Add(new NamedChoice<SlowerChoice> {Name = "Shift the times (or keep the round trips that fit)", Value = SlowerChoice.ShiftOrKeep});
            SlowerChoices.Add(new NamedChoice<SlowerChoice> {Name = "Keep only the round trips that fit", Value = SlowerChoice.KeepWhatFits});
            SlowerChoices.Add(new NamedChoice<SlowerChoice> {Name = "Skip that aircraft", Value = SlowerChoice.Skip});
            AfterChoices.Add(new NamedChoice<OldAircraftChoice> {Name = "Sell them (leased ones go back)", Value = OldAircraftChoice.Sell});
            AfterChoices.Add(new NamedChoice<OldAircraftChoice> {Name = "List them for sale", Value = OldAircraftChoice.ListForSale});
            AfterChoices.Add(new NamedChoice<OldAircraftChoice> {Name = "Keep them idle", Value = OldAircraftChoice.KeepIdle});
            _slower = SlowerChoices[0];
            _after = AfterChoices[0];
            RenewCommand = new RelayCommand(AskRenew, () => !_busy && _plan != null && _plan.Rows.Any(r => r.Ok));
            ConfirmCommand = new AsyncCommand(Confirm);
            CancelCommand = new RelayCommand(() => PendingText = null);
            Reload();
        }

        public ObservableCollection<NamedChoice<AirlinerType>> OldTypes { get; } = new ObservableCollection<NamedChoice<AirlinerType>>();
        public ObservableCollection<NamedChoice<AirlinerType>> NewTypes { get; } = new ObservableCollection<NamedChoice<AirlinerType>>();
        public ObservableCollection<NamedChoice<SlowerChoice>> SlowerChoices { get; } = new ObservableCollection<NamedChoice<SlowerChoice>>();
        public ObservableCollection<NamedChoice<OldAircraftChoice>> AfterChoices { get; } = new ObservableCollection<NamedChoice<OldAircraftChoice>>();
        public ObservableCollection<string> Rows { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Report { get; } = new ObservableCollection<string>();

        public ICommand RenewCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public NamedChoice<AirlinerType> OldType
        {
            get { return _oldType; }
            set
            {
                if (SetProperty(ref _oldType, value) && !_loading)
                {
                    LoadNewTypes();
                }
            }
        }

        public NamedChoice<AirlinerType> NewType
        {
            get { return _newType; }
            set
            {
                if (SetProperty(ref _newType, value) && !_loading) UpdatePlan();
            }
        }

        public NamedChoice<SlowerChoice> Slower
        {
            get { return _slower; }
            set
            {
                if (SetProperty(ref _slower, value)) UpdatePlan();
            }
        }

        public NamedChoice<OldAircraftChoice> After
        {
            get { return _after; }
            set
            {
                if (SetProperty(ref _after, value)) UpdatePlan();
            }
        }

        public bool BuyMissing
        {
            get { return _buyMissing; }
            set
            {
                if (SetProperty(ref _buyMissing, value)) UpdatePlan();
            }
        }

        public bool Lease
        {
            get { return _lease; }
            set
            {
                if (SetProperty(ref _lease, value)) UpdatePlan();
            }
        }

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }

        public string PendingText
        {
            get { return _pendingText; }
            private set
            {
                if (SetProperty(ref _pendingText, value)) OnPropertyChanged(nameof(HasPending));
            }
        }

        public bool HasPending => _pendingText != null;
        public bool HasReport => Report.Count > 0;

        private RenewalSettings Settings()
        {
            return new RenewalSettings
            {
                OldType = _oldType?.Value, NewType = _newType?.Value, BuyMissing = _buyMissing, Lease = _lease,
                Slower = _slower?.Value ?? SlowerChoice.ShiftOrKeep, AfterOld = _after?.Value ?? OldAircraftChoice.Sell
            };
        }

        /// <summary>Read the fleet and the market again, keeping the picked types when they are still there.</summary>
        public void Reload()
        {
            if (_busy || GameObject.GetInstance()?.HumanAirline == null) return;
            var keepOld = _oldType?.Value;
            _loading = true;
            OldTypes.Clear();
            foreach (var t in FleetRenewalService.FleetTypes())
            {
                OldTypes.Add(new NamedChoice<AirlinerType> {Name = t.Item1.Name + " (" + t.Item2 + " in the fleet)", Value = t.Item1});
            }
            _oldType = OldTypes.FirstOrDefault(o => o.Value == keepOld) ?? OldTypes.FirstOrDefault();
            OnPropertyChanged(nameof(OldType));
            _loading = false;
            LoadNewTypes();
        }

        private void LoadNewTypes()
        {
            var keepNew = _newType?.Value;
            _loading = true;
            NewTypes.Clear();
            if (_oldType != null)
            {
                foreach (var t in FleetRenewalService.NewTypes(_oldType.Value))
                {
                    NewTypes.Add(new NamedChoice<AirlinerType>
                    {
                        Name = t.Item1.Name + ": " + t.Item2 + " idle, " + t.Item3 + " for sale" + (t.Item3 > 0 ? " from " + Math.Round(t.Item4).ToString("N0") : "")
                               + " (" + t.Item1.CruisingSpeed.ToString("N0") + " km/h, " + t.Item1.Range.ToString("N0") + " km)",
                        Value = t.Item1
                    });
                }
            }
            _newType = NewTypes.FirstOrDefault(n => n.Value == keepNew) ?? NewTypes.FirstOrDefault();
            OnPropertyChanged(nameof(NewType));
            _loading = false;
            UpdatePlan();
        }

        private void UpdatePlan()
        {
            Rows.Clear();
            if (_oldType == null || _newType == null)
            {
                _plan = null;
                Summary = _oldType == null ? "Your fleet is empty." : "No other type of the same kind is idle in your fleet or for sale.";
            }
            else
            {
                _plan = FleetRenewalService.Plan(Settings());
                foreach (RenewalRow row in _plan.Rows) Rows.Add(row.Text);
                Summary = _plan.Summary;
            }
            (RenewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AskRenew()
        {
            if (_plan == null) return;
            PendingText = "Renew now? " + _plan.Summary + " This can't be undone.";
        }

        private async Task Confirm()
        {
            PendingText = null;
            var settings = Settings();
            _busy = true;
            (RenewCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Status = "Renewing...";
            try
            {
                bool wasRunning = !GameObjectWorker.GetInstance().IsPaused;
                var report = await Task.Run(() =>
                    {
                        GameSession.StopClock();
                        return FleetRenewalService.Renew(settings);
                    });
                if (wasRunning) GameObjectWorker.GetInstance().Restart();
                Report.Clear();
                foreach (string line in report) Report.Add(line);
                OnPropertyChanged(nameof(HasReport));
                Status = "Done.";
            }
            catch (Exception ex)
            {
                StartupLog.Write("Fleet renewal failed: " + ex);
                Status = "That failed: " + ex.Message;
            }
            finally
            {
                _busy = false;
                Reload();
                _changed?.Invoke();
            }
        }
    }
}
