using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation;
using Prism.Navigation.Regions;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using Region = TheAirline.Models.General.Countries.Region;

namespace TheAirline.ViewModels.Game
{
    public sealed class PageStartDataViewModel : BindableBase, INavigationAware
    {
        private readonly Region _allRegions;
        private readonly IRegionManager _regionManager;
        private readonly IQueryable<Region> _regions;
        private readonly AppState _state;
        private Player _player;
        private Region _selectedRegion;

        public PageStartDataViewModel(AppState state, IRegionManager regionManager)
        {
            _state = state;
            _regionManager = regionManager;

            _allRegions = new Region {Name = "All Regions", Id = 100};

            var continents = from continent in _state.Context.Continents
                orderby continent.Id descending
                select continent;
            Continents = new ObservableCollection<Continent>(continents.ToList());

            _regions = from region in _state.Context.Regions select region;
            Regions = new ObservableCollection<Region>(_regions.ToList());
            Regions.Insert(0, _allRegions);
            SelectedRegion = _allRegions;

            MajorAirports = false;
            RandomOpponents = true;
            RealData = false;
            GameTurn = true;
            PausedOnStart = false;
            SameRegion = true;

            // Use Enumerable.Range to create lists for years and opponents.
            NumOpponents = Enumerable.Range(1, 51).ToList();

            var start = new DateTime(1960, 1, 1);
            var end = DateTime.Now;

            Years = Enumerable.Range(start.Year, (end.Year - start.Year) + 1).OrderByDescending(i => i).ToList();

            var difficulties = from difficulty in _state.Context.Difficulties select difficulty;
            Difficulties = new ObservableCollection<Difficulty>(difficulties.ToList());

            // DelegateCommand only works with Nullable objects hence the int?.
            ChangeRegions = new DelegateCommand<Continent>(UpdateRegions);
            NavigateCommand = new DelegateCommand<Uri>(ChangePages);
        }

        public ObservableCollection<Continent> Continents { get; }
        public ObservableCollection<Difficulty> Difficulties { get; }
        public ObservableCollection<Region> Regions { get; }
        public DelegateCommand<Continent> ChangeRegions { get; }
        public Continent Continent { get; set; }

        public Region SelectedRegion
        {
            get { return _selectedRegion; }
            set { SetProperty(ref _selectedRegion, value); }
        }

        public bool MajorAirports { get; set; }
        public List<int> NumOpponents { get; }
        public int SelectedOpponents { get; set; }
        public List<int> Years { get; }
        public int Year { get; set; }
        public bool RandomOpponents { get; set; }
        public bool RealData { get; set; }
        public bool GameTurn { get; set; }
        public bool PausedOnStart { get; set; }
        public bool SameRegion { get; set; }
        public DelegateCommand<Uri> NavigateCommand { get; }
        public Uri StartMenuUri => new Uri("/PageStartMenu", UriKind.Relative);
        public Uri NewAirlineUri => new Uri("/PageAirlineData", UriKind.Relative);
        public AirlineFocus SelectedFocus { get; set; }
        public Difficulty SelectedDifficulty { get; set; }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            
        }

        // always build a fresh page so the lists and choices are reloaded
        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        private void ChangePages(Uri obj)
        {
            if (obj.Equals(NewAirlineUri))
            {
                _player = new Player
                {
                    Continent = Continent,
                    Region = ReferenceEquals(SelectedRegion, _allRegions) ? null : SelectedRegion, // "All Regions" is not a database row
                    MajorAirports = MajorAirports,
                    StartYear = Year,
                    Focus = SelectedFocus,
                    Difficulty = SelectedDifficulty,
                    NumOfOpponents = SelectedOpponents,
                    RandomOpponents = RandomOpponents,
                    SameRegion = SameRegion,
                    RealData = RealData,
                    UseDays = GameTurn,
                    Paused = PausedOnStart
                };

                _state.Context.Players.Add(_player);
                _state.SaveState();
            }

            var parameters = new NavigationParameters();
            if (obj.Equals(NewAirlineUri) && _player != null)
            {
                parameters.Add("player", _player.Id);
            }

            try
            {
                _regionManager.RequestNavigate("MainContentRegion", obj, result =>
                {
                    if (result.Success != true)
                    {
                        System.Windows.MessageBox.Show("Navigation to " + obj + " failed:\n" + result.Exception,
                            "TheAirline");
                    }
                }, parameters);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show("Navigation to " + obj + " threw:\n" + e, "TheAirline");
            }
        }

        private void UpdateRegions(Continent obj)
        {
            if (obj.Id == 7)
            {
                Regions.Clear();
                Regions.Add(_allRegions);
                foreach (var region in _regions.ToList())
                {
                    Regions.Add(region);
                }
            }
            else
            {
                var continent = from cont in _state.Context.Continents where cont.Id == obj.Id select cont;

                Regions.Clear();
                var regions = continent.First()?.Regions;
                if (regions == null) return;
                Regions.Add(_allRegions);
                foreach (var region in regions)
                {
                    Regions.Add(region);
                }
            }

            SelectedRegion = _allRegions;
        }
    }
}