using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Pilots;

namespace TheAirline.Presentation
{
    public class SchoolItem
    {
        public FlightSchool School { get; set; }
        public string Title { get; set; }
        public string Info { get; set; }
        public override string ToString() { return Title; }
    }

    public class InstructorItem
    {
        public Instructor Instructor { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public string ActionText { get; set; }
        public ICommand Command { get; set; }
    }

    public class TrainingAircraftItem
    {
        public TrainingAircraft Aircraft { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ICommand SellCommand { get; set; }
    }

    public class StudentItem
    {
        public PilotStudent Student { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ICommand CancelCommand { get; set; }
    }

    /// <summary>Flight schools: pilots of your own, trained by instructors in training aircraft.</summary>
    public class FlightSchoolsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private SchoolItem _selected;
        private NamedChoice<Airport> _buildAirport;
        private NamedChoice<TrainingAircraftType> _buyType;
        private NamedChoice<Instructor> _studentInstructor;
        private string _family;
        private string _status, _pendingText;
        private Action _pending;

        public FlightSchoolsViewModel(Action changed)
        {
            _changed = changed;
            Schools = new ObservableCollection<SchoolItem>();
            BuildAirports = new ObservableCollection<NamedChoice<Airport>>();
            Instructors = new ObservableCollection<InstructorItem>();
            Market = new ObservableCollection<InstructorItem>();
            Aircraft = new ObservableCollection<TrainingAircraftItem>();
            AircraftTypes = new ObservableCollection<NamedChoice<TrainingAircraftType>>();
            Students = new ObservableCollection<StudentItem>();
            FreeInstructors = new ObservableCollection<NamedChoice<Instructor>>();
            Families = new ObservableCollection<string>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            BuildCommand = new RelayCommand(AskBuild);
            CloseCommand = new RelayCommand(AskClose);
            BuyAircraftCommand = new RelayCommand(AskBuyAircraft);
            StartStudentCommand = new RelayCommand(AskStartStudent);
            Reload();
        }

        public ObservableCollection<SchoolItem> Schools { get; }
        public ObservableCollection<NamedChoice<Airport>> BuildAirports { get; }
        public ObservableCollection<InstructorItem> Instructors { get; }
        public ObservableCollection<InstructorItem> Market { get; }
        public ObservableCollection<TrainingAircraftItem> Aircraft { get; }
        public ObservableCollection<NamedChoice<TrainingAircraftType>> AircraftTypes { get; }
        public ObservableCollection<StudentItem> Students { get; }
        public ObservableCollection<NamedChoice<Instructor>> FreeInstructors { get; }
        public ObservableCollection<string> Families { get; }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BuildCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand BuyAircraftCommand { get; }
        public ICommand StartStudentCommand { get; }

        public string Summary { get; private set; }
        public string SchoolInfo { get; private set; }
        public string StudentHint { get; private set; }
        public bool HasSchool => _selected != null;
        public bool HasSchools => Schools.Count > 0;
        public bool CanBuild => BuildAirports.Count > 0;
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

        public SchoolItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    ReloadSchool();
                    OnPropertyChanged(nameof(HasSchool));
                }
            }
        }

        public NamedChoice<Airport> BuildAirport
        {
            get { return _buildAirport; }
            set { SetProperty(ref _buildAirport, value); }
        }

        public NamedChoice<TrainingAircraftType> BuyType
        {
            get { return _buyType; }
            set { SetProperty(ref _buyType, value); }
        }

        public NamedChoice<Instructor> StudentInstructor
        {
            get { return _studentInstructor; }
            set { SetProperty(ref _studentInstructor, value); }
        }

        public string Family
        {
            get { return _family; }
            set { SetProperty(ref _family, value); }
        }

        private static string Money(double v)
        {
            return v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
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

        private void Done(string message)
        {
            Status = message;
            Reload();
            _changed?.Invoke();
        }

        public void Reload()
        {
            var game = GameObject.GetInstance();
            var airline = game.HumanAirline;
            if (airline == null)
            {
                return;
            }

            PilotSupply.EnsureInstructors();
            var keep = _selected?.School;
            Schools.Clear();
            foreach (var s in airline.FlightSchools.OrderBy(s => s.Name))
            {
                Schools.Add(
                    new SchoolItem
                    {
                        School = s,
                        Title = s.Name + " (" + s.Airport.Profile.IATACode + ")",
                        Info = string.Format(
                            "{0}/{1} instructors  |  {2} of {3} student places in use  |  {4} training aircraft",
                            s.NumberOfInstructors, FlightSchool.MaxNumberOfInstructors, s.NumberOfStudents, FlightSchoolService.Capacity(s), s.TrainingAircrafts.Count)
                    });
            }

            _selected = Schools.FirstOrDefault(s => s.School == keep) ?? Schools.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasSchool));
            OnPropertyChanged(nameof(HasSchools));

            var prev = _buildAirport?.Value;
            BuildAirports.Clear();
            foreach (var a in FlightSchoolService.PossibleAirports(airline))
            {
                BuildAirports.Add(new NamedChoice<Airport> {Name = a.Profile.IATACode + "  " + a.Profile.Name + ", " + a.Profile.Town.Name, Value = a});
            }
            _buildAirport = BuildAirports.FirstOrDefault(b => b.Value == prev) ?? BuildAirports.FirstOrDefault();
            OnPropertyChanged(nameof(BuildAirport));
            OnPropertyChanged(nameof(CanBuild));

            int students = airline.FlightSchools.Sum(s => s.NumberOfStudents);
            int instructors = airline.FlightSchools.Sum(s => s.NumberOfInstructors);
            Summary = airline.FlightSchools.Count == 0
                ? "You have no flight school. Build one at a hub or at an airport with a service facility, hire instructors, buy a training aircraft, and train pilots of your own for "
                  + Money(FlightSchoolService.StudentPrice) + " each."
                : string.Format(
                    "{0} flight school(s)  |  {1} instructors  |  {2} students in training. A school costs {3} to build, a student {4}.",
                    airline.FlightSchools.Count, instructors, students, Money(FlightSchoolService.BuildPrice), Money(FlightSchoolService.StudentPrice));
            OnPropertyChanged(nameof(Summary));

            ReloadSchool();
        }

        private void ReloadSchool()
        {
            Instructors.Clear();
            Market.Clear();
            Aircraft.Clear();
            Students.Clear();
            FreeInstructors.Clear();
            AircraftTypes.Clear();
            Families.Clear();

            var game = GameObject.GetInstance();
            var airline = game.HumanAirline;
            var school = _selected?.School;
            if (school == null || airline == null)
            {
                SchoolInfo = "";
                StudentHint = "";
                OnPropertyChanged(nameof(SchoolInfo));
                OnPropertyChanged(nameof(StudentHint));
                return;
            }

            double salaryBase = airline.Fees.GetValue(FeeTypes.GetType("Instructor Base Salary"));
            foreach (var i in school.Instructors.OrderBy(i => i.Profile.Name))
            {
                var instructor = i;
                Instructors.Add(
                    new InstructorItem
                    {
                        Instructor = instructor,
                        Text = instructor.Profile.Name + "  (rating " + instructor.Rating.Name + ")",
                        Detail = string.Format("{0}/{1} students  |  {2} a month", instructor.Students.Count, FlightSchool.MaxNumberOfStudentsPerInstructor, Money(instructor.Rating.CostIndex*salaryBase)),
                        ActionText = "Dismiss",
                        Command = new RelayCommand(() => AskDismiss(school, instructor))
                    });
            }

            if (school.NumberOfInstructors < FlightSchool.MaxNumberOfInstructors)
            {
                foreach (var i in TheAirline.Models.Pilots.Instructors.GetUnassignedInstructors().OrderBy(i => i.Rating.CostIndex).ThenBy(i => i.Profile.Name).Take(60))
                {
                    var instructor = i;
                    Market.Add(
                        new InstructorItem
                        {
                            Instructor = instructor,
                            Text = instructor.Profile.Name + "  (rating " + instructor.Rating.Name + ")",
                            Detail = string.Format("age {0}  |  {1} a month", instructor.Profile.Age, Money(instructor.Rating.CostIndex*salaryBase)),
                            ActionText = "Hire",
                            Command = new RelayCommand(() => AskHire(school, instructor))
                        });
                }
            }

            foreach (var a in school.TrainingAircrafts.OrderBy(a => a.Type.TypeLevel))
            {
                var aircraft = a;
                Aircraft.Add(
                    new TrainingAircraftItem
                    {
                        Aircraft = aircraft,
                        Text = aircraft.Type.Name,
                        Detail = string.Format(
                            "{0} seats, {1} in use  |  bought {2}  |  sells for {3}",
                            aircraft.Type.MaxNumberOfStudents, aircraft.Type.MaxNumberOfStudents - FlightSchoolService.FreeSeats(school, aircraft), DateFormatter.Date(aircraft.BoughtDate), Money(aircraft.Type.Price*0.75)),
                        SellCommand = new RelayCommand(() => AskSell(school, aircraft))
                    });
            }

            foreach (var t in TrainingAircraftTypes.GetAircraftTypes().OrderBy(t => t.TypeLevel))
            {
                AircraftTypes.Add(new NamedChoice<TrainingAircraftType> {Name = t.Name + "  -  " + Money(t.Price) + ", " + t.MaxNumberOfStudents + " seats", Value = t});
            }
            _buyType = AircraftTypes.FirstOrDefault(c => c.Value == _buyType?.Value) ?? AircraftTypes.FirstOrDefault();
            OnPropertyChanged(nameof(BuyType));

            foreach (var s in school.Students.OrderBy(s => s.EndDate))
            {
                var student = s;
                int left = Math.Max(0, (int) (student.EndDate.Date - game.GameTime.Date).TotalDays);
                Students.Add(
                    new StudentItem
                    {
                        Student = student,
                        Text = student.Profile.Name + "  -  " + student.AirlinerFamily,
                        Detail = string.Format(
                            "rating {0}  |  instructor {1}  |  {2}  |  graduates {3} ({4} days)",
                            student.Rating?.Name, student.Instructor?.Profile.Name, student.Aircraft?.Type.Name, DateFormatter.Date(student.EndDate), left),
                        CancelCommand = new RelayCommand(() => AskCancel(school, student))
                    });
            }

            foreach (var i in FlightSchoolService.InstructorsWithRoom(school).OrderBy(i => i.Profile.Name))
            {
                FreeInstructors.Add(new NamedChoice<Instructor> {Name = i.Profile.Name + " (" + i.Students.Count + "/" + FlightSchool.MaxNumberOfStudentsPerInstructor + ")", Value = i});
            }
            _studentInstructor = FreeInstructors.FirstOrDefault(c => c.Value == _studentInstructor?.Value) ?? FreeInstructors.FirstOrDefault();
            OnPropertyChanged(nameof(StudentInstructor));

            foreach (var f in FlightSchoolService.Families())
            {
                Families.Add(f);
            }
            if (_family == null || !Families.Contains(_family))
            {
                _family = airline.Fleet.Select(f => f.Airliner.Type.AirlinerFamily).FirstOrDefault(f => Families.Contains(f)) ?? Families.FirstOrDefault();
            }
            OnPropertyChanged(nameof(Family));

            SchoolInfo = string.Format(
                "{0}  |  {1} instructors  |  {2} training seats  |  room for {3} students ({4} in training)",
                school.Name, school.NumberOfInstructors, FlightSchoolService.Seats(school), FlightSchoolService.Capacity(school), school.NumberOfStudents);
            StudentHint = FlightSchoolService.GetStudentBlocker(airline, school)
                          ?? "A student costs " + Money(FlightSchoolService.StudentPrice) + " and graduates in about 70 to 150 days, depending on the rating. The pilot then joins your airline and goes to an aircraft that needs a pilot of that family.";
            OnPropertyChanged(nameof(SchoolInfo));
            OnPropertyChanged(nameof(StudentHint));
        }

        private void AskBuild()
        {
            var airport = _buildAirport?.Value;
            if (airport == null)
            {
                Status = "You have no airport that can take a flight school: build it at a hub or at an airport with a service facility.";
                return;
            }
            SetPending(
                "Build a flight school at " + airport.Profile.Name + " for " + Money(FlightSchoolService.BuildPrice) + "?",
                () =>
                {
                    string m;
                    var airline = GameObject.GetInstance().HumanAirline;
                    bool ok = FlightSchoolService.Build(airline, airport, out m);
                    if (ok)
                    {
                        _selected = null;
                        Reload();
                        _selected = Schools.FirstOrDefault(s => s.School.Airport == airport) ?? _selected;
                        OnPropertyChanged(nameof(Selected));
                        ReloadSchool();
                        Status = m;
                        _changed?.Invoke();
                    }
                    else
                    {
                        Status = m;
                    }
                });
        }

        private void AskClose()
        {
            var school = _selected?.School;
            if (school == null) return;
            SetPending(
                "Close " + school.Name + "? Its " + school.NumberOfStudents + " student(s) are sent home and their fee is lost; the training aircraft are sold for 75% and the instructors go back to the market.",
                () =>
                {
                    string m;
                    FlightSchoolService.Close(GameObject.GetInstance().HumanAirline, school, out m);
                    _selected = null;
                    Done(m);
                });
        }

        private void AskHire(FlightSchool school, Instructor instructor)
        {
            SetPending(
                "Hire " + instructor.Profile.Name + " (rating " + instructor.Rating.Name + ") as an instructor at " + school.Name + "?",
                () =>
                {
                    string m;
                    FlightSchoolService.HireInstructor(GameObject.GetInstance().HumanAirline, school, instructor, out m);
                    Done(m);
                });
        }

        private void AskDismiss(FlightSchool school, Instructor instructor)
        {
            if (instructor.Students.Count > 0)
            {
                string m;
                FlightSchoolService.DismissInstructor(school, instructor, out m);
                Status = m;
                return;
            }
            SetPending(
                "Dismiss " + instructor.Profile.Name + "?",
                () =>
                {
                    string m;
                    FlightSchoolService.DismissInstructor(school, instructor, out m);
                    Done(m);
                });
        }

        private void AskBuyAircraft()
        {
            var school = _selected?.School;
            var type = _buyType?.Value;
            if (school == null || type == null) return;
            SetPending(
                "Buy a " + type.Name + " for " + school.Name + " at " + Money(type.Price) + "? It gives " + type.MaxNumberOfStudents + " student seats.",
                () =>
                {
                    string m;
                    FlightSchoolService.BuyAircraft(GameObject.GetInstance().HumanAirline, school, type, out m);
                    Done(m);
                });
        }

        private void AskSell(FlightSchool school, TrainingAircraft aircraft)
        {
            if (school.Students.Any(s => s.Aircraft == aircraft))
            {
                string m;
                FlightSchoolService.SellAircraft(GameObject.GetInstance().HumanAirline, school, aircraft, out m);
                Status = m;
                return;
            }
            SetPending(
                "Sell the " + aircraft.Type.Name + " for " + Money(aircraft.Type.Price*0.75) + "?",
                () =>
                {
                    string m;
                    FlightSchoolService.SellAircraft(GameObject.GetInstance().HumanAirline, school, aircraft, out m);
                    Done(m);
                });
        }

        private void AskStartStudent()
        {
            var school = _selected?.School;
            if (school == null) return;
            var airline = GameObject.GetInstance().HumanAirline;
            string blocker = FlightSchoolService.GetStudentBlocker(airline, school);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            var instructor = _studentInstructor?.Value;
            if (instructor == null || string.IsNullOrEmpty(_family))
            {
                Status = "Choose an instructor and an aircraft family.";
                return;
            }
            SetPending(
                "Take in a new student with " + instructor.Profile.Name + ", trained for the " + _family + ", for " + Money(FlightSchoolService.StudentPrice) + "?",
                () =>
                {
                    string m;
                    FlightSchoolService.StartStudent(GameObject.GetInstance().HumanAirline, school, instructor, _family, out m);
                    Done(m);
                });
        }

        private void AskCancel(FlightSchool school, PilotStudent student)
        {
            SetPending(
                "Send " + student.Profile.Name + " home? The fee is not paid back.",
                () =>
                {
                    FlightSchoolService.CancelStudent(school, student);
                    Done(student.Profile.Name + " left the school.");
                });
        }
    }
}
