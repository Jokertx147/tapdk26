using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TheAirline.Helpers;
using TheAirline.Models.General;
using TheAirline.Presentation;

namespace TheAirline.Desktop
{
    /// <summary>
    ///     "--screenshots &lt;folder&gt;": starts a game, gives the player a few flying routes, plays ten weeks, then
    ///     opens the newer screens one by one and saves a picture of each (and of what is further down a screen that
    ///     scrolls), and exits without saving the game. For checking the layout on a real display; progress.txt in the
    ///     folder says how far it got, error.txt what went wrong.
    /// </summary>
    public static class ScreenshotMode
    {
        public static string FolderFrom(string[] args)
        {
            if (args == null) return null;
            int i = Array.IndexOf(args, "--screenshots");
            if (i < 0) return null;
            return i + 1 < args.Length ? args[i + 1] : "screenshots";
        }

        private static string _dir;

        private static void Log(string line)
        {
            try { File.AppendAllText(Path.Combine(_dir, "progress.txt"), DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine); }
            catch (Exception) { }
        }

        private static async Task Wait(Func<bool> until, string what, int seconds = 180)
        {
            DateTime end = DateTime.Now.AddSeconds(seconds);
            while (!until())
            {
                if (DateTime.Now > end) throw new TimeoutException("waited too long for " + what);
                await Task.Delay(100);
            }
        }

        // lets bindings, layout and rendering catch up
        private static async Task Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(150);
            }
        }

        private static void Save(Window window, string name)
        {
            double scale = window.RenderScaling;
            Size size = window.ClientSize;
            var pixels = new PixelSize(Math.Max(1, (int) (size.Width*scale)), Math.Max(1, (int) (size.Height*scale)));
            using (var bitmap = new RenderTargetBitmap(pixels, new Vector(96*scale, 96*scale)))
            {
                bitmap.Render(window);
                bitmap.Save(Path.Combine(_dir, name + ".png"));
            }
            Log("saved " + name + ".png (" + pixels.Width + "x" + pixels.Height + ")");
        }

        // a picture of the screen, and of each further screenful of its tallest scrolling area
        private static async Task Shoot(Window window, string name)
        {
            await Settle();
            Save(window, name);
            ScrollViewer scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
                                          .Where(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height + 40 && s.Viewport.Height > 200)
                                          .OrderByDescending(s => s.Viewport.Width*s.Viewport.Height).FirstOrDefault();
            if (scroller == null) return;
            for (int page = 2; page <= 4; page++)
            {
                double next = scroller.Offset.Y + scroller.Viewport.Height - 60;
                double max = scroller.Extent.Height - scroller.Viewport.Height;
                if (scroller.Offset.Y >= max - 1) break;
                scroller.Offset = new Vector(scroller.Offset.X, Math.Min(next, max));
                await Settle();
                Save(window, name + "-" + page);
            }
            scroller.Offset = new Vector(scroller.Offset.X, 0);
        }

        private static int PageOf(string name) => Array.IndexOf(AirlineViewModel.PageNames, name);

        // an aircraft from the market the player can base somewhere (as the tests buy theirs)
        private static void Buy(AirlineViewModel av, int skip)
        {
            var mk = av.Market;
            foreach (var item in mk.Aircraft.OrderBy(x => x.Airliner.GetPrice()).Skip(skip).Take(200))
            {
                mk.Selected = item;
                if (mk.Homebase != null) break;
            }
            mk.BuyCommand.Execute(null);
            av.Refresh();
        }

        public static async Task Run(ShellViewModel shell, Window window, string dir)
        {
            _dir = Path.GetFullPath(dir);
            try
            {
                Directory.CreateDirectory(_dir);
                foreach (string old in Directory.GetFiles(_dir, "*.png").Concat(Directory.GetFiles(_dir, "*.txt"))) File.Delete(old);
                Log("started; window " + window.ClientSize.Width + "x" + window.ClientSize.Height + " at scale " + window.RenderScaling);
                window.Width = 1440;
                window.Height = 900;
                await Settle();

                // a new game as Delta with eight computer airlines
                ((MainMenuViewModel) shell.Current).NewGameCommand.Execute(null);
                var ng = (NewGameViewModel) shell.Current;
                await Wait(() => !ng.IsBusy, "the new game screen");
                var america = ng.Continents.FirstOrDefault(c => c.Name == "North America");
                if (america != null) ng.SelectedContinent = america;
                await Wait(() => !ng.IsBusy, "the airlines");
                ng.SelectedAirline = ng.Airlines.FirstOrDefault(a => a.Profile.IATACode == "DL") ?? ng.Airlines.First();
                ng.Opponents = 8;
                await Shoot(window, "00-new-game");
                ng.CreateCommand.Execute(null);
                await Wait(() => shell.Current is AirlineViewModel, "the game", 600);
                GameSession.StopClock();
                var av = (AirlineViewModel) shell.Current;
                var me = GameObject.GetInstance().HumanAirline;
                Log("game created: " + me.Profile.Name + ", " + GameObject.GetInstance().GameTime.ToString("yyyy-MM-dd"));

                // three aircraft, each on the best route suggested for it
                foreach (int skip in new[] {40, 90, 140}) Buy(av, skip);
                foreach (var f in me.Fleet.ToList())
                    if (f.Pilots.Count < f.Airliner.Type.CockpitCrew) AirlineHelpers.HireAirlinerPilots(f);
                for (int i = 0; i < 3; i++)
                {
                    await av.Suggestions.Reload();
                    var row = av.Suggestions.Aircraft.FirstOrDefault(a => a.Suggestions.Count > 0);
                    if (row == null) break;
                    bool opened = av.Suggestions.Open(row.Suggestions[0]);
                    Log("route " + row.Suggestions[0].Name + " with " + row.Title + ": " + (opened ? "opened" : av.Suggestions.Status));
                    await Wait(() => !av.Suggestions.IsBusy, "the suggestions");
                }
                foreach (var f in me.Fleet.ToList())
                    if (f.Routes.Count > 0 && f.Status == TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.Stopped)
                        f.Status = TheAirline.Models.Airliners.FleetAirliner.AirlinerStatus.OnRoute;

                // ten weeks of play: two full months and part of a third
                await Task.Run(() =>
                {
                    for (int d = 0; d < 72; d++) GameObjectHelpers.SimulateTurn();
                });
                Log("played to " + GameObject.GetInstance().GameTime.ToString("yyyy-MM-dd") + "; routes " + me.Routes.Count + ", fleet " + me.Fleet.Count);
                // one more aircraft, left idle, for the suggestions screen
                Buy(av, 60);
                av.Refresh();

                // My Airline > Statistics
                av.Page = PageOf("My Airline");
                av.MyAirlineTab = AirlineViewModel.StatisticsTabIndex;
                await Shoot(window, "01-statistics-passengers");
                av.Statistics.Figure = "Operating result";
                await Shoot(window, "02-statistics-result");
                av.Statistics.Figure = "Seats filled";
                await Settle();
                Save(window, "03-statistics-seats-filled");
                av.Statistics.Figure = "Cash";
                av.Statistics.Period = StatisticsViewModel.ByYear;
                await Settle();
                Save(window, "04-statistics-cash-by-year");
                av.Statistics.Period = StatisticsViewModel.ByMonth;
                av.Statistics.Figure = "Passengers";

                // My Airline > Monthly report
                av.MyAirlineTab = AirlineViewModel.ReportTabIndex;
                await Shoot(window, "05-monthly-report");

                // Routes > Suggestions
                av.Page = PageOf("Routes");
                av.RoutesTab = AirlineViewModel.SuggestionsTabIndex;
                await Wait(() => !av.Suggestions.IsBusy, "the suggestions");
                await Shoot(window, "06-route-suggestions");

                // Airlines > Competitors
                av.Page = PageOf("Airlines");
                av.AirlinesTab = 1;
                await Shoot(window, "07-competitors");

                // News
                av.Page = PageOf("News");
                av.News.Reload();
                var report = av.News.Rows.FirstOrDefault(r => r.News.ActionObject is ReportLink) ?? av.News.Rows.FirstOrDefault();
                if (report != null) av.News.Selected = report;
                await Shoot(window, "08-news-report-link");

                // Airliners > New aircraft: a type with a choice of engines, ordered ahead of its first deliveries
                av.Page = PageOf("Airliners");
                av.NewAircraft.Reload();
                var boeing = av.NewAircraft.Manufacturers.FirstOrDefault(m => m.Name == "The Boeing Company");
                if (boeing != null)
                {
                    av.NewAircraft.Manufacturer = boeing;
                    var type = av.NewAircraft.Types.FirstOrDefault(t => t.Name == "Boeing 787-8") ?? av.NewAircraft.Types.FirstOrDefault(t => t.Name.StartsWith("Boeing 777"));
                    if (type != null) av.NewAircraft.Selected = type;
                }
                await Shoot(window, "09-new-aircraft-engines");

                Log("done");
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(_dir, "error.txt"), ex.ToString()); }
                catch (Exception) { }
                Log("failed: " + ex.Message);
            }
            // without the game's save on exit: this was a throwaway game
            Environment.Exit(0);
        }
    }
}
