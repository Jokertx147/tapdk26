using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TheAirline.Helpers;
using TheAirline.Models.Airports;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class AirportItem
    {
        public AirportItem(Airport airport, bool own)
        {
            Airport = airport;
            Text = airport.Profile.IATACode + "  " + airport.Profile.Name + ", " + airport.Profile.Country?.Name;
            IsOwn = own;
        }

        public Airport Airport { get; }
        public string Text { get; }
        public bool IsOwn { get; }
    }

    public class AdviceItem
    {
        public AdviceItem(RouteAdvice a)
        {
            Advice = a;
            var f = new ValueCurrencyFormatter();
            Name = a.Destination.Profile.IATACode + "  " + a.Destination.Profile.Name + ", " + a.Destination.Profile.Country?.Name;
            Numbers = string.Format(
                "{0:0} km  |  {1} pax/day  |  {2} competitor(s)  |  ~{3}/day{4}",
                a.Distance, a.DailyDemand, a.Competitors, f.Convert(a.RevenuePerDay),
                a.FleetCanFly ? "" : "  |  no aircraft of yours can fly it");
            Notes = a.Advice;
        }

        public RouteAdvice Advice { get; }
        public string Name { get; }
        public string Numbers { get; }
        public string Notes { get; }
    }

    /// <summary>Airports tab: pick an airport, see the advisor's suggested routes, create one.</summary>
    public class AirportsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _search = "";
        private AirportItem _selected;
        private AdviceItem _selectedAdvice;
        private CreateRouteViewModel _create;
        private string _status;
        private bool _busy;
        private List<Airport> _all;

        public AirportsViewModel(Action changed)
        {
            _changed = changed;
            Airports = new ObservableCollection<AirportItem>();
            Advice = new ObservableCollection<AdviceItem>();
            ApplySearch();
            Selected = Airports.FirstOrDefault();
        }

        public ObservableCollection<AirportItem> Airports { get; }
        public ObservableCollection<AdviceItem> Advice { get; }

        /// <summary>Filters by IATA code, name or country; empty shows the airline's own airports.</summary>
        public string Search
        {
            get { return _search; }
            set
            {
                if (SetProperty(ref _search, value ?? ""))
                {
                    ApplySearch();
                }
            }
        }

        public AirportItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    Create = null;
                    LoadAdvice();
                }
            }
        }

        public AdviceItem SelectedAdvice
        {
            get { return _selectedAdvice; }
            set
            {
                if (SetProperty(ref _selectedAdvice, value) && value != null && _selected != null)
                {
                    Create = new CreateRouteViewModel(_selected.Airport, value.Advice.Destination, OnCreateDone);
                }
            }
        }

        public CreateRouteViewModel Create
        {
            get { return _create; }
            private set
            {
                if (SetProperty(ref _create, value))
                {
                    OnPropertyChanged(nameof(HasCreate));
                }
            }
        }

        public bool HasCreate
        {
            get { return _create != null; }
        }

        public bool IsBusy
        {
            get { return _busy; }
            private set { SetProperty(ref _busy, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private void OnCreateDone(string message)
        {
            Create = null;
            if (message != null)
            {
                Status = message;
                _changed();
            }
            _selectedAdvice = null;
            OnPropertyChanged(nameof(SelectedAdvice));
        }

        private void ApplySearch()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            var own = new HashSet<Airport>(airline.Airports);
            _all = _all ?? Models.Airports.Airports.GetAllAirports().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Airport> list;
            if (_search.Trim().Length == 0)
            {
                list = airline.Airports.OrderBy(a => a.Profile.Name);
            }
            else
            {
                string s = _search.Trim();
                list = _all.Where(
                    a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (a.Profile.Country != null
                             && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            Airports.Clear();
            foreach (var a in list.Take(150))
            {
                Airports.Add(new AirportItem(a, own.Contains(a)));
            }
        }

        /// <summary>Recalculates the suggestions (after the fleet or routes changed).</summary>
        public void ReloadAdvice()
        {
            LoadAdvice(false);
        }

        private int _loadId;

        private async void LoadAdvice(bool announce = true)
        {
            Advice.Clear();
            if (_selected == null)
            {
                return;
            }

            var airport = _selected.Airport;
            int id = ++_loadId;
            IsBusy = true;
            if (announce)
            {
                Status = "Looking for routes from " + airport.Profile.IATACode + "...";
            }

            List<RouteAdvice> advice;
            try
            {
                advice = await Task.Run(() => RouteAdvisor.GetAdvice(airport));
            }
            catch (Exception e)
            {
                IsBusy = false;
                Status = "Could not work out route advice: " + e.Message;
                return;
            }

            if (id != _loadId)
            {
                return; // a newer request replaced this one
            }

            IsBusy = false;
            if (_selected == null || _selected.Airport != airport)
            {
                return; // the player picked another airport meanwhile
            }

            foreach (var a in advice)
            {
                Advice.Add(new AdviceItem(a));
            }

            if (announce || advice.Count == 0)
            {
                Status = advice.Count == 0 ? "No routes to suggest from " + airport.Profile.IATACode + "." : null;
            }
        }
    }
}
