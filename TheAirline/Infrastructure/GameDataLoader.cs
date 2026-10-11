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
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                Setup.SetupGame();
                _loaded = true;
            }
            finally
            {
                Mouse.OverrideCursor = previous;
            }
        }
    }
}
