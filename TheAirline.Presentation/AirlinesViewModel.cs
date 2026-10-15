using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class AirlineRow
    {
        public AirlineRow(Airline airline)
        {
            Airline = airline;
            Name = airline.Profile.Name;
            Code = airline.Profile.IATACode;
            Country = airline.Profile.Country?.Name ?? "";
            Fleet = airline.Fleet.Count;
            Routes = airline.Routes.Count;
            Airports = airline.Airports.Count;
            Value = airline.GetAirlineValue().ToString();
            ValueRank = (int) airline.GetAirlineValue();
            Alliance = string.Join(", ", airline.Alliances.Select(a => a.Name));
            IsHuman = airline.IsHuman;
            Details = string.Format(
                "{0}  |  founded {1}  |  {2}  |  reputation {3}  |  {4}",
                airline.Profile.Name,
                airline.Profile.Founded,
                airline.Mentality,
                airline.Reputation,
                airline.GetHubs().Count > 0 ? "hubs " + string.Join(", ", airline.GetHubs().Select(h => h.Profile.IATACode)) : "no hubs");
        }

        public Airline Airline { get; }
        public string Name { get; }
        public string Code { get; }
        public string Country { get; }
        public int Fleet { get; }
        public int Routes { get; }
        public int Airports { get; }
        public string Value { get; }
        public int ValueRank { get; }
        public string Alliance { get; }
        public bool IsHuman { get; }
        public string Details { get; }
    }

    /// <summary>All the airlines in the game and how big they are.</summary>
    public class AirlinesViewModel : ObservableObject
    {
        private string _search = "";
        private string _sortBy = "Value";
        private AirlineRow _selected;

        public AirlinesViewModel()
        {
            RefreshCommand = new RelayCommand(Reload);
            Reload();
        }

        public ObservableCollection<AirlineRow> Airlines { get; } = new ObservableCollection<AirlineRow>();
        public List<string> SortChoices { get; } = new List<string> { "Value", "Name", "Country", "Fleet", "Routes", "Airports" };
        public ICommand RefreshCommand { get; }
        public string Count { get; private set; }

        public string Search
        {
            get { return _search; }
            set
            {
                if (SetProperty(ref _search, value ?? ""))
                {
                    Apply();
                }
            }
        }

        public string SortBy
        {
            get { return _sortBy; }
            set
            {
                if (SetProperty(ref _sortBy, value ?? "Value"))
                {
                    Apply();
                }
            }
        }

        public AirlineRow Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(Details));
                }
            }
        }

        public string Details => _selected?.Details ?? "";

        private List<AirlineRow> _all = new List<AirlineRow>();

        public void Reload()
        {
            int year = GameObject.GetInstance().GameTime.Year;
            _all = Models.Airlines.Airlines.GetAirlines(a => a.Profile.Founded <= year && a.Profile.Folded >= year)
                                           .Select(a => new AirlineRow(a))
                                           .ToList();
            Apply();
        }

        private void Apply()
        {
            var keep = _selected?.Airline;
            IEnumerable<AirlineRow> list = _all.Where(
                a => _search.Trim().Length == 0
                     || a.Name.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
                     || a.Code.StartsWith(_search.Trim(), StringComparison.OrdinalIgnoreCase)
                     || a.Country.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

            switch (_sortBy)
            {
                case "Name": list = list.OrderBy(a => a.Name); break;
                case "Country": list = list.OrderBy(a => a.Country).ThenBy(a => a.Name); break;
                case "Fleet": list = list.OrderByDescending(a => a.Fleet).ThenBy(a => a.Name); break;
                case "Routes": list = list.OrderByDescending(a => a.Routes).ThenBy(a => a.Name); break;
                case "Airports": list = list.OrderByDescending(a => a.Airports).ThenBy(a => a.Name); break;
                default: list = list.OrderByDescending(a => a.ValueRank).ThenByDescending(a => a.Fleet).ThenBy(a => a.Name); break;
            }

            Airlines.Clear();
            foreach (var a in list)
            {
                Airlines.Add(a);
            }

            Count = Airlines.Count + " of " + _all.Count + " airlines";
            OnPropertyChanged(nameof(Count));
            _selected = Airlines.FirstOrDefault(a => a.Airline == keep) ?? Airlines.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(Details));
        }
    }
}
