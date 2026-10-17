using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class CheckItem
    {
        public string Label { get; set; }
        public string Last { get; set; }
        public string Due { get; set; }
        public bool Overdue { get; set; }
        public string Effect { get; set; }
        public ICommand Command { get; set; }
        public bool CanDo { get; set; }
    }

    public class MaintenanceItem : ObservableObject
    {
        private string _aText, _bText, _cText, _dText;

        public FleetAirliner Aircraft { get; set; }
        public string Title { get; set; }
        public string Condition { get; set; }
        public double ConditionNumber { get; set; }
        public string Grounded { get; set; }
        public bool IsGrounded { get; set; }
        public string History { get; set; }
        public List<CheckItem> Checks { get; set; }
        public ICommand ApplyIntervalsCommand { get; set; }

        public string AText { get { return _aText; } set { SetProperty(ref _aText, value); } }
        public string BText { get { return _bText; } set { SetProperty(ref _bText, value); } }
        public string CText { get { return _cText; } set { SetProperty(ref _cText, value); } }
        public string DText { get { return _dText; } set { SetProperty(ref _dText, value); } }
    }

    /// <summary>Condition of the fleet and the A to D checks that keep it flying.</summary>
    public class MaintenanceViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status;
        private string _pendingText;
        private Action _pending;

        public MaintenanceViewModel(Action changed)
        {
            _changed = changed;
            Items = new ObservableCollection<MaintenanceItem>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<MaintenanceItem> Items { get; }
        public string Summary { get; private set; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public bool HasPending => _pending != null;

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        private void Confirm()
        {
            var action = _pending;
            SetPending(null, null);
            action?.Invoke();
        }

        public void Reload()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null)
            {
                return;
            }

            var game = GameObject.GetInstance();
            var keep = Items.ToDictionary(i => i.Aircraft, i => i);
            Items.Clear();
            var delivered = airline.Fleet.Where(f => f.Airliner.BuiltDate <= game.GameTime).OrderBy(f => f.Airliner.Condition).ToList();
            int overdue = 0;
            foreach (var f in delivered)
            {
                var aircraft = f;
                var checks = new List<CheckItem>();
                foreach (MaintenanceCheck c in Enum.GetValues(typeof(MaintenanceCheck)))
                {
                    var check = c;
                    bool late = MaintenanceService.IsOverdue(aircraft, check);
                    if (late)
                    {
                        overdue++;
                    }
                    int days = MaintenanceService.GetDowntimeDays(aircraft, check);
                    checks.Add(new CheckItem
                    {
                        Label = check + " check",
                        Last = DateFormatter.Date(MaintenanceService.GetLastDate(aircraft, check)),
                        Due = (late ? "overdue since " : "due ") + DateFormatter.Date(MaintenanceService.GetDueDate(aircraft, check)),
                        Overdue = late,
                        Effect = string.Format(
                            "{0}  |  +{1} to {2}% condition  |  {3}",
                            Money(MaintenanceService.GetCost(aircraft, check)), MaintenanceService.MinGain(check), MaintenanceService.MaxGain(check),
                            days == 0 ? "no downtime" : "grounded about " + days + (days == 1 ? " day" : " days")),
                        CanDo = !MaintenanceService.IsGrounded(aircraft),
                        Command = new RelayCommand(() => AskCheck(aircraft, check))
                    });
                }

                var history = aircraft.MaintenanceHistory.Reverse().Take(4)
                    .Select(h => DateFormatter.Date(h.Key.Date) + " " + h.Value + " check " + Money(-h.Key.Amount)).ToList();

                var item = new MaintenanceItem
                {
                    Aircraft = aircraft,
                    Title = aircraft.Name + "  -  " + aircraft.Airliner.Type.Name,
                    ConditionNumber = aircraft.Airliner.Condition,
                    Condition = string.Format("Condition {0:0}% ({1})", aircraft.Airliner.Condition, MaintenanceService.GetConditionText(aircraft.Airliner.Condition)),
                    IsGrounded = MaintenanceService.IsGrounded(aircraft),
                    Grounded = MaintenanceService.IsGrounded(aircraft) ? "Grounded until " + DateFormatter.Date(aircraft.GroundedToDate) : "",
                    History = history.Count == 0 ? "No checks done yet." : "Recent: " + string.Join("   |   ", history),
                    Checks = checks,
                    AText = (aircraft.AMaintenanceInterval > 0 ? aircraft.AMaintenanceInterval : MaintenanceService.GetIntervalDays(aircraft, MaintenanceCheck.A)).ToString(),
                    BText = MaintenanceService.GetIntervalDays(aircraft, MaintenanceCheck.B).ToString(),
                    CText = (MaintenanceService.GetIntervalDays(aircraft, MaintenanceCheck.C)/30).ToString(),
                    DText = (MaintenanceService.GetIntervalDays(aircraft, MaintenanceCheck.D)/30).ToString()
                };
                MaintenanceItem old;
                if (keep.TryGetValue(aircraft, out old))
                {
                    item.AText = old.AText;
                    item.BText = old.BText;
                    item.CText = old.CText;
                    item.DText = old.DText;
                }
                item.ApplyIntervalsCommand = new RelayCommand(() => ApplyIntervals(item));
                Items.Add(item);
            }

            double avg = delivered.Count == 0 ? 0 : delivered.Average(f => f.Airliner.Condition);
            Summary = delivered.Count == 0
                ? "You have no aircraft yet. Buy one under Airliners."
                : string.Format(
                    "{0} aircraft  |  average condition {1:0}%  |  {2} overdue checks  |  worn aircraft break down more often, and lose value. A and B checks are cheap; C and D checks ground the aircraft.",
                    delivered.Count, avg, overdue);
            OnPropertyChanged(nameof(Summary));
        }

        private void AskCheck(FleetAirliner f, MaintenanceCheck c)
        {
            int days = MaintenanceService.GetDowntimeDays(f, c);
            SetPending(
                string.Format(
                    "Do the {0} check on {1} for {2}?{3}", c, f.Name, Money(MaintenanceService.GetCost(f, c)),
                    days > 0 ? " It will be grounded for about " + days + " days." : ""),
                () => DoCheck(f, c));
        }

        private void DoCheck(FleetAirliner f, MaintenanceCheck c)
        {
            string message;
            MaintenanceService.PerformCheck(f, c, out message);
            Status = message;
            Reload();
            _changed();
        }

        private void ApplyIntervals(MaintenanceItem item)
        {
            int a, b, c, d;
            if (!int.TryParse(item.AText, out a) || !int.TryParse(item.BText, out b) || !int.TryParse(item.CText, out c) || !int.TryParse(item.DText, out d))
            {
                Status = "Enter the intervals as whole numbers.";
                return;
            }

            MaintenanceService.SetIntervals(item.Aircraft, a, b, c, d);
            Status = "Intervals for " + item.Aircraft.Name + " set (A and B in days: 30-365 and 90-730, C and D in months: 6-48 and 24-120).";
            Reload();
        }
    }
}
