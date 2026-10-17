using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TheAirline.Presentation;

namespace TheAirline.Desktop.Views
{
    /// <summary>The week of one aircraft as a horizontal timetable: a row per day, the hours across, and a block per flight. Dashed blocks are the flights about to be added.</summary>
    public class WeekTimelineControl : Control
    {
        private const double Gutter = 44, Header = 22, RowHeight = 34;
        private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        private static readonly string[] Palette = { "#2f6fd0", "#c0602a", "#2e9a5a", "#8a4fc0", "#c0a020", "#2a9aa8", "#b0405a", "#6a7a30" };
        private static readonly Typeface Face = new Typeface("sans-serif");

        private TimetableViewModel _vm;

        public WeekTimelineControl()
        {
            ClipToBounds = true;
            DataContextChanged += (s, e) => Attach();
        }

        private void Attach()
        {
            if (_vm != null)
            {
                _vm.TimelineChanged -= OnChanged;
            }

            _vm = DataContext as TimetableViewModel;
            if (_vm != null)
            {
                _vm.TimelineChanged += OnChanged;
            }

            InvalidateVisual();
        }

        private void OnChanged()
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
        }

        private double X(double minutes)
        {
            return Gutter + (Bounds.Width - Gutter - 6) * minutes / 1440.0;
        }

        private Rect BlockRect(TimelineBlock b)
        {
            double x1 = X(b.Start), x2 = X(b.End);
            return new Rect(x1, Header + b.Day * RowHeight + 4, Math.Max(3, x2 - x1), RowHeight - 8);
        }

        private static FormattedText Text(string s, double size, IBrush brush)
        {
            return new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush);
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#1f1f1f")), null, new Rect(Bounds.Size));
            var grid = new Pen(new SolidColorBrush(Color.Parse("#3c3c3c")), 1);
            var strong = new Pen(new SolidColorBrush(Color.Parse("#5a5a5a")), 1);
            var label = new SolidColorBrush(Color.Parse("#b0b0b0"));

            for (int h = 0; h <= 24; h += 1)
            {
                double x = X(h * 60);
                ctx.DrawLine(h % 6 == 0 ? strong : grid, new Point(x, Header), new Point(x, Header + 7 * RowHeight));
                if (h % 3 == 0 && h < 24)
                {
                    ctx.DrawText(Text(h.ToString("00") + ":00", 10, label), new Point(x + 2, 3));
                }
            }

            for (int d = 0; d < 7; d++)
            {
                double y = Header + d * RowHeight;
                if (d % 2 == 1)
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), null, new Rect(Gutter, y, Bounds.Width - Gutter, RowHeight));
                }

                ctx.DrawLine(grid, new Point(0, y), new Point(Bounds.Width, y));
                ctx.DrawText(Text(DayNames[d], 12, Brushes.White), new Point(6, y + 9));
            }

            ctx.DrawLine(grid, new Point(0, Header + 7 * RowHeight), new Point(Bounds.Width, Header + 7 * RowHeight));

            if (_vm == null)
            {
                return;
            }

            foreach (var b in _vm.Preview.ToList())
            {
                var c = Color.Parse(Palette[b.ColorIndex % Palette.Length]);
                var r = BlockRect(b);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(55, c.R, c.G, c.B)), new Pen(new SolidColorBrush(c), 1.5, new DashStyle(new double[] { 3, 2 }, 0)), r, 4, 4);
            }

            foreach (var b in _vm.Blocks.ToList())
            {
                var c = Color.Parse(Palette[b.ColorIndex % Palette.Length]);
                var r = BlockRect(b);
                bool sel = _vm.SelectedBlock != null && _vm.SelectedBlock.Entry == b.Entry;
                ctx.DrawRectangle(new SolidColorBrush(c), sel ? new Pen(Brushes.White, 2) : new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1), r, 4, 4);
                if (r.Width > 60)
                {
                    using (ctx.PushClip(r))
                    {
                        ctx.DrawText(Text(b.Label, 10.5, Brushes.White), new Point(r.X + 4, r.Y + 5));
                    }
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_vm == null)
            {
                return;
            }

            Point p = e.GetPosition(this);
            var hit = _vm.Blocks.ToList().FirstOrDefault(b => BlockRect(b).Contains(p));
            _vm.SelectedBlock = hit;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, Header + 7 * RowHeight + 2);
        }
    }
}
