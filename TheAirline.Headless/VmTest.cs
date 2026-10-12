using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TheAirline.Helpers.Workers;
using TheAirline.Presentation;

// Drives the view models the way a UI would: menu -> new game -> airline screen -> save -> menu -> load.
static class VmTest
{
    static async Task Wait(Func<bool> done, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done()) { if (sw.Elapsed.TotalSeconds > 120) throw new Exception("timeout: " + what); await Task.Delay(100); }
    }

    public static async Task Run()
    {
        try
        {
            var shell = new ShellViewModel(a => a(), () => Console.WriteLine("exit"));
            var menu = (MainMenuViewModel)shell.Current;
            menu.NewGameCommand.Execute(null);
            var ng = (NewGameViewModel)shell.Current;
            await Wait(() => !ng.IsBusy, "new game screen loads");
            Console.WriteLine("airlines for " + ng.StartYear + ": " + ng.Airlines.Count);
            ng.SelectedAirline = ng.Airlines.First(a => a.Profile.IATACode == "DL") ;
            ng.CreateCommand.Execute(null);
            await Wait(() => shell.Current is AirlineViewModel, "game created");
            var av = (AirlineViewModel)shell.Current;
            Console.WriteLine("AIRLINE SCREEN: " + av.AirlineName + " " + av.Date + " " + av.Money + " fleet=" + av.Fleet.Count + " routes=" + av.Routes.Count + " running=" + av.IsRunning);
            if (!av.IsRunning) av.TogglePauseCommand.Execute(null);
            Thread.Sleep(5000);
            av.Refresh();
            Console.WriteLine("after 5s: " + av.Date + " running=" + av.IsRunning);
            av.TogglePauseCommand.Execute(null);
            av.SaveCommand.Execute(null);
            await Wait(() => av.Status != null && av.Status.StartsWith("Saved"), "save");
            Console.WriteLine(av.Status);
            av.MainMenuCommand.Execute(null);
            ((MainMenuViewModel)shell.Current).LoadGameCommand.Execute(null);
            var lg = (LoadGameViewModel)shell.Current;
            await Wait(() => !lg.IsBusy, "load list");
            Console.WriteLine("saves: " + string.Join(", ", lg.Saves.Select(s => s.Name)));
            lg.Selected = lg.Saves.First();
            lg.LoadCommand.Execute(null);
            await Wait(() => shell.Current is AirlineViewModel, "load");
            var av2 = (AirlineViewModel)shell.Current;
            Console.WriteLine("LOADED: " + av2.Date + " " + av2.Money);
            GameSession.StopClock();
            Console.WriteLine("VM TEST OK");
            Environment.Exit(0);
        }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); Environment.Exit(1); }
    }
}
