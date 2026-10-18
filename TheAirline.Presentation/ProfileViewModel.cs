using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Helpers;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    /// <summary>A logo that comes with the game.</summary>
    public class LogoChoice
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    /// <summary>My Airline > Profile: name, CEO, color, story, route focus and logo.</summary>
    public class ProfileViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _name, _ceo, _color, _narrative, _status, _logoFilter = "", _logoPath, _info;
        private NamedChoice<Route.RouteType> _focus;
        private LogoChoice _selectedLogo;

        public ProfileViewModel(Action changed)
        {
            _changed = changed;
            Logos = new ObservableCollection<LogoChoice>();
            Focuses = new List<NamedChoice<Route.RouteType>>
            {
                new NamedChoice<Route.RouteType> { Name = "Passengers", Value = Route.RouteType.Passenger },
                new NamedChoice<Route.RouteType> { Name = "Cargo", Value = Route.RouteType.Cargo },
                new NamedChoice<Route.RouteType> { Name = "Passengers and cargo", Value = Route.RouteType.Mixed }
            };
            SaveCommand = new RelayCommand(Save);
            UndoCommand = new RelayCommand(Reload);
            UseLogoCommand = new RelayCommand(UseSelectedLogo, () => _selectedLogo != null);
            ResetLogoCommand = new RelayCommand(ResetLogo);
            Reload();
        }

        public ICommand SaveCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand UseLogoCommand { get; }
        public ICommand ResetLogoCommand { get; }

        public ObservableCollection<LogoChoice> Logos { get; }
        public List<NamedChoice<Route.RouteType>> Focuses { get; }
        public List<string> ColorChoices => AirlineProfileService.ColorChoices;

        public string Code { get; private set; }
        public string Info { get => _info; private set => SetProperty(ref _info, value); }

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string CEO { get => _ceo; set => SetProperty(ref _ceo, value); }
        public string Narrative { get => _narrative; set => SetProperty(ref _narrative, value); }
        public NamedChoice<Route.RouteType> Focus { get => _focus; set => SetProperty(ref _focus, value); }

        public string Color
        {
            get => _color;
            set
            {
                if (SetProperty(ref _color, value))
                {
                    OnPropertyChanged(nameof(ColorPreview));
                }
            }
        }

        /// <summary>The color as #RRGGBB for the preview square, or grey when the text is not a color.</summary>
        public string ColorPreview => AirlineProfileService.NormalizeColor(_color) is string c && c.StartsWith("#") ? c : "#808080";

        /// <summary>Picks a color from the swatches.</summary>
        public string PickedColor
        {
            get => null;
            set
            {
                if (value != null) Color = value;
                OnPropertyChanged();
            }
        }

        public string LogoPath { get => _logoPath; private set => SetProperty(ref _logoPath, value); }

        public string LogoFilter
        {
            get => _logoFilter;
            set
            {
                if (SetProperty(ref _logoFilter, value ?? ""))
                {
                    LoadLogos();
                }
            }
        }

        public LogoChoice SelectedLogo
        {
            get => _selectedLogo;
            set
            {
                if (SetProperty(ref _selectedLogo, value))
                {
                    (UseLogoCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string LogoCount { get; private set; }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }

        public void Reload()
        {
            var airline = GameObject.GetInstance()?.HumanAirline;
            if (airline == null) return;
            var p = airline.Profile;
            Code = p.IATACode;
            OnPropertyChanged(nameof(Code));
            Name = p.Name;
            CEO = p.CEO;
            Color = p.Color;
            Narrative = p.Narrative;
            Focus = Focuses.FirstOrDefault(f => f.Value == airline.AirlineRouteFocus) ?? Focuses[0];
            Info = string.Format(
                "Code {0}  |  {1}  |  founded {2}  |  the code cannot be changed, the game uses it to find your airline",
                p.IATACode, p.Country?.Name, p.Founded);
            LogoPath = AirlineProfileService.GetLogo(airline);
            if (Logos.Count == 0) LoadLogos();
        }

        private void LoadLogos()
        {
            var all = AirlineProfileService.BuiltInLogos(_logoFilter);
            Logos.Clear();
            foreach (var n in all.Take(120))
            {
                Logos.Add(new LogoChoice { Name = n, Path = AirlineProfileService.BuiltInLogoPath(n) });
            }
            LogoCount = all.Count > 120
                ? "Showing 120 of " + all.Count + " logos. Type a code (for example DL or AA) to narrow it down."
                : all.Count + " logo(s)";
            OnPropertyChanged(nameof(LogoCount));
        }

        private void Save()
        {
            var airline = GameObject.GetInstance().HumanAirline;
            string message;
            bool ok = AirlineProfileService.Save(airline, _name, _ceo, _color, _narrative, _focus?.Value ?? airline.AirlineRouteFocus, out message);
            Status = message;
            if (ok)
            {
                Reload();
                Status = message;
                _changed?.Invoke();
            }
        }

        private void UseSelectedLogo()
        {
            if (_selectedLogo == null) return;
            string message;
            AirlineProfileService.SetBuiltInLogo(GameObject.GetInstance().HumanAirline, _selectedLogo.Name, out message);
            AfterLogo(message);
        }

        /// <summary>Called by the view with the file the player picked.</summary>
        public void UseLogoFile(string file)
        {
            string message;
            AirlineProfileService.SetCustomLogo(GameObject.GetInstance().HumanAirline, file, out message);
            AfterLogo(message);
        }

        private void ResetLogo()
        {
            AirlineProfileService.ResetLogo(GameObject.GetInstance().HumanAirline);
            AfterLogo("Back to the starting logo.");
        }

        private void AfterLogo(string message)
        {
            LogoPath = AirlineProfileService.GetLogo(GameObject.GetInstance().HumanAirline);
            Status = message;
            _changed?.Invoke();
        }
    }
}
