using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>A codeshare you have.</summary>
    public class CodesharePartnerRow
    {
        public CodeshareAgreement Agreement { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Numbers { get; set; }
        public ICommand EndCommand { get; set; }
    }

    /// <summary>An offer from a computer airline.</summary>
    public class CodeshareOfferRow
    {
        public CodeshareAgreement Agreement { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
        public ICommand AcceptCommand { get; set; }
        public ICommand DeclineCommand { get; set; }
    }

    /// <summary>An airline you could ask.</summary>
    public class CodeshareCandidateRow
    {
        public Airline Airline { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
    }

    /// <summary>Airlines > Codeshares: your partners, offers from other airlines and finding new partners.</summary>
    public class CodesharesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _search = "";
        private CodeshareCandidateRow _selected;
        private string _status, _pendingText, _summary;
        private Action _pending;

        public CodesharesViewModel(Action changed)
        {
            _changed = changed;
            Partners = new ObservableCollection<CodesharePartnerRow>();
            Offers = new ObservableCollection<CodeshareOfferRow>();
            Candidates = new ObservableCollection<CodeshareCandidateRow>();
            AskBothCommand = new RelayCommand(() => Ask(CodeshareAgreement.CodeshareType.BothWays), () => _selected != null);
            AskOneCommand = new RelayCommand(() => Ask(CodeshareAgreement.CodeshareType.OneWay), () => _selected != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<CodesharePartnerRow> Partners { get; }
        public ObservableCollection<CodeshareOfferRow> Offers { get; }
        public ObservableCollection<CodeshareCandidateRow> Candidates { get; }
        public ICommand AskBothCommand { get; }
        public ICommand AskOneCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public bool HasPartners => Partners.Count > 0;
        public bool HasOffers => Offers.Count > 0;
        public bool HasSelection => _selected != null;

        public string Explanation =>
            "A codeshare lets two airlines sell seats on each other's flights. Each partner earns 1% of the ticket income " +
            "on the other's flights it sells, and both get more passengers at airports the partner flies to. " +
            "Both ways: you each sell the other's flights. One way: you sell the partner's flights only (you earn, it doesn't).";

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) LoadCandidates(); }
        }

        public CodeshareCandidateRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    SetPending(null, null);
                    (AskBothCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (AskOneCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(Evaluation));
                }
            }
        }

        /// <summary>How the selected airline would answer, by the game's rules.</summary>
        public string Evaluation
        {
            get
            {
                if (_selected == null) return "";
                var me = GameObject.GetInstance().HumanAirline;
                var a = _selected.Airline;
                string blocker = CodeshareService.GetBlocker(me, a);
                if (blocker != null) return blocker;
                var both = CodeshareService.Reasons(a, me, CodeshareAgreement.CodeshareType.BothWays);
                var one = CodeshareService.Reasons(a, me, CodeshareAgreement.CodeshareType.OneWay);
                string fee = "Signing fee " + Money(CodeshareService.SigningFee(a, me)) + ".";
                return "Both ways: " + (both.Count == 0 ? "they would likely say yes." : string.Join(" ", both)) + Environment.NewLine
                       + "One way: " + (one.Count == 0 ? "they would likely say yes." : string.Join(" ", one)) + Environment.NewLine + fee;
            }
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string PendingText
        {
            get => _pendingText;
            private set => SetProperty(ref _pendingText, value);
        }

        public bool HasPending => _pending != null;

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
            var me = GameObject.GetInstance()?.HumanAirline;
            if (me == null) return;

            Partners.Clear();
            double income = 0, cost = 0;
            foreach (var c in me.Codeshares.ToList())
            {
                var p = CodeshareService.Partner(c, me);
                double inc = CodeshareService.MonthlyIncome(me, c), co = CodeshareService.MonthlyCost(me, c);
                income += inc;
                cost += co;
                var agreement = c;
                Partners.Add(new CodesharePartnerRow
                {
                    Agreement = c,
                    LogoPath = AirlineProfileService.GetLogo(p),
                    Name = p.Profile.Name + " (" + p.Profile.IATACode + ")",
                    Kind = CodeshareService.Describe(c, me),
                    Numbers = string.Format(
                        "{0} of their routes touch your airports  |  {1} shared airports  |  last month: you got {2}, you paid {3}",
                        CodeshareService.PartnerRoutesAtMyAirports(me, p), CodeshareService.SharedAirports(me, p), Money(inc), Money(co)),
                    EndCommand = new RelayCommand(() => SetPending(
                        "End the codeshare with " + p.Profile.Name + "?",
                        () =>
                            {
                                CodeshareService.End(me, agreement);
                                Status = "The codeshare with " + p.Profile.Name + " has ended.";
                                Reload();
                                _changed?.Invoke();
                            }))
                });
            }

            Offers.Clear();
            foreach (var o in CodeshareService.Offers(me).ToList())
            {
                var p = CodeshareService.Partner(o, me);
                var offer = o;
                Offers.Add(new CodeshareOfferRow
                {
                    Agreement = o,
                    LogoPath = AirlineProfileService.GetLogo(p),
                    Name = p.Profile.Name + " (" + p.Profile.IATACode + ") asks for a codeshare both ways",
                    Numbers = string.Format(
                        "{0} routes  |  {1} of their routes touch your airports  |  {2} shared airports",
                        p.Routes.Count, CodeshareService.PartnerRoutesAtMyAirports(me, p), CodeshareService.SharedAirports(me, p)),
                    AcceptCommand = new RelayCommand(() => Answer(offer, true)),
                    DeclineCommand = new RelayCommand(() => Answer(offer, false))
                });
            }

            OnPropertyChanged(nameof(HasPartners));
            OnPropertyChanged(nameof(HasOffers));
            Summary = Partners.Count == 0
                ? "You have no codeshare partners yet."
                : string.Format("{0} partner(s)  |  last month you got {1} and paid {2}", Partners.Count, Money(income), Money(cost));

            LoadCandidates();
        }

        private void LoadCandidates()
        {
            var me = GameObject.GetInstance()?.HumanAirline;
            if (me == null) return;
            var keep = _selected?.Airline;
            string s = _search.Trim();
            var list = CodeshareService.Candidates(me)
                                       .Where(a => s.Length == 0
                                                   || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                                   || (a.Profile.IATACode ?? "").StartsWith(s, StringComparison.OrdinalIgnoreCase)
                                                   || (a.Profile.Country?.Name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                                       .Take(60);
            Candidates.Clear();
            foreach (var a in list)
            {
                Candidates.Add(new CodeshareCandidateRow
                {
                    Airline = a,
                    LogoPath = AirlineProfileService.GetLogo(a),
                    Name = a.Profile.Name + " (" + a.Profile.IATACode + "), " + a.Profile.Country?.Name,
                    Numbers = string.Format(
                        "{0} routes  |  {1} touch your airports  |  {2} shared airports",
                        a.Routes.Count, CodeshareService.PartnerRoutesAtMyAirports(me, a), CodeshareService.SharedAirports(me, a))
                });
            }
            _selected = null;
            Selected = Candidates.FirstOrDefault(c => c.Airline == keep);
            OnPropertyChanged(nameof(Selected));
        }

        private void Ask(CodeshareAgreement.CodeshareType type)
        {
            var row = _selected;
            if (row == null) return;
            var me = GameObject.GetInstance().HumanAirline;
            string blocker = CodeshareService.GetBlocker(me, row.Airline);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            string kind = type == CodeshareAgreement.CodeshareType.BothWays ? "both ways" : "one way";
            SetPending(
                "Ask " + row.Airline.Profile.Name + " for a codeshare " + kind + "? If they agree, the signing fee is "
                + Money(CodeshareService.SigningFee(row.Airline, me)) + ". If they say no, you can ask again after "
                + CodeshareService.RefusalDays + " days.",
                () =>
                    {
                        string m;
                        bool ok = CodeshareService.Propose(me, row.Airline, type, out m);
                        Reload();
                        Status = m;
                        if (ok) _changed?.Invoke();
                    });
        }

        private void Answer(CodeshareAgreement offer, bool accept)
        {
            string m;
            CodeshareService.AnswerOffer(GameObject.GetInstance().HumanAirline, offer, accept, out m);
            Reload();
            Status = m;
            if (accept) _changed?.Invoke();
        }
    }
}
