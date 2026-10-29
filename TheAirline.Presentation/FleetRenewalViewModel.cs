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
        private NamedChoice<MissingSource> _source;
        private bool _downPayment, _busy, _loading;
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
            SourceChoices.Add(new NamedChoice<MissingSource> {Name = "Buy used ones on the market", Value = MissingSource.BuyUsed});
            SourceChoices.Add(new NamedChoice<MissingSource> {Name = "Lease used ones on the market", Value = MissingSource.LeaseUsed});
            SourceChoices.Add(new NamedChoice<MissingSource> {Name = "Order new ones from the manufacturer", Value = MissingSource.OrderNew});
            SourceChoices.Add(new NamedChoice<MissingSource> {Name = "Buy used ones, order new ones for the rest", Value = MissingSource.UsedThenNew});
            SourceChoices.Add(new NamedChoice<MissingSource> {Name = "Don't get any (idle ones in the fleet only)", Value = MissingSource.IdleOnly});
            _source = SourceChoices[0];
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

        public ObservableCollection<NamedChoice<MissingSource>> SourceChoices { get; } = new ObservableCollection<NamedChoice<MissingSource>>();

        /// <summary>Where the aircraft come from when the fleet hasn't enough idle ones of the new type.</summary>
        public NamedChoice<MissingSource> Source
        {
            get { return _source; }
            set
            {
                if (SetProperty(ref _source, value))
                {
                    OnPropertyChanged(nameof(Orders));
                    UpdatePlan();
                }
            }
        }

        public bool Orders => _source?.Value == MissingSource.OrderNew || _source?.Value == MissingSource.UsedThenNew;

        /// <summary>New aircraft: the down payment now and the rest on delivery.</summary>
        public bool DownPayment
        {
            get { return _downPayment; }
            set
            {
                if (SetProperty(ref _downPayment, value)) UpdatePlan();
            }
        }

        /// <summary>New aircraft from earlier renewals still waiting for delivery.</summary>
        public ObservableCollection<string> Waiting { get; } = new ObservableCollection<string>();

        public bool HasWaiting => Waiting.Count > 0;

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
                OldType = _oldType?.Value, NewType = _newType?.Value, Source = _source?.Value ?? MissingSource.BuyUsed, DownPayment = _downPayment,
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
                    var parts = new System.Collections.Generic.List<string>();
                    if (t.Idle > 0) parts.Add(t.Idle + " idle");
                    if (t.ForSale > 0) parts.Add(t.ForSale + " used from " + Math.Round(t.UsedFrom).ToString("N0"));
                    if (t.Orderable) parts.Add("new " + Math.Round(t.NewPrice).ToString("N0"));
                    NewTypes.Add(new NamedChoice<AirlinerType>
                    {
                        Name = t.Type.Name + ": " + string.Join(", ", parts) + " (" + t.Type.CruisingSpeed.ToString("N0") + " km/h, " + t.Type.Range.ToString("N0") + " km)",
                        Value = t.Type
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
            Waiting.Clear();
            if (GameObject.GetInstance()?.HumanAirline != null)
            {
                foreach (string w in FleetRenewalService.Pending()) Waiting.Add(w);
            }
            OnPropertyChanged(nameof(HasWaiting));
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
            bool done = false;
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
                done = true;
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

            // only now: the aircraft waiting for delivery are listed again by Reload
            if (done) Status = "Done.";
        }
    }
}
