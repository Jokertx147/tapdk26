using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class AllianceItem
    {
        public Alliance Alliance { get; set; }
        public string Text { get; set; }
    }

    public class AllianceMemberItem
    {
        public string Text { get; set; }
        public bool CanRemove { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    public class PendingMemberItem
    {
        public string Text { get; set; }
        public ICommand AcceptCommand { get; set; }
        public ICommand DeclineCommand { get; set; }
    }

    /// <summary>Alliances: look at them, join, leave, invite airlines, answer requests and start your own.</summary>
    public class AlliancesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private AllianceItem _selected;
        private Airline _invite;
        private Airport _headquarter;
        private string _newName = "";
        private string _status;
        private Action _pending;
        private string _pendingText;

        public AlliancesViewModel(Action changed = null)
        {
            _changed = changed;
            JoinCommand = new RelayCommand(AskJoin, () => CanJoin);
            LeaveCommand = new RelayCommand(AskLeave, () => IsMember);
            InviteCommand = new RelayCommand(DoInvite, () => IsMember && _invite != null);
            CreateCommand = new RelayCommand(Create);
            ConfirmCommand = new RelayCommand(
                () =>
                    {
                        var action = _pending;
                        PendingText = null;
                        action?.Invoke();
                    });
            CancelCommand = new RelayCommand(() => PendingText = null);
            Reload();
        }

        public ObservableCollection<AllianceItem> Alliances { get; } = new ObservableCollection<AllianceItem>();
        public ObservableCollection<AllianceMemberItem> Members { get; } = new ObservableCollection<AllianceMemberItem>();
        public ObservableCollection<PendingMemberItem> Requests { get; } = new ObservableCollection<PendingMemberItem>();
        public ObservableCollection<Airline> InviteCandidates { get; } = new ObservableCollection<Airline>();
        public List<Airport> Headquarters { get; private set; } = new List<Airport>();

        public ICommand JoinCommand { get; }
        public ICommand LeaveCommand { get; }
        public ICommand InviteCommand { get; }
        public ICommand CreateCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public AllianceItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    LoadDetail();
                }
            }
        }

        public Airline Invite
        {
            get { return _invite; }
            set
            {
                if (SetProperty(ref _invite, value))
                {
                    (InviteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public Airport Headquarter
        {
            get { return _headquarter; }
            set { SetProperty(ref _headquarter, value); }
        }

        public string NewName
        {
            get { return _newName; }
            set { SetProperty(ref _newName, value ?? ""); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

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
        public string Title { get; private set; }
        public string Info { get; private set; }
        public bool HasSelection => _selected != null;
        public bool IsMember => _selected != null && _selected.Alliance.Members.Any(m => m.Airline == Human);
        public bool CanJoin => _selected != null && !IsMember;
        public bool HasRequests => Requests.Count > 0;

        private static Airline Human => GameObject.GetInstance().HumanAirline;

        public void Reload()
        {
            var keep = _selected?.Alliance;
            Alliances.Clear();
            foreach (var a in Models.Airlines.Alliances.GetAlliances().OrderByDescending(a => a.IsHumanAlliance).ThenBy(a => a.Name))
            {
                Alliances.Add(
                    new AllianceItem
                    {
                        Alliance = a,
                        Text = string.Format(
                            "{0}{1}  |  {2} member(s)  |  founded {3:MMM yyyy}",
                            a.Name, a.Members.Any(m => m.Airline == Human) ? "  (yours)" : "", a.Members.Count, a.FormationDate)
                    });
            }

            Headquarters = Human == null ? new List<Airport>() : Human.Airports.OrderBy(a => a.Profile.Name).ToList();
            _headquarter = _headquarter != null && Headquarters.Contains(_headquarter) ? _headquarter : Headquarters.FirstOrDefault();
            OnPropertyChanged(nameof(Headquarters));
            OnPropertyChanged(nameof(Headquarter));

            _selected = Alliances.FirstOrDefault(a => a.Alliance == keep) ?? Alliances.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            LoadDetail();
        }

        private void LoadDetail()
        {
            Members.Clear();
            Requests.Clear();
            InviteCandidates.Clear();
            var human = Human;

            if (_selected == null)
            {
                Title = Info = "";
            }
            else
            {
                var alliance = _selected.Alliance;
                Title = alliance.Name;
                Info = string.Format(
                    "Headquarters {0}  |  founded {1:d MMM yyyy}  |  {2} routes in total",
                    alliance.Headquarter?.Profile.Name ?? "-", alliance.FormationDate, alliance.Members.Sum(m => m.Airline.Routes.Count));

                bool member = alliance.Members.Any(m => m.Airline == human);
                foreach (var m in alliance.Members.ToList())
                {
                    var captured = m;
                    Members.Add(
                        new AllianceMemberItem
                        {
                            Text = string.Format(
                                "{0}  |  {1}  |  {2} aircraft, {3} routes  |  joined {4:MMM yyyy}",
                                m.Airline.Profile.Name, m.Airline.Profile.Country?.Name, m.Airline.Fleet.Count, m.Airline.Routes.Count, m.JoinedDate),
                            CanRemove = member && m.Airline != human,
                            RemoveCommand = new RelayCommand(() => AskRemove(alliance, captured.Airline))
                        });
                }

                if (member)
                {
                    foreach (var p in alliance.PendingMembers.ToList())
                    {
                        var captured = p;
                        Requests.Add(
                            new PendingMemberItem
                            {
                                Text = p.Airline.Profile.Name + " asks to join (" + p.Date.ToString("d MMM yyyy") + ")",
                                AcceptCommand = new RelayCommand(() => Answer(alliance, captured, true)),
                                DeclineCommand = new RelayCommand(() => Answer(alliance, captured, false))
                            });
                    }

                    foreach (var a in Airlines.GetAllAirlines()
                                              .Where(a => !a.IsHuman && alliance.Members.All(m => m.Airline != a)
                                                          && a.Profile.Founded <= GameObject.GetInstance().GameTime.Year
                                                          && a.Profile.Folded >= GameObject.GetInstance().GameTime.Year)
                                              .OrderBy(a => a.Profile.Name))
                    {
                        InviteCandidates.Add(a);
                    }
                }
            }

            _invite = InviteCandidates.FirstOrDefault();
            OnPropertyChanged(nameof(Invite));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsMember));
            OnPropertyChanged(nameof(CanJoin));
            OnPropertyChanged(nameof(HasRequests));
            (JoinCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LeaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InviteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AskJoin()
        {
            var alliance = _selected.Alliance;
            _pending = () =>
                {
                    if (AIHelpers.CanJoinAlliance(Human, alliance))
                    {
                        alliance.AddMember(new AllianceMember(Human, GameObject.GetInstance().GameTime));
                        Done("You joined " + alliance.Name + ".");
                    }
                    else
                    {
                        Status = alliance.Name + " turned down your request: your airline is too small, or too similar to its members.";
                    }
                };
            PendingText = "Ask to join " + alliance.Name + "?";
        }

        private void AskLeave()
        {
            var alliance = _selected.Alliance;
            _pending = () =>
                {
                    alliance.RemoveMember(Human);
                    if (alliance.Members.Count == 0 && alliance.PendingMembers.Count == 0)
                    {
                        Models.Airlines.Alliances.RemoveAlliance(alliance);
                    }

                    Done("You left " + alliance.Name + ".");
                };
            PendingText = "Leave " + alliance.Name + "?";
        }

        private void AskRemove(Alliance alliance, Airline airline)
        {
            if (!AIHelpers.CanRemoveFromAlliance(Human, airline, alliance))
            {
                Status = "You are not big enough to remove " + airline.Profile.Name + " from the alliance.";
                return;
            }

            _pending = () =>
                {
                    alliance.RemoveMember(airline);
                    Done(airline.Profile.Name + " was removed from " + alliance.Name + ".");
                };
            PendingText = "Remove " + airline.Profile.Name + " from " + alliance.Name + "?";
        }

        private void Answer(Alliance alliance, PendingAllianceMember pending, bool accept)
        {
            if (accept)
            {
                alliance.AddMember(new AllianceMember(pending.Airline, GameObject.GetInstance().GameTime));
            }

            alliance.RemovePendingMember(pending);
            Done(accept ? pending.Airline.Profile.Name + " joined." : "Request from " + pending.Airline.Profile.Name + " declined.");
        }

        private void DoInvite()
        {
            if (_selected == null || _invite == null)
            {
                return;
            }

            var alliance = _selected.Alliance;
            var airline = _invite;
            if (AIHelpers.DoAcceptAllianceInvitation(airline, alliance))
            {
                alliance.AddMember(new AllianceMember(airline, GameObject.GetInstance().GameTime));
                Done(airline.Profile.Name + " accepted and joined " + alliance.Name + ".");
            }
            else
            {
                Status = airline.Profile.Name + " declined the invitation to " + alliance.Name + ".";
            }
        }

        private void Create()
        {
            string name = _newName.Trim();
            if (name.Length == 0)
            {
                Status = "Give the alliance a name first.";
                return;
            }

            if (Models.Airlines.Alliances.GetAlliances().Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Status = "There is an alliance with that name already.";
                return;
            }

            if (_headquarter == null)
            {
                Status = "Pick the airport for its headquarters (one of your airports).";
                return;
            }

            var alliance = new Alliance(GameObject.GetInstance().GameTime, name, _headquarter)
            {
                Logo = AppSettings.GetDataPath() + "/graphics/alliancelogos/default.png"
            };
            alliance.AddMember(new AllianceMember(Human, GameObject.GetInstance().GameTime));
            Models.Airlines.Alliances.AddAlliance(alliance);
            NewName = "";
            Done("Created " + name + ".");
            Selected = Alliances.FirstOrDefault(a => a.Alliance == alliance);
        }

        private void Done(string status)
        {
            Reload();
            Status = status;
            _changed?.Invoke();
        }
    }
}
