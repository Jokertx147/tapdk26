using System.Globalization;
using System.Threading;
using System.Windows.Input;

namespace TheAirline.Infrastructure
{
    /// <summary>
    ///     Loads the game's XML data (airports, airlines, airliners ...) once, using the older static setup code.
    ///     It builds WPF images, so it must be called on the UI thread.
    /// </summary>
    public static class GameDataLoader
    {
        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            var previous = Mouse.OverrideCursor;
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                // the data files use US number and date formats
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US", false);
                Setup.SetupGame();
                if (Setup.SetupError != null)
                {
                    throw new System.InvalidOperationException("Loading the game data failed: " + Setup.SetupError.Message,
                        Setup.SetupError);
                }
                _loaded = true;
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                Mouse.OverrideCursor = previous;
            }
        }
    }
}
