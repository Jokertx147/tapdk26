using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TheAirline.Presentation;

namespace TheAirline.Desktop
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var shell = new ShellViewModel(
                    action => Dispatcher.UIThread.Post(action),
                    () => desktop.Shutdown());
                desktop.MainWindow = new ShellWindow {DataContext = shell};
                bool saved = false;
                desktop.ShutdownRequested += (s, e) =>
                {
                    // closing the window or choosing Exit: save first, then close for real
                    if (saved || !GameSession.InGame || !GameSession.AutoSaveOnExit)
                    {
                        return;
                    }

                    e.Cancel = true;
                    System.Threading.Tasks.Task.Run(() => GameSession.AutoSaveIfNeeded())
                        .ContinueWith(_ => Dispatcher.UIThread.Post(() => { saved = true; desktop.Shutdown(); }));
                };
                desktop.Exit += (s, e) => GameSession.StopClock();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
