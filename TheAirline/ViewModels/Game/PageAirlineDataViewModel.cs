using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation;
using Prism.Navigation.Regions;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.GUIModel.ObjectsModel;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using AirlineModel = TheAirline.Models.Airlines.Airline;
using AirlinesList = TheAirline.Models.Airlines.Airlines;
using Region = TheAirline.Models.General.Countries.Region;

namespace TheAirline.ViewModels.Game
{
    public class PageAirlineDataViewModel : BindableBase, INavigationAware
    {
        private readonly AppState _state;
        private readonly IRegionManager _regionManager;

        private Player _player;
        private AirlineModel _selectedAirline;
        private Airport _selectedAirport;
        private Country _selectedCountry;
        private GameTimeZone _selectedTimeZone;
        private string _ceo;
        private string _narrative;
        private string _routeFocus;
        private bool _useLocalCurrency = true;
        private bool _isCreating;
        private bool _updating;

        public PageAirlineDataViewModel(AppState state, IRegionManager regionManager)
        {
            _state = state;
            _regionManager = regionManager;

            _airlines = new ObservableCollection<AirlineModel>();
            _airports = new ObservableCollection<Airport>();
            _countries = new ObservableCollection<Country>();
            TimeZones = new List<GameTimeZone>();

            CreateGameCommand = new DelegateCommand(CreateGame, () => SelectedAirline != null && SelectedAirport != null && !IsCreating)
                .ObservesProperty(() => SelectedAirline)
                .ObservesProperty(() => SelectedAirport)
                .ObservesProperty(() => IsCreating);
            BackCommand = new DelegateCommand(() => _regionManager.RequestNavigate("MainContentRegion",
                new Uri("/PageStartData", UriKind.Relative)));
            StartMenuCommand = new DelegateCommand(() => _regionManager.RequestNavigate("MainContentRegion",
                new Uri("/PageStartMenu", UriKind.Relative)));
        }

        // the lists are replaced as a whole (not cleared and refilled) so the combo boxes reset cleanly
        private ObservableCollection<AirlineModel> _airlines;
        private ObservableCollection<Airport> _airports;
        private ObservableCollection<Country> _countries;

        public ObservableCollection<AirlineModel> Airlines
        {
            get { return _airlines; }
            private set { SetProperty(ref _airlines, value); }
        }

        public ObservableCollection<Airport> Airports
        {
            get { return _airports; }
            private set { SetProperty(ref _airports, value); }
        }

        public ObservableCollection<Country> Countries
        {
            get { return _countries; }
            private set { SetProperty(ref _countries, value); }
        }
        public List<GameTimeZone> TimeZones { get; private set; }

        public DelegateCommand CreateGameCommand { get; }
        public DelegateCommand BackCommand { get; }
        public DelegateCommand StartMenuCommand { get; }

        public AirlineModel SelectedAirline
        {
            get { return _selectedAirline; }
            set
            {
                if (SetProperty(ref _selectedAirline, value))
                {
                    AirlineChanged();
                }
            }
        }

        public Airport SelectedAirport
        {
            get { return _selectedAirport; }
            set
            {
                if (SetProperty(ref _selectedAirport, value) && value != null && !_updating)
                {
                    SelectedTimeZone = value.Profile.TimeZone;
                }
            }
        }

        public Country SelectedCountry
        {
            get { return _selectedCountry; }
            set { SetProperty(ref _selectedCountry, value); }
        }

        public GameTimeZone SelectedTimeZone
        {
            get { return _selectedTimeZone; }
            set { SetProperty(ref _selectedTimeZone, value); }
        }

        public string Ceo
        {
            get { return _ceo; }
            set { SetProperty(ref _ceo, value); }
        }

        public string Narrative
        {
            get { return _narrative; }
            private set { SetProperty(ref _narrative, value); }
        }

        /// <summary>The kind of airline: passenger, cargo or mixed.</summary>
        public string RouteFocus
        {
            get { return _routeFocus; }
            private set { SetProperty(ref _routeFocus, value); }
        }

        public bool UseLocalCurrency
        {
            get { return _useLocalCurrency; }
            set { SetProperty(ref _useLocalCurrency, value); }
        }

        public bool IsCreating
        {
            get { return _isCreating; }
            private set { SetProperty(ref _isCreating, value); }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var id = navigationContext.Parameters.GetValue<int>("player");
            _player = _state.Context.Players.FirstOrDefault(p => p.Id == id);

            try
            {
                GameDataLoader.EnsureLoaded();
                LoadAirlines();
            }
            catch (Exception e)
            {
                MessageBox.Show("Could not load the game data:\n" + e, "TheAirline");
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        private int StartYear => _player != null && _player.StartYear >= 1900 ? _player.StartYear : DateTime.Now.Year;

        private Continent LegacyContinent
        {
            get
            {
                if (_player?.Continent == null || _player.Continent.Name == "All Continents")
                {
                    return null;
                }
                return Continents.GetContinents().FirstOrDefault(c => c.Name == _player.Continent.Name);
            }
        }

        private Region LegacyRegion
        {
            get
            {
                if (_player?.Region == null || _player.Region.Name == "All Regions")
                {
                    return null;
                }
                return Regions.GetAllRegions().FirstOrDefault(r => r.Name == _player.Region.Name);
            }
        }

        private void LoadAirlines()
        {
            var year = StartYear;
            var continent = LegacyContinent;
            var region = LegacyRegion;

            // airports are only "active" relative to the game clock, so set it to the start year first
            GameObject.GetInstance().GameTime = new DateTime(year, 1, 1);

            TimeZones = Models.General.TimeZones.GetTimeZones();
            RaisePropertyChanged(nameof(TimeZones));

            var airlines = AirlinesList.GetAirlines(a =>
            {
                var airlineRegion = a.Profile.Country?.Region;
                var inArea = region != null
                    ? airlineRegion == region
                    : continent == null || continent.HasRegion(airlineRegion);
                return inArea && a.Profile.Founded <= year && a.Profile.Folded > year;
            }).OrderBy(a => a.Profile.Name);

            Airlines = new ObservableCollection<AirlineModel>(airlines);

            SelectedAirline = null;
            SelectedAirline = Airlines.FirstOrDefault();
        }

        private void AirlineChanged()
        {
            var airline = SelectedAirline;

            if (airline == null)
            {
                Airports = new ObservableCollection<Airport>();
                Countries = new ObservableCollection<Country>();
                return;
            }

            RouteFocus = airline.AirlineRouteFocus.ToString();
            Ceo = airline.Profile.CEO;
            Narrative = airline.Profile.Narrative;

            // clear the old selections first, otherwise the combo boxes keep showing the previous airline's values
            _updating = true;
            try
            {
                SelectedCountry = null;
                SelectedAirport = null;

                var countries = airline.Profile.Countries ?? new List<Country>();
                Countries = new ObservableCollection<Country>(countries);

                Airports = new ObservableCollection<Airport>(Models.Airports.Airports.GetAllActiveAirports()
                    .Where(a => a.Profile.Country != null && countries.Any(c => c.Uid == a.Profile.Country.Uid))
                    .OrderBy(a => a.Profile.Name));

                var homeUid = airline.Profile.Country?.Uid;
                SelectedCountry = Countries.FirstOrDefault(c => c.Uid == homeUid) ?? Countries.FirstOrDefault();

                var preferred = airline.Profile.PreferedAirport;
                SelectedAirport = (preferred == null ? null : Airports.FirstOrDefault(a => a.Profile.IATACode == preferred.Profile.IATACode))
                                  ?? Airports.FirstOrDefault();
            }
            finally
            {
                _updating = false;
            }

            if (SelectedAirport != null)
            {
                SelectedTimeZone = SelectedAirport.Profile.TimeZone;
            }

            RaisePropertyChanged(nameof(SelectedCountry));
            RaisePropertyChanged(nameof(SelectedAirport));
        }

        private void CreateGame()
        {
            var difficultyName = _player?.Difficulty?.Name ?? "Normal";
            var opponents = _player != null && _player.NumOfOpponents > 0 ? _player.NumOfOpponents : 5;

            var startData = new StartDataObject
            {
                Airline = SelectedAirline,
                Airport = SelectedAirport,
                CEO = Ceo,
                // uid "100" is the "all" entry the game setup code checks for
                Continent = LegacyContinent ?? new Continent("100", "All continents"),
                Difficulty = DifficultyLevels.GetDifficultyLevel(difficultyName),
                Focus = _player?.Focus ?? default(Infrastructure.Enums.AirlineFocus),
                HomeCountry = SelectedCountry ?? SelectedAirport?.Profile.Country ?? SelectedAirline.Profile.Country,
                IsPaused = _player != null && _player.Paused,
                LocalCurrency = UseLocalCurrency && (SelectedCountry ?? SelectedAirport?.Profile.Country ?? SelectedAirline.Profile.Country) != null && (SelectedCountry ?? SelectedAirport?.Profile.Country ?? SelectedAirline.Profile.Country).HasLocalCurrency,
                MajorAirports = _player != null && _player.MajorAirports,
                NumberOfOpponents = opponents,
                RandomOpponents = _player == null || _player.RandomOpponents,
                RealData = _player != null && _player.RealData,
                Region = LegacyRegion ?? Regions.GetRegion("100"),
                SameRegion = _player == null || _player.SameRegion,
                TimeZone = SelectedTimeZone,
                UseDayTurns = _player != null && _player.UseDays,
                Year = StartYear
            };

            if (!startData.RandomOpponents)
            {
                PageNavigator.NavigateTo(new GUIModel.PagesModel.GamePageModel.PageSelectOpponents(startData));
                return;
            }

            IsCreating = true;
            Task.Run(() => GameObjectHelpers.CreateGame(startData))
                .ContinueWith(task =>
                {
                    IsCreating = false;

                    if (task.IsFaulted)
                    {
                        MessageBox.Show("Creating the game failed:\n" + task.Exception?.GetBaseException(), "TheAirline");
                        return;
                    }

                    StartupLog.Write("Game created, opening the airline page");
                    PageNavigator.NavigateTo(
                        new Views.Airline.PageAirline(GameObject.GetInstance().HumanAirline));
                    PageNavigator.ClearNavigator();
                    StartupLog.Write("Airline page opened");
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
