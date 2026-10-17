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

        /// <summary>True while a game is open on the main screen (set by the main screen).</summary>
        public static volatile bool InGame;

        private static string AutoSaveSettingPath()
        {
            return System.IO.Path.Combine(AppSettings.GetCommonApplicationDataPath(), "autosave.txt");
        }

        /// <summary>Save the game automatically when it is left. On by default; remembered in a small file.</summary>
        public static bool AutoSaveOnExit
        {
            get
            {
                try
                {
                    string p = AutoSaveSettingPath();
                    return !System.IO.File.Exists(p) || System.IO.File.ReadAllText(p).Trim() != "off";
                }
                catch (Exception)
                {
                    return true;
                }
            }
            set
            {
                try
                {
                    System.IO.File.WriteAllText(AutoSaveSettingPath(), value ? "on" : "off");
                }
                catch (Exception ex)
                {
                    StartupLog.Write("Could not store the autosave setting: " + ex.Message);
                }
            }
        }

        public static string AutoSaveName()
        {
            var airline = GameObject.GetInstance()?.HumanAirline;
            return "Autosave " + SafeFileName(airline?.Profile?.Name ?? "game");
        }

        /// <summary>Saves under the autosave name if that is switched on and a game is open. Blocks; call when leaving. Returns the name or null.</summary>
        public static string AutoSaveIfNeeded()
        {
            if (!InGame || !AutoSaveOnExit || GameObject.GetInstance()?.HumanAirline == null)
            {
                return null;
            }

            string name = AutoSaveName();
            try
            {
                StopClock();
                SerializedLoadSaveHelpers.SaveGame(name);
                return name;
            }
            catch (Exception ex)
            {
                StartupLog.Write("Autosave failed: " + ex);
                return null;
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
