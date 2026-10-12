using System;
using System.Linq;
using System.Threading.Tasks;
using TheAirline.Helpers;
using TheAirline.Helpers.Workers;
using TheAirline.Infrastructure;
using TheAirline.Models.General;
using TheAirline.Models.General.Holidays;

namespace TheAirline.Presentation
{
    /// <summary>
    ///     What every screen needs from the running game: loading the data, starting, loading and saving a game.
    ///     All the real work is in TheAirline.Core; this only sequences it.
    /// </summary>
    public static class GameSession
    {
        public static Task EnsureDataLoadedAsync()
        {
            return Task.Run(() => GameDataLoader.EnsureLoaded());
        }

        /// <summary>Creates a new game and returns when its route demand exists, so the clock can run safely.</summary>
        public static async Task CreateGameAsync(StartDataObject startData)
        {
            await EnsureDataLoadedAsync();

            await Task.Run(() => GameObjectHelpers.CreateGame(startData));
            await GameObjectHelpers.DemandCreation;

            StartupLog.Write("GameSession: new game ready");
        }

        public static async Task LoadGameAsync(string name)
        {
            await EnsureDataLoadedAsync();

            await Task.Run(
                () =>
                    {
                        StopClock();
                        SerializedLoadSaveHelpers.LoadGame(name);
                    });

            HolidayYear.Clear();
            GeneralHelpers.CreateHolidays(GameObject.GetInstance().GameTime.Year);
            Setup.SetupMergers();

            // a loaded game waits for the player to start the clock
            GameObjectWorker.GetInstance().Pause();
        }

        /// <summary>Saves under the game's name; the clock is stopped while the file is written and restarted after.</summary>
        public static async Task SaveGameAsync(string name)
        {
            bool wasRunning = !GameObjectWorker.GetInstance().IsPaused;

            await Task.Run(
                () =>
                    {
                        StopClock();
                        SerializedLoadSaveHelpers.SaveGame(name);
                    });

            if (wasRunning)
            {
                GameObjectWorker.GetInstance().Restart();
            }
        }

        /// <summary>Stops the clock and waits until the running turn has finished. Call from a background thread.</summary>
        public static void StopClock()
        {
            var worker = GameObjectWorker.GetInstance();
            worker.RequestCancel();
            worker.IsPaused = true;

            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (worker.IsBusy() && waited.Elapsed < TimeSpan.FromSeconds(30))
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        public static string SafeFileName(string name)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            name = name.Trim();

            return name.Length == 0 ? "game" : name;
        }
    }
}
