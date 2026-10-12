using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TheAirline.Infrastructure.Services
{
    /// <summary>
    ///     Follows the Windows light/dark setting and publishes the game's background colour
    ///     as the "AppBackgroundBrush" resource (used with DynamicResource).
    /// </summary>
    public static class AppTheme
    {
        private static readonly Color DarkGrey = Color.FromRgb(0x2B, 0x2B, 0x2B);
        private static readonly Color LightGrey = Color.FromRgb(0xE6, 0xE6, 0xE6);

        public static void Initialize(Application app)
        {
            Apply(app);
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                    app.Dispatcher.BeginInvoke(new Action(() => Apply(app)));
            };
        }

        private static void Apply(Application app)
        {
            bool light = IsLightTheme();

            // dark mode keeps the game's original look (white text on translucent black panels)
            app.Resources["AppBackgroundBrush"] = Frozen(light ? LightGrey : DarkGrey);
            app.Resources["AppTextBrush"] = Frozen(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
            app.Resources["PanelRowBrush"] = Frozen(light ? Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x73, 0, 0, 0));
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static bool IsLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme");
                    return value == null || Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
                return true;
            }
        }
    }
}
