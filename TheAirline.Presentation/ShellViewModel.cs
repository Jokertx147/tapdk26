using System;
using System.Windows.Input;
using TheAirline.Infrastructure;

namespace TheAirline.Presentation
{
    /// <summary>
    ///     The window's view model: it shows one screen (a view model) at a time and shows messages from the game logic.
    ///     The UI project passes in how to run something on the UI thread and how to close the program.
    /// </summary>
    public class ShellViewModel : ObservableObject
    {
        private readonly Action<Action> _post;
        private readonly Action _exit;
        private object _current;
        private string _message;
        private string _messageTitle;

        public ShellViewModel(Action<Action> postToUiThread, Action exit)
        {
            _post = postToUiThread ?? (a => a());
            _exit = exit ?? (() => { });

            DismissMessageCommand = new RelayCommand(() => Message = null);

            // the game logic reports problems (a failing turn, a missing save) through this hook
            UserInterface.ShowMessage = (title, text) => _post(() => ShowMessage(title, text));

            ShowMainMenu();
        }

        public object Current
        {
            get { return _current; }
            private set { SetProperty(ref _current, value); }
        }

        public string Message
        {
            get { return _message; }
            private set
            {
                if (SetProperty(ref _message, value))
                {
                    OnPropertyChanged(nameof(HasMessage));
                }
            }
        }

        public string MessageTitle
        {
            get { return _messageTitle; }
            private set { SetProperty(ref _messageTitle, value); }
        }

        public bool HasMessage
        {
            get { return !string.IsNullOrEmpty(_message); }
        }

        public ICommand DismissMessageCommand { get; }

        public void ShowMessage(string title, string text)
        {
            MessageTitle = title;
            Message = text;
        }

        public void Navigate(object viewModel)
        {
            var old = Current as IDisposable;
            Current = viewModel;
            old?.Dispose();
        }

        public void Exit()
        {
            _exit();
        }

        public void ShowMainMenu()
        {
            Navigate(new MainMenuViewModel(this, _exit));
        }

        public void ShowNewGame()
        {
            Navigate(new NewGameViewModel(this));
        }

        public void ShowLoadGame()
        {
            Navigate(new LoadGameViewModel(this));
        }

        public void ShowAirline()
        {
            Navigate(new AirlineViewModel(this));
        }
    }
}
