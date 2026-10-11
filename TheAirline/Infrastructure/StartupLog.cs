using System;
using System.IO;

namespace TheAirline.Infrastructure
{
    /// <summary>
    ///     Appends timestamped lines to startup.log in the game's documents folder. Used to see where a hang or crash happened.
    /// </summary>
    public static class StartupLog
    {
        private static readonly object Sync = new object();

        public static string Path => System.IO.Path.Combine(AppSettings.GetCommonApplicationDataPath(), "startup.log");

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " [t" +
                        System.Threading.Thread.CurrentThread.ManagedThreadId + "] " + message + Environment.NewLine);
                }
            }
            catch
            {
                // logging must never break the game
            }
        }
    }
}
