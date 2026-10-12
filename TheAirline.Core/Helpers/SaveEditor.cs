using System;
using System.Globalization;
using System.IO;
using TheAirline.Infrastructure;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Edits values inside a saved game file. Loads the save with the game's own serializer, changes it and writes it back,
    ///     so it does not depend on the layout of the save file.
    /// </summary>
    public static class SaveEditor
    {
        public static string GetSavePath(string name)
        {
            return Path.Combine(AppSettings.GetCommonApplicationDataPath(), "saves", name + ".sav");
        }

        /// <summary>
        ///     Sets the money of the player's airline in the named save (name without ".sav"). Returns a message for the user.
        /// </summary>
        public static string SetMoney(string saveName, double amount)
        {
            var path = GetSavePath(saveName);
            if (!File.Exists(path))
            {
                return "Save not found: " + path;
            }

            // some game classes read game data in their static constructors, so the data has to be there before a save is read
            GameDataLoader.EnsureLoaded();

            var save = FileSerializer.Deserialize<SaveObject>(path);
            if (save == null)
            {
                return "Could not read the save: " + path;
            }

            var airline = save.Instance?.HumanAirline ?? save.Instance?.MainAirline;
            if (airline == null)
            {
                return "The save has no player airline.";
            }

            var old = airline.Money;
            airline.Money = amount;

            // keep a backup, and write to a temporary file first so a failed write cannot destroy the save
            File.Copy(path, path + ".bak", true);
            var temp = path + ".tmp";
            FileSerializer.Serialize(temp, save);

            if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                return "Writing the save failed. The original is unchanged.";
            }

            File.Copy(temp, path, true);
            File.Delete(temp);

            return string.Format(CultureInfo.InvariantCulture, "{0}: money changed from {1:N0} to {2:N0}. Backup: {3}.bak",
                airline.Profile?.Name ?? "Player airline", old, amount, path);
        }
    }
}
