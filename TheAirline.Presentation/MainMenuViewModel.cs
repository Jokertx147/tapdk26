using System;
using System.Windows.Input;

namespace TheAirline.Presentation
{
    public class MainMenuViewModel : ObservableObject
    {
        public MainMenuViewModel(ShellViewModel shell, Action exit)
        {
            NewGameCommand = new RelayCommand(shell.ShowNewGame);
            LoadGameCommand = new RelayCommand(shell.ShowLoadGame);
            ExitCommand = new RelayCommand(exit);
        }

        public string Title => "The Airline Project";

        public ICommand NewGameCommand { get; }

        public ICommand LoadGameCommand { get; }

        public ICommand ExitCommand { get; }
    }
}
