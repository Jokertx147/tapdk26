using System;
using System.Collections.Generic;
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
        private int _dragDays;
        private double _dragMinutes;

        // shift held when the drag began: the flight is copied to where it lands instead of moved there
        private bool _copying;

        // the rubber band drawn across empty space to pick several flights at once
        private bool _banding;
        private Point _bandFrom, _bandTo;
        private bool _bandAdds;

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

            // the gate time after each flight, behind the flights themselves: the aircraft is there but not flying,
            // and nothing else can leave until it has passed
            foreach (var b in _vm.Ground.ToList().Concat(_vm.PreviewGround.ToList()))
            {
                var r = BlockRect(b);
                if (r.Width < 1) continue;
                var tail = new Rect(r.X, r.Y + 5, r.Width, Math.Max(4, r.Height - 10));
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(b.IsPreview ? (byte) 55 : (byte) 95, 190, 196, 205)), null, tail, 2, 2);
            }

            foreach (var b in _vm.Blocks.ToList())
            {
                var c = Color.Parse(Palette[b.ColorIndex % Palette.Length]);
                var r = BlockRect(b);
                bool sel = _vm.IsSelected(b.Entry);
                bool primary = _vm.SelectedBlock != null && _vm.SelectedBlock.Entry == b.Entry;
                bool moving = !_copying && (_dragging && sel && _vm.SelectedCount > 1 || _dragging && _drag != null && _drag.Entry == b.Entry);
                var fill = new SolidColorBrush(c, moving ? 0.35 : 1);
                var edge = primary ? new Pen(Brushes.White, 2.5)
                    : sel ? new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 1.8)
                    : new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1);
                ctx.DrawRectangle(fill, edge, r, 4, 4);
                if (r.Width > 60)
                {
                    using (ctx.PushClip(r))
                    {
                        ctx.DrawText(Text(b.Label, 10.5, Brushes.White), new Point(r.X + 4, r.Y + 5));
                    }
                }
            }

            // where the dragged flights would land: one outline each, the whole group moving together
            if (_dragging && _drag != null)
            {
                var pen = new Pen(Brushes.White, 2, new DashStyle(new double[] { 4, 3 }, 0));
                bool group = !_copying && _vm.SelectedCount > 1 && _vm.IsSelected(_drag.Entry);
                // the same shift the move will apply, so the outline is where the flights really land
                double shift = TimetableViewModel.SnapShift(_dragMinutes);
                var dragged = group
                    ? _vm.Blocks.Where(b => b.Entry != null && _vm.IsSelected(b.Entry) && b.Start == b.Entry.Time.TotalMinutes).ToList()
                    : new List<TimelineBlock> { _drag };

                foreach (var b in dragged)
                {
                    int day = _ghostDay;
                    double start = _ghostStart;
                    if (group)
                    {
                        TimetableViewModel.DragTarget(b.Entry.Day, b.Entry.Time, _dragDays, shift, out DayOfWeek d, out TimeSpan t, false);
                        day = DayIndexOf(d);
                        start = t.TotalMinutes;
                    }

                    var c = Color.Parse(Palette[b.ColorIndex % Palette.Length]);
                    var wash = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
                    double end = start + b.DurationMinutes;
                    var first = BlockRectAt(day, start, Math.Min(end, 1440));
                    ctx.DrawRectangle(wash, pen, first, 4, 4);
                    if (end > 1440)
                    {
                        ctx.DrawRectangle(wash, pen, BlockRectAt((day + 1) % 7, 0, end - 1440), 4, 4);
                    }

                    // where it would still be on the ground afterwards
                    double gate = _vm.GroundMinutes;
                    if (gate > 0)
                    {
                        double gs = end % 1440, ge = gs + gate;
                        int gd = (day + (int) (end/1440)) % 7;
                        var faint = new SolidColorBrush(Color.FromArgb(70, 190, 196, 205));
                        ctx.DrawRectangle(faint, null, BlockRectAt(gd, gs, Math.Min(ge, 1440)), 2, 2);
                        if (ge > 1440) ctx.DrawRectangle(faint, null, BlockRectAt((gd + 1) % 7, 0, ge - 1440), 2, 2);
                    }

                    using (ctx.PushClip(first))
                    {
                        ctx.DrawText(Text((_copying ? "copy to " : "") + DayNames[day] + " " + TimeSpan.FromMinutes(start).ToString(@"hh\:mm"), 10.5, Brushes.White), new Point(first.X + 4, first.Y + 5));
                    }
                }
            }

            // the rubber band
            if (_banding)
            {
                var band = new Rect(_bandFrom, _bandTo);
                if (band.Width > 1 || band.Height > 1)
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                                      new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1, new DashStyle(new double[] { 3, 3 }, 0)), band);
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
            Focus();
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            _drag = null;
            _dragging = false;
            _banding = false;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (hit?.Entry == null)
            {
                // empty space: a rubber band, which adds to what is picked while ctrl is held
                if (!ctrl && !shift) _vm.SelectedBlock = null;
                _banding = true;
                _bandAdds = ctrl || shift;
                _bandFrom = _bandTo = p;
                _pressed = p;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            // Shift does two things, told apart by whether the pointer moves: shift-clicking a flight picks
            // everything between it and the last one picked, while shift-dragging one copies it. So shift on its own
            // decides nothing yet; the range is picked on release if the pointer never moved.
            _copying = shift && !ctrl;
            if (ctrl) _vm.SelectAlso(hit);
            else if (!shift && !_vm.IsSelected(hit.Entry)) _vm.SelectedBlock = hit; // a flight already picked keeps the group

            // dragging starts from any picked flight and takes the whole group with it; a copy takes only the one
            if (_copying || _vm.IsSelected(hit.Entry))
            {
                _drag = hit;
                _pressed = p;
                e.Pointer.Capture(this);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_banding)
            {
                _bandTo = e.GetPosition(this);
                InvalidateVisual();
                return;
            }

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
            _dragDays = DayAt(p.Y) - DayAt(_pressed.Y);
            _dragMinutes = Minutes(p.X) - Minutes(_pressed.X);
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
            bool banding = _banding;
            _drag = null;
            _dragging = false;
            _banding = false;
            e.Pointer.Capture(null);

            if (banding && _vm != null)
            {
                var band = new Rect(_bandFrom, _bandTo);
                // a band of a few pixels is a click on empty space, which picks nothing
                if (band.Width > 3 || band.Height > 3)
                {
                    _vm.SelectBlocks(_vm.Blocks.Where(b => b.Entry != null && BlockRect(b).Intersects(band)).Select(b => b.Entry), _bandAdds);
                }

                InvalidateVisual();
                return;
            }

            bool copying = _copying;
            _copying = false;
            if (drag?.Entry == null || _vm == null)
            {
                InvalidateVisual();
                return;
            }

            if (!dragging)
            {
                // shift and no movement: pick everything between this flight and the last one picked
                if (copying) _vm.SelectRange(drag);
                InvalidateVisual();
                return;
            }

            Point at = e.GetPosition(this);
            if (copying)
            {
                Target(drag, at, out DayOfWeek copyDay, out TimeSpan copyTime);
                _vm.CopyEntry(drag.Entry, copyDay, copyTime);
                InvalidateVisual();
                return;
            }

            if (_vm.SelectedCount > 1 && _vm.IsSelected(drag.Entry))
            {
                _vm.MoveEntries(_vm.SelectedEntries.ToList(), DayAt(at.Y) - DayAt(_pressed.Y), Minutes(at.X) - Minutes(_pressed.X));
            }
            else
            {
                Target(drag, at, out DayOfWeek day, out TimeSpan time);
                _vm.MoveEntry(drag.Entry, day, time);
            }

            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && (_drag != null || _banding))
            {
                _drag = null;
                _dragging = false;
                _banding = false;
                _copying = false;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _vm?.UndoCommand?.CanExecute(null) == true)
            {
                _vm.UndoCommand.Execute(null);
                e.Handled = true;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, Header + 7 * RowHeight + 2);
        }
    }
}
