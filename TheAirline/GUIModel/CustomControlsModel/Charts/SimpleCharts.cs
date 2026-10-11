using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TheAirline.GUIModel.HelpersModel;

namespace TheAirline.GUIModel.CustomControlsModel.Charts
{
    /// <summary>
    ///     Shared drawing helpers for the small built-in charts (they replace the old MetroChart library).
    ///     Every chart takes its data from ItemsSource, which the pages bind to their DataContext.
    /// </summary>
    internal static class ChartHelper
    {
        public static readonly Color[] Palette =
        {
            Color.FromRgb(0x5B, 0x9B, 0xD5), Color.FromRgb(0xED, 0x7D, 0x31), Color.FromRgb(0xA5, 0xA5, 0xA5),
            Color.FromRgb(0xFF, 0xC0, 0x00), Color.FromRgb(0x70, 0xAD, 0x47), Color.FromRgb(0x9E, 0x6B, 0xC4),
            Color.FromRgb(0x26, 0xA6, 0x9A), Color.FromRgb(0xD9, 0x53, 0x5B)
        };

        public static Brush BrushFor(int index)
        {
            var brush = new SolidColorBrush(Palette[index % Palette.Length]);
            brush.Freeze();
            return brush;
        }

        // reads a list of key/value pairs whatever the number type is
        public static List<KeyValuePair<string, double>> ReadPairs(IEnumerable items)
        {
            var list = new List<KeyValuePair<string, double>>();

            if (items == null)
            {
                return list;
            }

            foreach (object item in items)
            {
                if (item is KeyValuePair<string, int> intPair)
                {
                    list.Add(new KeyValuePair<string, double>(intPair.Key, intPair.Value));
                }
                else if (item is KeyValuePair<string, double> doublePair)
                {
                    list.Add(doublePair);
                }
                else if (item is KeyValuePair<string, long> longPair)
                {
                    list.Add(new KeyValuePair<string, double>(longPair.Key, longPair.Value));
                }
            }

            return list;
        }

        public static string Format(double value)
        {
            double abs = Math.Abs(value);

            if (abs >= 1000000000)
            {
                return (value / 1000000000).ToString("0.#", CultureInfo.CurrentCulture) + "B";
            }
            if (abs >= 1000000)
            {
                return (value / 1000000).ToString("0.#", CultureInfo.CurrentCulture) + "M";
            }
            if (abs >= 10000)
            {
                return (value / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "K";
            }

            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }

        public static FormattedText Text(
            Visual owner,
            string text,
            double size,
            Brush brush,
            bool bold = false,
            double maxWidth = 0)
        {
            var formatted = new FormattedText(
                text ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                size,
                brush,
                VisualTreeHelper.GetDpi(owner).PixelsPerDip);

            if (maxWidth > 0)
            {
                formatted.MaxTextWidth = maxWidth;
                formatted.MaxLineCount = 1;
                formatted.Trimming = TextTrimming.CharacterEllipsis;
            }

            return formatted;
        }

        // rounds the top of an axis to a "nice" number
        public static double NiceStep(double range, int ticks)
        {
            if (range <= 0)
            {
                return 1;
            }

            double rough = range / ticks;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double fraction = rough / magnitude;

            double nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;

            return nice * magnitude;
        }
    }

    /// <summary>
    ///     Base for the charts: a title, a foreground colour and the data.
    /// </summary>
    public abstract class ChartBase : FrameworkElement
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            "ItemsSource",
            typeof (IEnumerable),
            typeof (ChartBase),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ChartTitleProperty = DependencyProperty.Register(
            "ChartTitle",
            typeof (string),
            typeof (ChartBase),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            "Foreground",
            typeof (Brush),
            typeof (ChartBase),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable) GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public string ChartTitle
        {
            get { return (string) GetValue(ChartTitleProperty); }
            set { SetValue(ChartTitleProperty, value); }
        }

        public Brush Foreground
        {
            get { return (Brush) GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        protected virtual Size DefaultSize
        {
            get { return new Size(300, 300); }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? DefaultSize.Width : availableSize.Width;
            double height = double.IsInfinity(availableSize.Height) ? DefaultSize.Height : availableSize.Height;

            return new Size(width, height);
        }

        protected Brush SoftForeground
        {
            get
            {
                var brush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                brush.Freeze();
                return brush;
            }
        }

        // draws the title and returns the height it used
        protected double DrawTitle(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(ChartTitle))
            {
                return 4;
            }

            dc.DrawText(ChartHelper.Text(this, ChartTitle, 16, Foreground, false, ActualWidth - 8), new Point(4, 2));

            return 28;
        }
    }

    /// <summary>
    ///     Grouped columns: ItemsSource is a list of SeriesData, each series becoming one column per key.
    /// </summary>
    public class ColumnChart : ChartBase
    {
        protected override Size DefaultSize
        {
            get { return new Size(400, 300); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;

            if (width < 60 || height < 60)
            {
                return;
            }

            double top = DrawTitle(dc);

            List<SeriesData> series = (ItemsSource ?? new object[0]).OfType<SeriesData>().ToList();
            var data = series.Select(s => ChartHelper.ReadPairs(s.Items)).ToList();

            // categories in the order they first appear
            var categories = new List<string>();
            foreach (var seriesData in data)
            {
                foreach (var pair in seriesData)
                {
                    if (!categories.Contains(pair.Key))
                    {
                        categories.Add(pair.Key);
                    }
                }
            }

            if (series.Count == 0 || categories.Count == 0)
            {
                dc.DrawText(ChartHelper.Text(this, "No data", 13, SoftForeground), new Point(8, top + 6));
                return;
            }

            // legend
            double legendHeight = 20;
            double legendX = 8;
            for (int s = 0; s < series.Count; s++)
            {
                string name = series[s].DisplayName ?? "";
                var label = ChartHelper.Text(this, name, 12, Foreground, false, Math.Max(40, width / series.Count - 30));
                dc.DrawRectangle(ChartHelper.BrushFor(s), null, new Rect(legendX, height - legendHeight + 5, 10, 10));
                dc.DrawText(label, new Point(legendX + 14, height - legendHeight + 2));
                legendX += 14 + label.Width + 14;
            }

            // value range
            double max = 0;
            double min = 0;
            foreach (var seriesData in data)
            {
                foreach (var pair in seriesData)
                {
                    max = Math.Max(max, pair.Value);
                    min = Math.Min(min, pair.Value);
                }
            }
            if (max == 0 && min == 0)
            {
                max = 1;
            }

            double step = ChartHelper.NiceStep(max - min, 5);
            double axisTop = Math.Ceiling(max / step) * step;
            double axisBottom = Math.Floor(min / step) * step;
            if (axisTop == axisBottom)
            {
                axisTop = axisBottom + step;
            }

            double labelAreaHeight = 18;
            double axisLabelWidth = 0;
            for (double v = axisBottom; v <= axisTop + step / 2; v += step)
            {
                axisLabelWidth = Math.Max(axisLabelWidth, ChartHelper.Text(this, ChartHelper.Format(v), 11, SoftForeground).Width);
            }

            double plotLeft = axisLabelWidth + 12;
            double plotRight = width - 8;
            double plotTop = top + 6;
            double plotBottom = height - legendHeight - labelAreaHeight - 4;

            if (plotRight - plotLeft < 30 || plotBottom - plotTop < 30)
            {
                return;
            }

            Func<double, double> y = v => plotBottom - (v - axisBottom) / (axisTop - axisBottom) * (plotBottom - plotTop);

            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), 1);
            gridPen.Freeze();

            for (double v = axisBottom; v <= axisTop + step / 2; v += step)
            {
                double lineY = Math.Round(y(v)) + 0.5;
                dc.DrawLine(gridPen, new Point(plotLeft, lineY), new Point(plotRight, lineY));

                var label = ChartHelper.Text(this, ChartHelper.Format(v), 11, SoftForeground);
                dc.DrawText(label, new Point(plotLeft - 6 - label.Width, lineY - label.Height / 2));
            }

            double categoryWidth = (plotRight - plotLeft) / categories.Count;
            double groupWidth = categoryWidth * 0.75;
            double barWidth = groupWidth / series.Count;
            double zeroY = y(0);

            for (int c = 0; c < categories.Count; c++)
            {
                double groupLeft = plotLeft + c * categoryWidth + (categoryWidth - groupWidth) / 2;

                for (int s = 0; s < series.Count; s++)
                {
                    double value = data[s].Where(p => p.Key == categories[c]).Sum(p => p.Value);
                    double barY = y(value);
                    double barTop = Math.Min(barY, zeroY);
                    double barHeight = Math.Max(1, Math.Abs(zeroY - barY));

                    dc.DrawRectangle(
                        ChartHelper.BrushFor(s),
                        null,
                        new Rect(groupLeft + s * barWidth + 1, barTop, Math.Max(1, barWidth - 2), barHeight));

                    if (barWidth >= 34 && value != 0)
                    {
                        var valueLabel = ChartHelper.Text(this, ChartHelper.Format(value), 10, Foreground, false, barWidth + 8);
                        double labelY = value >= 0 ? barTop - valueLabel.Height - 1 : barTop + barHeight + 1;
                        dc.DrawText(valueLabel, new Point(groupLeft + s * barWidth + (barWidth - valueLabel.Width) / 2, labelY));
                    }
                }

                var categoryLabel = ChartHelper.Text(this, categories[c], 11, Foreground, false, categoryWidth - 2);
                dc.DrawText(
                    categoryLabel,
                    new Point(plotLeft + c * categoryWidth + (categoryWidth - categoryLabel.Width) / 2, plotBottom + 3));
            }
        }
    }

    /// <summary>
    ///     A pie with a legend underneath: ItemsSource is a list of KeyValuePair of name and number.
    /// </summary>
    public class PieChart : ChartBase
    {
        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;

            if (width < 60 || height < 60)
            {
                return;
            }

            double top = DrawTitle(dc);

            List<KeyValuePair<string, double>> items = ChartHelper.ReadPairs(ItemsSource);
            double total = items.Where(i => i.Value > 0).Sum(i => i.Value);

            int rowHeight = 16;
            int maxRows = Math.Max(1, (int) ((height - top - 90) / rowHeight));
            int rows = Math.Min(items.Count, maxRows);
            double legendHeight = rows * rowHeight + (items.Count > rows ? rowHeight : 0);

            double diameter = Math.Min(width - 16, height - top - legendHeight - 12);
            diameter = Math.Max(40, diameter);
            var center = new Point(width / 2, top + 4 + diameter / 2);
            double radius = diameter / 2;

            if (total <= 0)
            {
                var emptyPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 2);
                dc.DrawEllipse(null, emptyPen, center, radius, radius);
            }
            else
            {
                double angle = -90;
                int colorIndex = 0;
                var positives = items.Where(i => i.Value > 0).ToList();

                foreach (var item in positives)
                {
                    double sweep = item.Value / total * 360;

                    if (positives.Count == 1)
                    {
                        dc.DrawEllipse(ChartHelper.BrushFor(colorIndex), null, center, radius, radius);
                    }
                    else
                    {
                        dc.DrawGeometry(ChartHelper.BrushFor(colorIndex), null, Slice(center, radius, angle, sweep));
                    }

                    angle += sweep;
                    colorIndex++;
                }
            }

            // legend
            double legendTop = top + diameter + 10;
            int index = 0;
            foreach (var item in items.Take(rows))
            {
                int colorIndex = items.Where(i => i.Value > 0).ToList().FindIndex(i => i.Key == item.Key);
                Brush swatch = item.Value > 0 && colorIndex >= 0 ? ChartHelper.BrushFor(colorIndex) : Brushes.Gray;

                double rowY = legendTop + index * rowHeight;
                dc.DrawRectangle(swatch, null, new Rect(8, rowY + 3, 9, 9));

                string percent = total > 0 ? string.Format(" ({0:0}%)", item.Value / total * 100) : "";
                dc.DrawText(
                    ChartHelper.Text(this, item.Key + "  " + ChartHelper.Format(item.Value) + percent, 12, Foreground, false, width - 28),
                    new Point(22, rowY));

                index++;
            }

            if (items.Count > rows)
            {
                dc.DrawText(
                    ChartHelper.Text(this, string.Format("... and {0} more", items.Count - rows), 12, SoftForeground),
                    new Point(22, legendTop + rows * rowHeight));
            }
        }

        private static Geometry Slice(Point center, double radius, double startAngle, double sweep)
        {
            double start = startAngle * Math.PI / 180;
            double end = (startAngle + sweep) * Math.PI / 180;

            var startPoint = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
            var endPoint = new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end));

            var figure = new PathFigure { StartPoint = center, IsClosed = true };
            figure.Segments.Add(new LineSegment(startPoint, true));
            figure.Segments.Add(
                new ArcSegment(endPoint, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            return geometry;
        }
    }

    /// <summary>
    ///     A round gauge for one percentage: ItemsSource holds one KeyValuePair of name and a value from 0 to 100.
    /// </summary>
    public class GaugeChart : ChartBase
    {
        protected override Size DefaultSize
        {
            get { return new Size(150, 150); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;

            if (width < 40 || height < 40)
            {
                return;
            }

            var pair = ChartHelper.ReadPairs(ItemsSource).FirstOrDefault();
            double value = Math.Max(0, Math.Min(100, pair.Value));

            double labelHeight = 20;
            double diameter = Math.Min(width - 16, height - labelHeight - 8);
            double thickness = Math.Max(8, diameter * 0.12);
            double radius = diameter / 2 - thickness / 2;
            var center = new Point(width / 2, 4 + diameter / 2);

            var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            // a 270 degree dial that opens at the bottom
            const double startAngle = 135;
            const double totalSweep = 270;

            dc.DrawGeometry(null, trackPen, Arc(center, radius, startAngle, totalSweep));

            if (value > 0)
            {
                Brush fill = value < 25 ? ChartHelper.BrushFor(7) : value < 60 ? ChartHelper.BrushFor(3) : ChartHelper.BrushFor(4);
                var valuePen = new Pen(fill, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                dc.DrawGeometry(null, valuePen, Arc(center, radius, startAngle, Math.Max(1, totalSweep * value / 100)));
            }

            var valueText = ChartHelper.Text(this, string.Format("{0:0}%", pair.Value), Math.Max(12, diameter * 0.2), Foreground, true);
            dc.DrawText(valueText, new Point(center.X - valueText.Width / 2, center.Y - valueText.Height / 2));

            if (!string.IsNullOrEmpty(pair.Key))
            {
                var label = ChartHelper.Text(this, pair.Key, 12, Foreground, false, width - 8);
                dc.DrawText(label, new Point((width - label.Width) / 2, 4 + diameter + 2));
            }
        }

        private static Geometry Arc(Point center, double radius, double startAngle, double sweep)
        {
            double start = startAngle * Math.PI / 180;
            double end = (startAngle + sweep) * Math.PI / 180;

            var startPoint = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
            var endPoint = new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end));

            var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
            figure.Segments.Add(
                new ArcSegment(endPoint, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            return geometry;
        }
    }
}
