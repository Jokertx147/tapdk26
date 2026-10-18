using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TheAirline.Desktop.Views
{
    /// <summary>Turns a picture file path into an image. The parameter is the width to decode to (keeps memory low for small logos).</summary>
    public class LogoConverter : IValueConverter
    {
        public static readonly LogoConverter Instance = new LogoConverter();

        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            int width = 0;
            if (parameter != null) int.TryParse(parameter.ToString(), out width);
            string key = path + "|" + width + "|" + File.GetLastWriteTimeUtc(path).Ticks;

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }

            try
            {
                Bitmap bitmap;
                using (var stream = File.OpenRead(path))
                {
                    bitmap = width > 0 ? Bitmap.DecodeToWidth(stream, width) : new Bitmap(stream);
                }
                lock (Cache)
                {
                    if (Cache.Count > 600) Cache.Clear();
                    Cache[key] = bitmap;
                }
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
