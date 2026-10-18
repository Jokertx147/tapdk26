using System;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>The airline's flying license and the upgrade to a higher one.</summary>
    public class LicenseViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status, _pendingText;
        private Action _pending;

        public LicenseViewModel(Action changed)
        {
            _changed = changed;
            UpgradeCommand = new RelayCommand(AskUpgrade);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ICommand UpgradeCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public string Current { get; private set; }
        public string Allows { get; private set; }
        public string NextText { get; private set; }
        public bool CanUpgrade { get; private set; }
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

        public void Reload()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            if (airline == null) return;
            Current = "License: " + LicenseService.Name(airline.License);
            Allows = LicenseService.Allows(airline.License);
            if (LicenseService.IsHighest(airline))
            {
                NextText = "This is the highest license. You can fly anywhere.";
                CanUpgrade = false;
            }
            else
            {
                var next = LicenseService.Next(airline);
                NextText = "Next: " + LicenseService.Name(next) + " license for " + Money(LicenseService.Price(airline)) + ". " + LicenseService.Allows(next);
                CanUpgrade = true;
            }
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(Allows));
            OnPropertyChanged(nameof(NextText));
            OnPropertyChanged(nameof(CanUpgrade));
        }

        private void AskUpgrade()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            string blocker = LicenseService.GetBlocker(airline);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            SetPending(
                string.Format("Buy the {0} license for {1}?", LicenseService.Name(LicenseService.Next(airline)), Money(LicenseService.Price(airline))),
                () =>
                {
                    string m;
                    LicenseService.Upgrade(airline, out m);
                    Status = m;
                    Reload();
                    _changed?.Invoke();
                });
        }
    }
}
