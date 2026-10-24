using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Infrastructure;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;

namespace TheAirline.Presentation
{
    /// <summary>Choose an airline, a start year and a difficulty, then start a new game.</summary>
    /// <summary>A region with the name shown to the player (from the region translations).</summary>
    public class RegionChoice
    {
        public RegionChoice(Region region, string name)
        {
            Region = region;
            Name = name ?? TranslatedName(region);
        }

        public Region Region { get; }
        public string Name { get; }

        private static string TranslatedName(Region region)
        {
            return Regions.GetDisplayName(region);
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class NewGameViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private bool _isLoading = true;
        private bool _isCreating;
        private string _status = "Loading the game data...";
        private int _startYear = 2007;
        private Airline _selectedAirline;
        private string _ceo = "";
        private DifficultyLevel _selectedDifficulty;
        private int _opponents = 12;
        private bool _useDayTurns = true;
        private bool _majorAirports, _sameRegion = true;
        private List<Continent> _continents = new List<Continent>();
        private List<RegionChoice> _regions = new List<RegionChoice>();
        private Continent _continent;
        private RegionChoice _region;
        private List<Airline> _airlines = new List<Airline>();
        private List<DifficultyLevel> _difficulties = new List<DifficultyLevel>();

        public NewGameViewModel(ShellViewModel shell)
        {
            _shell = shell;

            StartYears = Enumerable.Range(1960, 2030 - 1960 + 1).ToList();
            // more than about 50 makes setting up a game and every game day very slow
            OpponentChoices = new List<int> { 1, 3, 5, 8, 12, 20, 30, 50 };
            OpponentOptions = OpponentChoices.Select(n => new NamedChoice<int> { Name = n.ToString(), Value = n }).ToList();

            CreateCommand = new AsyncCommand(CreateAsync, () => !_isLoading && !_isCreating && SelectedAirline != null);
            BackCommand = new RelayCommand(shell.ShowMainMenu, () => !_isCreating);

            var ignore = LoadAsync();
        }

        public List<int> StartYears { get; }

        public List<int> OpponentChoices { get; }

        public List<NamedChoice<int>> OpponentOptions { get; }

        public NamedChoice<int> SelectedOpponentOption
        {
            get { return OpponentOptions.FirstOrDefault(o => o.Value == _opponents) ?? OpponentOptions[0]; }
            set { if (value != null) Opponents = value.Value; }
        }

        /// <summary>How many airlines there are to compete with in the chosen year.</summary>
        public string OpponentsText
        {
            get
            {
                if (_isLoading) return null;
                int year = StartYear;
                int n = Models.Airlines.Airlines.GetAirlines(a => a.Profile.Founded <= year && a.Profile.Folded > year).Count - 1;
                int playing = Math.Min(n, _opponents);
                return string.Format(
                    "{0} other airlines flew in {1}; {2} of them will compete with you{3}. More airlines mean more competition and higher-fare routes get crowded; each game day also takes longer.",
                    Math.Max(0, n), year, Math.Max(0, playing), SameRegion ? " (the ones nearest to you)" : " (picked at random)");
            }
        }

        public ICommand CreateCommand { get; }

        public ICommand BackCommand { get; }

        public bool IsBusy => _isLoading || _isCreating;

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public int StartYear
        {
            get { return _startYear; }
            set
            {
                if (SetProperty(ref _startYear, value))
                {
                    RefreshAirlines();
                }
            }
        }

        public List<Airline> Airlines
        {
            get { return _airlines; }
            private set { SetProperty(ref _airlines, value); }
        }

        public Airline SelectedAirline
        {
            get { return _selectedAirline; }
            set
            {
                if (SetProperty(ref _selectedAirline, value))
                {
                    if (value != null)
                    {
                        Ceo = value.Profile.CEO ?? "";
                        OnPropertyChanged(nameof(Narrative));
                    }

                    (CreateCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Narrative => _selectedAirline?.Profile.Narrative ?? "";

        public string Ceo
        {
            get { return _ceo; }
            set { SetProperty(ref _ceo, value); }
        }

        public List<DifficultyLevel> Difficulties
        {
            get { return _difficulties; }
            private set { SetProperty(ref _difficulties, value); }
        }

        public DifficultyLevel SelectedDifficulty
        {
            get { return _selectedDifficulty; }
            set { SetProperty(ref _selectedDifficulty, value); }
        }

        public int Opponents
        {
            get { return _opponents; }
            set
            {
                if (SetProperty(ref _opponents, value))
                {
                    OnPropertyChanged(nameof(SelectedOpponentOption));
                    OnPropertyChanged(nameof(OpponentsText));
                }
            }
        }

        public List<Continent> Continents
        {
            get { return _continents; }
            private set { SetProperty(ref _continents, value); }
        }

        public Continent SelectedContinent
        {
            get { return _continent; }
            set
            {
                if (value != null && SetProperty(ref _continent, value))
                {
                    var list = new List<RegionChoice> { new RegionChoice(Models.General.Countries.Regions.GetRegion("100"), "All regions") };
                    if (value.Uid != "100")
                    {
                        list.AddRange(value.Regions.Select(r => new RegionChoice(r, null)));
                    }
                    _region = list[0];
                    Regions = list;
                    OnPropertyChanged(nameof(SelectedRegion));
                    RefreshAirlines();
                }
            }
        }

        public List<RegionChoice> Regions
        {
            get { return _regions; }
            private set { SetProperty(ref _regions, value); }
        }

        public RegionChoice SelectedRegion
        {
            get { return _region; }
            set
            {
                if (value != null && SetProperty(ref _region, value))
                {
                    RefreshAirlines();
                }
            }
        }

        /// <summary>Start with the major airports only.</summary>
        public bool MajorAirports
        {
            get { return _majorAirports; }
            set { SetProperty(ref _majorAirports, value); }
        }

        /// <summary>Opponents are airlines from the same region as the player.</summary>
        public bool SameRegion
        {
            get { return _sameRegion; }
            set { SetProperty(ref _sameRegion, value); }
        }

        public bool UseDayTurns
        {
            get { return _useDayTurns; }
            set { SetProperty(ref _useDayTurns, value); }
        }

        private async Task LoadAsync()
        {
            try
            {
                await GameSession.EnsureDataLoadedAsync();

                var allContinents = new Continent("100", "All continents");
                allContinents.Regions = Models.General.Countries.Regions.GetRegions();
                Continents = new List<Continent> { allContinents };
                Continents.AddRange(Models.General.Countries.Continents.GetContinents());
                _isLoading = true;
                SelectedContinent = Continents[0];

                Difficulties = DifficultyLevels.GetDifficultyLevels();
                SelectedDifficulty = Difficulties.FirstOrDefault(d => d.Name == "Normal") ?? Difficulties.FirstOrDefault();

                _isLoading = false;
                RefreshAirlines();
            }
            catch (Exception ex)
            {
                StartupLog.Write("New game: loading the data failed: " + ex);
                Status = "Loading the game data failed: " + ex.Message;
                return;
            }
            finally
            {
                OnPropertyChanged(nameof(IsBusy));
                (CreateCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }

        // the airlines that exist in the chosen year
        private void RefreshAirlines()
        {
            if (_isLoading)
            {
                return;
            }

            int year = StartYear;
            Airlines = Models.Airlines.Airlines.GetAirlines(a => a.Profile.Founded <= year && a.Profile.Folded > year && InSelectedArea(a))
                                               .OrderBy(a => a.Profile.Name)
                                               .ToList();
            SelectedAirline = Airlines.FirstOrDefault();
            Status = Airlines.Count + " airlines in " + year;
            OnPropertyChanged(nameof(OpponentsText));
        }

        private bool InSelectedArea(Airline a)
        {
            var region = a.Profile.Country?.Region;
            if (_region == null || _region.Region.Uid == "100")
            {
                return _continent == null || _continent.Uid == "100" || _continent.HasRegion(region);
            }

            return region != null && region.Uid == _region.Region.Uid;
        }

        private async Task CreateAsync()
        {
            Airline airline = SelectedAirline;
            _isCreating = true;
            OnPropertyChanged(nameof(IsBusy));
            (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Status = "Creating the game, this takes a little while...";

            try
            {
                // airports are only active relative to the game clock, so set it to the start year first
                GameObject.GetInstance().GameTime = new DateTime(StartYear, 1, 1);

                Airport airport = FindHomeAirport(airline);
                if (airport == null)
                {
                    Status = "No airport found for " + airline.Profile.Name + " in " + StartYear;
                    return;
                }

                var startData = new StartDataObject
                    {
                        Airline = airline,
                        Airport = airport,
                        CEO = string.IsNullOrWhiteSpace(Ceo) ? "CEO" : Ceo.Trim(),
                        // uid "100" is the "all" entry the game setup code checks for
                        Continent = _continent ?? new Continent("100", "All continents"),
                        Difficulty = SelectedDifficulty,
                        Focus = default(Infrastructure.Enums.AirlineFocus),
                        HomeCountry = airport.Profile.Country ?? airline.Profile.Country,
                        IsPaused = true,
                        LocalCurrency = false,
                        MajorAirports = MajorAirports,
                        NumberOfOpponents = Opponents,
                        RandomOpponents = true,
                        RealData = false,
                        Region = _region?.Region ?? Models.General.Countries.Regions.GetRegion("100"),
                        SameRegion = SameRegion,
                        TimeZone = airport.Profile.TimeZone ?? TimeZones.GetTimeZones().First(),
                        UseDayTurns = UseDayTurns,
                        Year = StartYear
                    };

                await GameSession.CreateGameAsync(startData);

                _shell.ShowAirline();
            }
            catch (Exception ex)
            {
                StartupLog.Write("New game: creating the game failed: " + ex);
                Status = "Creating the game failed: " + ex.Message;
            }
            finally
            {
                _isCreating = false;
                OnPropertyChanged(nameof(IsBusy));
                (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private static Airport FindHomeAirport(Airline airline)
        {
            Airport preferred = airline.Profile.PreferedAirport;
            if (preferred != null)
            {
                return preferred;
            }

            Airport own = airline.Airports.FirstOrDefault();
            if (own != null)
            {
                return own;
            }

            return Models.Airports.Airports.GetAllActiveAirports()
                         .Where(a => a.Profile.Country != null && airline.Profile.Country != null
                                     && a.Profile.Country.Uid == airline.Profile.Country.Uid)
                         .OrderByDescending(a => a.Profile.Size)
                         .FirstOrDefault();
        }
    }
}
