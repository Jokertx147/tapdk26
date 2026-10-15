using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    /// <summary>One selectable cabin facility (seats, audio or video) with its price.</summary>
    public class FacilityChoice
    {
        public AirlinerFacility Facility { get; set; }
        public string Text { get; set; }
        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>The choice of one kind of facility for one cabin class.</summary>
    public class FacilityPicker : ObservableObject
    {
        private readonly CabinClassViewModel _owner;
        private FacilityChoice _selected;

        public FacilityPicker(CabinClassViewModel owner, AirlinerFacility.FacilityType type, List<FacilityChoice> choices)
        {
            _owner = owner;
            Type = type;
            Choices = choices;
            Title = type == AirlinerFacility.FacilityType.Seat ? "Seats"
                  : type == AirlinerFacility.FacilityType.Audio ? "Audio" : "Video";
        }

        public AirlinerFacility.FacilityType Type { get; }
        public string Title { get; }
        public List<FacilityChoice> Choices { get; }

        public FacilityChoice Selected
        {
            get { return _selected; }
            set
            {
                if (value != null && SetProperty(ref _selected, value))
                {
                    _owner.FacilityChanged(Type);
                }
            }
        }

        public AirlinerFacility Facility => _selected?.Facility;

        /// <summary>Selects without telling the owner (used while a layout is loaded).</summary>
        internal void Load(AirlinerFacility facility)
        {
            FacilityChoice choice = Choices.FirstOrDefault(c => c.Facility.Uid == facility.Uid && c.Facility.Type == facility.Type);
            if (choice == null)
            {
                choice = new FacilityChoice { Facility = facility, Text = CabinEditorViewModel.Describe(facility) };
                Choices.Add(choice);
            }

            _selected = choice;
            OnPropertyChanged(nameof(Selected));
        }
    }

    /// <summary>One class of the cabin: economy takes what the others leave, business and first have a chosen number of seats.</summary>
    public class CabinClassViewModel : ObservableObject
    {
        private readonly CabinEditorViewModel _owner;
        private int _seats;
        private int _maxSeats;
        private int _regular;

        public CabinClassViewModel(CabinEditorViewModel owner, AirlinerClass.ClassType type, int year)
        {
            _owner = owner;
            Type = type;
            Name = type == AirlinerClass.ClassType.EconomyClass ? "Economy"
                 : type == AirlinerClass.ClassType.BusinessClass ? "Business" : "First";
            Seat = new FacilityPicker(this, AirlinerFacility.FacilityType.Seat, CabinEditorViewModel.ChoicesFor(AirlinerFacility.FacilityType.Seat, year));
            Audio = new FacilityPicker(this, AirlinerFacility.FacilityType.Audio, CabinEditorViewModel.ChoicesFor(AirlinerFacility.FacilityType.Audio, year));
            Video = new FacilityPicker(this, AirlinerFacility.FacilityType.Video, CabinEditorViewModel.ChoicesFor(AirlinerFacility.FacilityType.Video, year));
            Seat.Load(AirlinerFacilities.GetBasicFacility(AirlinerFacility.FacilityType.Seat));
            Audio.Load(AirlinerFacilities.GetBasicFacility(AirlinerFacility.FacilityType.Audio));
            Video.Load(AirlinerFacilities.GetBasicFacility(AirlinerFacility.FacilityType.Video));
            RemoveCommand = new RelayCommand(() => _owner.Remove(this), () => !IsEconomy);
        }

        public AirlinerClass.ClassType Type { get; }
        public string Name { get; }
        public bool IsEconomy => Type == AirlinerClass.ClassType.EconomyClass;
        public bool CanEditSeats => !IsEconomy;
        public bool CanRemove => !IsEconomy;
        public ICommand RemoveCommand { get; }
        public FacilityPicker Seat { get; }
        public FacilityPicker Audio { get; }
        public FacilityPicker Video { get; }

        /// <summary>How much floor space one seat of this class takes, in economy seats.</summary>
        public double SeatUses
        {
            get
            {
                double uses = Seat.Facility?.SeatUses ?? 1;
                return uses > 0 ? uses : 1;
            }
        }

        /// <summary>Floor space the class takes, in economy seats.</summary>
        public int Regular => _regular;

        public int Seats
        {
            get { return _seats; }
            set
            {
                if (IsEconomy)
                {
                    return;
                }

                int v = Math.Max(1, Math.Min(Math.Max(1, _maxSeats), value));
                if (v != _seats)
                {
                    _seats = v;
                    _regular = (int) Math.Ceiling(v * SeatUses - 1e-9);
                    _owner.Recalculate();
                }
            }
        }

        public int MaxSeats => _maxSeats;

        public string Info => IsEconomy
            ? string.Format("{0} seats (what is left of the {1} seat floor)", _seats, _owner.TotalFloor)
            : string.Format("up to {0} seats", _maxSeats);

        internal void Set(int seats, int regular, int maxSeats)
        {
            _seats = seats;
            _regular = regular;
            _maxSeats = maxSeats;
            OnPropertyChanged(nameof(Seats));
            OnPropertyChanged(nameof(MaxSeats));
            OnPropertyChanged(nameof(Regular));
            OnPropertyChanged(nameof(Info));
        }

        internal void FacilityChanged(AirlinerFacility.FacilityType type)
        {
            if (type == AirlinerFacility.FacilityType.Seat && !IsEconomy)
            {
                _regular = (int) Math.Ceiling(_seats * SeatUses - 1e-9);
            }

            _owner.Recalculate();
        }
    }

    public class CabinPreset
    {
        public string Name { get; set; }
        public AirlinerConfiguration Configuration { get; set; }
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The cabin layout of an aircraft being ordered: classes, seats and onboard facilities.
    /// The airliner has a fixed floor (its maximum seating); better seats take more space per passenger.
    /// </summary>
    public class CabinEditorViewModel : ObservableObject
    {
        private readonly AirlinerPassengerType _type;
        private readonly int _year;
        private readonly Action _changed;
        private CabinPreset _preset;
        private bool _loading;

        public CabinEditorViewModel(AirlinerPassengerType type, Action changed = null)
        {
            _type = type;
            _changed = changed;
            _year = GameObject.GetInstance().GameTime.Year;
            Classes = new ObservableCollection<CabinClassViewModel>();
            AddCommand = new RelayCommand(Add, () => CanAdd);

            Presets = new List<CabinPreset> { new CabinPreset { Name = "Economy only" } };
            Presets.AddRange(
                Configurations.GetConfigurations(Configuration.ConfigurationType.Airliner)
                              .OfType<AirlinerConfiguration>()
                              .Where(
                                  c => c.MinimumSeats <= type.MaxSeatingCapacity
                                       && c.Classes.Count <= Math.Max(1, type.MaxAirlinerClasses)
                                       && c.Classes.Any(k => k.Type == AirlinerClass.ClassType.EconomyClass)
                                       && c.Classes.All(k => _year >= (int) k.Type))
                              .OrderBy(c => c.Name)
                              .Select(c => new CabinPreset { Name = c.Name, Configuration = c }));
            _preset = Presets[0];
            Reset();
        }

        /// <summary>The editor for an aircraft that exists already: starts from its classes and facilities.</summary>
        public CabinEditorViewModel(AirlinerPassengerType type, IEnumerable<AirlinerClass> existing, Action changed = null)
            : this(type, changed)
        {
            Load(existing);
        }

        /// <summary>What the seats and facilities of the classes are worth (what ordering them costs).</summary>
        public static double PriceOf(IEnumerable<AirlinerClass> classes)
        {
            double price = 0;
            foreach (AirlinerClass c in classes)
            {
                foreach (AirlinerFacility f in c.GetFacilities())
                {
                    price += f.PricePerSeat * (f.PercentOfSeats / 100) * c.SeatingCapacity;
                }
            }

            return price;
        }

        private void Load(IEnumerable<AirlinerClass> classes)
        {
            var list = classes.Where(c => c != null).ToList();
            if (list.Count == 0)
            {
                return;
            }

            _loading = true;
            Classes.Clear();
            foreach (AirlinerClass c in list.OrderBy(c => c.Type != AirlinerClass.ClassType.EconomyClass))
            {
                var vm = new CabinClassViewModel(this, c.Type, _year);
                foreach (AirlinerFacility f in c.GetFacilities())
                {
                    FacilityPicker picker = f.Type == AirlinerFacility.FacilityType.Seat ? vm.Seat
                                          : f.Type == AirlinerFacility.FacilityType.Audio ? vm.Audio : vm.Video;
                    picker.Load(f);
                }

                if (!vm.IsEconomy)
                {
                    vm.Set(c.SeatingCapacity, (int) Math.Ceiling(c.SeatingCapacity * vm.SeatUses - 1e-9), c.SeatingCapacity);
                }

                Classes.Add(vm);
            }

            if (Classes.All(c => !c.IsEconomy))
            {
                Classes.Insert(0, new CabinClassViewModel(this, AirlinerClass.ClassType.EconomyClass, _year));
            }

            _loading = false;
            Recalculate();
        }

        public ObservableCollection<CabinClassViewModel> Classes { get; }
        public List<CabinPreset> Presets { get; }
        public ICommand AddCommand { get; }
        public int TotalFloor => _type.MaxSeatingCapacity;

        public CabinPreset Preset
        {
            get { return _preset; }
            set
            {
                if (value != null && SetProperty(ref _preset, value))
                {
                    if (value.Configuration == null)
                    {
                        Reset();
                    }
                    else
                    {
                        Apply(value.Configuration);
                    }
                }
            }
        }

        public bool CanAdd => Classes.Count < Math.Max(1, _type.MaxAirlinerClasses) && NextType() != null;

        /// <summary>Extra price per aircraft for the seats and facilities.</summary>
        public double Price
        {
            get
            {
                double price = 0;
                foreach (CabinClassViewModel c in Classes)
                {
                    foreach (FacilityPicker p in new[] { c.Seat, c.Audio, c.Video })
                    {
                        AirlinerFacility f = p.Facility;
                        if (f != null)
                        {
                            price += f.PricePerSeat * (f.PercentOfSeats / 100) * c.Seats;
                        }
                    }
                }

                return price;
            }
        }

        public int TotalSeats => Classes.Sum(c => c.Seats);

        /// <summary>For example "8F | 24C | 120Y".</summary>
        public string Layout
        {
            get
            {
                int Seats(AirlinerClass.ClassType t) => Classes.FirstOrDefault(c => c.Type == t)?.Seats ?? 0;
                return string.Format(
                    "{0}F | {1}C | {2}Y",
                    Seats(AirlinerClass.ClassType.FirstClass),
                    Seats(AirlinerClass.ClassType.BusinessClass),
                    Seats(AirlinerClass.ClassType.EconomyClass));
            }
        }

        public string Summary => string.Format(
            "{0}  ({1} seats)  |  cabin extras {2} per aircraft",
            Layout,
            TotalSeats,
            Convert.ToString(new ValueCurrencyFormatter().Convert(Price)));

        public static string Describe(AirlinerFacility f)
        {
            string text = f.Name;
            if (f.PricePerSeat > 0 && f.PercentOfSeats > 0)
            {
                text += "  (" + Convert.ToString(new ValueCurrencyFormatter().Convert(f.PricePerSeat * f.PercentOfSeats / 100)) + " / seat)";
            }

            if (f.Type == AirlinerFacility.FacilityType.Seat && Math.Abs(f.SeatUses - 1) > 0.001)
            {
                text += "  [" + f.SeatUses.ToString("0.0#") + "x space]";
            }

            return text;
        }

        internal static List<FacilityChoice> ChoicesFor(AirlinerFacility.FacilityType type, int year)
        {
            return AirlinerFacilities.GetFacilities(type, year)
                                     .Select(f => new FacilityChoice { Facility = f, Text = Describe(f) })
                                     .ToList();
        }

        /// <summary>All seats economy with the basic facilities.</summary>
        public void Reset()
        {
            _loading = true;
            Classes.Clear();
            Classes.Add(new CabinClassViewModel(this, AirlinerClass.ClassType.EconomyClass, _year));
            _loading = false;
            Recalculate();
        }

        private void Apply(AirlinerConfiguration configuration)
        {
            _loading = true;
            Classes.Clear();

            // economy first, as it takes what the others leave
            foreach (AirlinerClassConfiguration conf in configuration.Classes.OrderBy(c => c.Type != AirlinerClass.ClassType.EconomyClass))
            {
                var vm = new CabinClassViewModel(this, conf.Type, _year);
                foreach (AirlinerFacility f in conf.GetFacilities())
                {
                    FacilityPicker picker = f.Type == AirlinerFacility.FacilityType.Seat ? vm.Seat
                                          : f.Type == AirlinerFacility.FacilityType.Audio ? vm.Audio : vm.Video;
                    picker.Load(f);
                }

                if (!vm.IsEconomy)
                {
                    vm.Set(conf.SeatingCapacity, (int) Math.Ceiling(conf.SeatingCapacity * vm.SeatUses - 1e-9), conf.SeatingCapacity);
                }

                Classes.Add(vm);
            }

            _loading = false;
            Recalculate();
        }

        private AirlinerClass.ClassType? NextType()
        {
            foreach (AirlinerClass.ClassType t in new[] { AirlinerClass.ClassType.BusinessClass, AirlinerClass.ClassType.FirstClass })
            {
                if (_year >= (int) t && Classes.All(c => c.Type != t))
                {
                    return t;
                }
            }

            return null;
        }

        private void Add()
        {
            AirlinerClass.ClassType? next = NextType();
            if (next == null)
            {
                return;
            }

            var vm = new CabinClassViewModel(this, next.Value, _year);
            Classes.Add(vm);
            vm.Set(1, (int) Math.Ceiling(vm.SeatUses - 1e-9), 1);
            Recalculate();
            vm.Seats = Math.Min(10, vm.MaxSeats);
        }

        internal void Remove(CabinClassViewModel cabinClass)
        {
            Classes.Remove(cabinClass);
            Recalculate();
        }

        internal void FacilityChanged()
        {
            Recalculate();
        }

        /// <summary>Gives economy what business and first leave, and works out how many seats each class can still take.</summary>
        internal void Recalculate()
        {
            if (_loading || Classes.Count == 0)
            {
                return;
            }

            CabinClassViewModel economy = Classes.First(c => c.IsEconomy);
            List<CabinClassViewModel> premium = Classes.Where(c => !c.IsEconomy).ToList();
            int economyMin = (int) Math.Ceiling(economy.SeatUses - 1e-9);

            // fit the premium classes into the floor, last one first
            foreach (CabinClassViewModel p in premium)
            {
                int others = premium.Where(o => o != p).Sum(o => o.Regular);
                int maxRegular = Math.Max(0, TotalFloor - economyMin - others);
                int maxSeats = Math.Max(1, (int) (maxRegular / p.SeatUses));
                int seats = Math.Max(1, Math.Min(maxSeats, p.Seats));
                p.Set(seats, (int) Math.Ceiling(seats * p.SeatUses - 1e-9), maxSeats);
            }

            // a premium class can only be as big as what the others leave, so shrink if the total is too big
            while (premium.Sum(p => p.Regular) > TotalFloor - economyMin && premium.Any(p => p.Seats > 1))
            {
                CabinClassViewModel biggest = premium.OrderByDescending(p => p.Regular).First();
                biggest.Set(biggest.Seats - 1, (int) Math.Ceiling((biggest.Seats - 1) * biggest.SeatUses - 1e-9), biggest.MaxSeats);
            }

            int economyFloor = TotalFloor - premium.Sum(p => p.Regular);
            economy.Set((int) (economyFloor / economy.SeatUses), economyFloor, economyFloor);

            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(Price));
            OnPropertyChanged(nameof(TotalSeats));
            OnPropertyChanged(nameof(Layout));
            OnPropertyChanged(nameof(Summary));
            (AddCommand as RelayCommand)?.RaiseCanExecuteChanged();
            _changed?.Invoke();
        }

        /// <summary>Fresh classes for one aircraft; the cabin price is already part of the order price, so nothing is charged here.</summary>
        public List<AirlinerClass> BuildClasses()
        {
            var list = new List<AirlinerClass>();
            foreach (CabinClassViewModel c in Classes)
            {
                var aClass = new AirlinerClass(c.Type, c.Seats) { RegularSeatingCapacity = c.Regular };
                foreach (FacilityPicker p in new[] { c.Seat, c.Audio, c.Video })
                {
                    aClass.ForceSetFacility(p.Facility);
                }

                list.Add(aClass);
            }

            return list;
        }
    }
}
