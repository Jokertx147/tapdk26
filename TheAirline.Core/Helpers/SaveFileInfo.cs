using System;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     A saved game in the list on the load and save screens: the name and when the file was last written.
    /// </summary>
    public class SaveFileInfo
    {
        public SaveFileInfo(string name, DateTime savedAt)
        {
            Name = name;
            SavedAt = savedAt;
        }

        public string Name { get; }

        public DateTime SavedAt { get; }

        public override string ToString()
        {
            return string.Format("{0}    -    saved {1:d} {1:t}", Name, SavedAt);
        }
    }
}
