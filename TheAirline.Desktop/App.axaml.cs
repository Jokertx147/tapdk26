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
                desktop.Exit += (s, e) => GameSession.StopClock();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
