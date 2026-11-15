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

        // the flight being dragged, where the drag started, and where it would land
        private TimelineBlock _drag;
        private Point _pressed;
        private bool _dragging;
        private int _ghostDay;
        private double _ghostStart;

        public WeekTimelineControl()
        {
            ClipToBounds = true;
            Focusable = true;
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
            return BlockRectAt(b.Day, b.Start, b.End);
        }

        private Rect BlockRectAt(int day, double startMinutes, double endMinutes)
        {
            double x1 = X(startMinutes), x2 = X(endMinutes);
            return new Rect(x1, Header + day * RowHeight + 4, Math.Max(3, x2 - x1), RowHeight - 8);
        }

        private double Minutes(double x)
        {
            double usable = Math.Max(1, Bounds.Width - Gutter - 6);
            return (x - Gutter) * 1440.0 / usable;
        }

        private int DayAt(double y)
        {
            return Math.Max(0, Math.Min(6, (int) ((y - Header) / RowHeight)));
        }

        private static int DayIndexOf(DayOfWeek d)
        {
            return ((int) d + 6) % 7; // Monday first, as the rows are drawn
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
                bool moving = _dragging && _drag != null && _drag.Entry == b.Entry;
                var fill = new SolidColorBrush(c, moving ? 0.35 : 1);
                ctx.DrawRectangle(fill, sel ? new Pen(Brushes.White, 2) : new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1), r, 4, 4);
                if (r.Width > 60)
                {
                    using (ctx.PushClip(r))
                    {
                        ctx.DrawText(Text(b.Label, 10.5, Brushes.White), new Point(r.X + 4, r.Y + 5));
                    }
                }
            }

            // where the dragged flight would land
            if (_dragging && _drag != null)
            {
                var c = Color.Parse(Palette[_drag.ColorIndex % Palette.Length]);
                var pen = new Pen(Brushes.White, 2, new DashStyle(new double[] { 4, 3 }, 0));
                double end = _ghostStart + _drag.DurationMinutes;
                var first = BlockRectAt(_ghostDay, _ghostStart, Math.Min(end, 1440));
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B)), pen, first, 4, 4);
                if (end > 1440)
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B)), pen, BlockRectAt((_ghostDay + 1) % 7, 0, end - 1440), 4, 4);
                }

                using (ctx.PushClip(first))
                {
                    ctx.DrawText(Text(DayNames[_ghostDay] + " " + TimeSpan.FromMinutes(_ghostStart).ToString(@"hh\:mm"), 10.5, Brushes.White), new Point(first.X + 4, first.Y + 5));
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
            Focus();

            _drag = null;
            _dragging = false;
            if (hit?.Entry != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _drag = hit;
                _pressed = p;
                e.Pointer.Capture(this);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_drag?.Entry == null)
            {
                return;
            }

            Point p = e.GetPosition(this);
            // a click shouldn't nudge a flight, so a few pixels have to be travelled first
            if (!_dragging && Math.Abs(p.X - _pressed.X) < 4 && Math.Abs(p.Y - _pressed.Y) < 4)
            {
                return;
            }

            _dragging = true;
            Target(_drag, p, out DayOfWeek day, out TimeSpan time);
            _ghostDay = DayIndexOf(day);
            _ghostStart = time.TotalMinutes;
            InvalidateVisual();
        }

        // where the pointer would put the dragged flight: the whole week moves with the pointer, so dragging a block
        // that ran past midnight works from either half of it
        private void Target(TimelineBlock block, Point p, out DayOfWeek day, out TimeSpan time)
        {
            var entry = block.Entry;
            TimetableViewModel.DragTarget(entry.Day, entry.Time, DayAt(p.Y) - DayAt(_pressed.Y), Minutes(p.X) - Minutes(_pressed.X), out day, out time);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            var drag = _drag;
            bool dragging = _dragging;
            _drag = null;
            _dragging = false;
            e.Pointer.Capture(null);
            if (!dragging || drag?.Entry == null || _vm == null)
            {
                InvalidateVisual();
                return;
            }

            Target(drag, e.GetPosition(this), out DayOfWeek day, out TimeSpan time);
            _vm.MoveEntry(drag.Entry, day, time);
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && _drag != null)
            {
                _drag = null;
                _dragging = false;
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, Header + 7 * RowHeight + 2);
        }
    }
}
