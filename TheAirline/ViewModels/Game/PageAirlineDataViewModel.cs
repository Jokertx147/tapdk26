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

            Airlines = new ObservableCollection<AirlineModel>();
            Airports = new ObservableCollection<Airport>();
            Countries = new ObservableCollection<Country>();
            TimeZones = new List<GameTimeZone>();

            CreateGameCommand = new DelegateCommand(CreateGame, () => SelectedAirline != null && !IsCreating)
                .ObservesProperty(() => SelectedAirline)
                .ObservesProperty(() => IsCreating);
            BackCommand = new DelegateCommand(() => _regionManager.RequestNavigate("MainContentRegion",
                new Uri("/PageStartData", UriKind.Relative)));
            StartMenuCommand = new DelegateCommand(() => _regionManager.RequestNavigate("MainContentRegion",
                new Uri("/PageStartMenu", UriKind.Relative)));
        }

        public ObservableCollection<AirlineModel> Airlines { get; }
        public ObservableCollection<Airport> Airports { get; }
        public ObservableCollection<Country> Countries { get; }
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

            Airlines.Clear();
            foreach (var airline in airlines)
            {
                Airlines.Add(airline);
            }

            SelectedAirline = Airlines.FirstOrDefault();
        }

        private void AirlineChanged()
        {
            var airline = SelectedAirline;

            Airports.Clear();
            Countries.Clear();

            if (airline == null)
            {
                return;
            }

            RouteFocus = airline.AirlineRouteFocus.ToString();
            Ceo = airline.Profile.CEO;
            Narrative = airline.Profile.Narrative;

            foreach (var country in airline.Profile.Countries)
            {
                Countries.Add(country);
            }
            SelectedCountry = airline.Profile.Country ?? Countries.FirstOrDefault();

            foreach (var airport in Models.Airports.Airports.GetAllActiveAirports()
                .Where(a => airline.Profile.Countries.Contains(a.Profile.Country))
                .OrderBy(a => a.Profile.Name))
            {
                Airports.Add(airport);
            }

            SelectedAirport = Airports.Contains(airline.Profile.PreferedAirport)
                ? airline.Profile.PreferedAirport
                : Airports.FirstOrDefault();
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
                Continent = LegacyContinent ?? Continents.GetContinents().First(),
                Difficulty = DifficultyLevels.GetDifficultyLevel(difficultyName),
                Focus = _player?.Focus ?? default(Infrastructure.Enums.AirlineFocus),
                HomeCountry = SelectedCountry,
                IsPaused = _player != null && _player.Paused,
                LocalCurrency = UseLocalCurrency && SelectedCountry != null && SelectedCountry.HasLocalCurrency,
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

                    PageNavigator.NavigateTo(
                        new Views.Airline.PageAirline(GameObject.GetInstance().HumanAirline));
                    PageNavigator.ClearNavigator();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
