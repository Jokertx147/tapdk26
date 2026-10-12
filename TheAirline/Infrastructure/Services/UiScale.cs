using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TheAirline.Infrastructure.Services
{
    /// <summary>
    ///     Lets the player zoom the whole game UI (Ctrl and +, Ctrl and -, Ctrl and 0, or Ctrl and the mouse wheel)
    ///     so it is easier to read at high resolutions. The chosen scale is remembered between runs.
    /// </summary>
    public static class UiScale
    {
        private const double Min = 0.75;
        private const double Max = 3.0;
        private const double Step = 0.1;

        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheAirline", "ui-scale.txt");

        private static double _scale = Load();

        public static void Attach(Window window, FrameworkElement root)
        {
            var transform = new ScaleTransform(_scale, _scale);
            root.LayoutTransform = transform;

            void Set(double value)
            {
                _scale = Math.Round(Math.Max(Min, Math.Min(Max, value)), 2);
                transform.ScaleX = _scale;
                transform.ScaleY = _scale;
                Save();
            }

            window.PreviewKeyDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                if (e.Key == Key.OemPlus || e.Key == Key.Add) { Set(_scale + Step); e.Handled = true; }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { Set(_scale - Step); e.Handled = true; }
                else if (e.Key == Key.D0 || e.Key == Key.NumPad0) { Set(1.0); e.Handled = true; }
            };

            window.PreviewMouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                Set(_scale + (e.Delta > 0 ? Step : -Step));
                e.Handled = true;
            };
        }

        private static double Load()
        {
            try
            {
                double value;
                if (File.Exists(SettingsFile) &&
                    double.TryParse(File.ReadAllText(SettingsFile), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return Math.Max(Min, Math.Min(Max, value));
            }
            catch
            {
            }

            return 1.0;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, _scale.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
            }
        }
    }
}
