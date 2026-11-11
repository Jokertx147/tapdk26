using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TheAirline.Presentation;

namespace TheAirline.Desktop.Views
{
    /// <summary>
    ///     A line chart of ChartData: a line per airline over the periods, the player's line thicker, gaps where a value
    ///     isn't known. Pointing at the chart shows that period's values.
    /// </summary>
    public class LineChart : Control
    {
        public static readonly StyledProperty<ChartData> DataProperty = AvaloniaProperty.Register<LineChart, ChartData>(nameof(Data));

        static LineChart()
        {
            AffectsRender<LineChart>(DataProperty);
        }

        public ChartData Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        private static readonly Typeface Face = new Typeface("sans-serif");
        private static readonly IBrush Back = new SolidColorBrush(Color.Parse("#202020"));
        private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#B8B8B8"));
        private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.Parse("#383838")), 1);
        private static readonly Pen ZeroPen = new Pen(new SolidColorBrush(Color.Parse("#707070")), 1);
        private static readonly Pen HoverPen = new Pen(new SolidColorBrush(Color.Parse("#808080")), 1, new DashStyle(new double[] {3, 3}, 0));
        private static readonly IBrush TipBack = new SolidColorBrush(Color.FromArgb(235, 40, 40, 40));
        private static readonly Pen TipBorder = new Pen(new SolidColorBrush(Color.Parse("#606060")), 1);

        private int _hover = -1;
        private const double Left = 64, Right = 16, Top = 12, Bottom = 30;

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var d = Data;
            if (d == null || d.Labels.Length == 0) return;
            double x = e.GetPosition(this).X;
            int i = (int) Math.Round((x - Left)/Step(d));
            i = Math.Max(0, Math.Min(d.Labels.Length - 1, i));
            if (i != _hover)
            {
                _hover = i;
                InvalidateVisual();
            }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = -1;
            InvalidateVisual();
        }

        private double Step(ChartData d) => d.Labels.Length > 1 ? (Bounds.Width - Left - Right)/(d.Labels.Length - 1) : 0;

        private FormattedText Text(string s, double size = 11, IBrush brush = null)
        {
            return new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush ?? TextBrush);
        }

        // ---- the log scale ----
        //
        // Plain logs can't show 0 or a loss, and both are ordinary here, so values below one unit keep a straight line
        // through zero and everything above it gains the same height per ten times, mirrored for losses. Four decades
        // of the largest value are shown, which puts a 27,000 passenger airline and a 4,500,000 one on one chart.

        private static double LogUnit(double maxAbs)
        {
            if (maxAbs <= 0) return 1;
            return Math.Max(1, Math.Pow(10, Math.Ceiling(Math.Log10(maxAbs)) - 4));
        }

        private static double Squash(double v, double unit)
        {
            double a = Math.Abs(v)/unit;
            return Math.Sign(v)*(a <= 1 ? a : 1 + Math.Log10(a));
        }

        // the values the axis is labelled with: zero, then one unit and each ten times above it, both ways
        private static List<double> LogTicks(double unit, double lo, double hi)
        {
            var ticks = new List<double> {0};
            for (int side = -1; side <= 1; side += 2)
            {
                for (double v = unit; ; v *= 10)
                {
                    double t = side*v;
                    if (t > hi + 1e-9 || t < lo - 1e-9) break;
                    ticks.Add(t);
                    if (v > 1e15) break;
                }
            }
            return ticks;
        }

        // round steps for the axis: 1, 2 or 5 times a power of ten
        private static double NiceStep(double range, int ticks)
        {
            if (range <= 0) return 1;
            double raw = range/ticks;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw/mag;
            double nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
            return nice*mag;
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            var size = Bounds.Size;
            ctx.DrawRectangle(Back, null, new Rect(size));
            var d = Data;
            if (d == null || d.Labels.Length == 0 || d.Series.Count == 0)
            {
                ctx.DrawText(Text("Nothing to show yet.", 13), new Point(16, 16));
                return;
            }
            double w = size.Width - Left - Right, h = size.Height - Top - Bottom;
            if (w <= 10 || h <= 10) return;

            var values = d.Series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v.Value).ToList();
            double min, max;
            if (d.IsShare)
            {
                min = 0;
                max = 1;
            }
            else
            {
                min = Math.Min(0, values.DefaultIfEmpty(0).Min());
                max = Math.Max(0, values.DefaultIfEmpty(0).Max());
                if (max - min < 1e-9) max = min + 1;
            }
            bool log = d.Log && !d.IsShare;
            double unit = log ? LogUnit(Math.Max(Math.Abs(min), Math.Abs(max))) : 1;
            Func<double, double> scale = v => log ? Squash(v, unit) : v;
            double step = d.IsShare ? 0.2 : NiceStep(max - min, 5);
            double lo, hi;
            List<double> ticks;
            if (log)
            {
                ticks = LogTicks(unit, min, max);
                // a little room above and below the furthest line
                lo = Math.Min(scale(min), 0) - 0.35;
                hi = Math.Max(scale(max), 0) + 0.35;
            }
            else
            {
                lo = Math.Floor(min/step)*step;
                hi = Math.Ceiling(max/step)*step;
                if (hi - lo < 1e-9) hi = lo + step;
                ticks = new List<double>();
                for (double v = lo; v <= hi + step/2; v += step) ticks.Add(v);
            }
            double span = Math.Max(1e-9, hi - lo);
            Func<double, double> Y = v => Top + h - (scale(v) - lo)/span*h;
            double dx = Step(d);
            Func<int, double> X = i => d.Labels.Length > 1 ? Left + i*dx : Left + w/2;

            // grid and axis labels
            foreach (double v in ticks)
            {
                double y = Y(v);
                if (y < Top - 1 || y > Top + h + 1) continue;
                ctx.DrawLine(Math.Abs(v) < 1e-9 ? ZeroPen : GridPen, new Point(Left, y), new Point(Left + w, y));
                var t = Text((d.AxisFormat ?? d.Format)(v), 10);
                ctx.DrawText(t, new Point(Left - 6 - t.Width, y - t.Height/2));
            }
            // the period names, thinned out to fit
            int every = Math.Max(1, (int) Math.Ceiling(d.Labels.Length*60/Math.Max(1, w)));
            for (int i = 0; i < d.Labels.Length; i++)
            {
                if (i % every != 0 && i != d.Labels.Length - 1) continue;
                var t = Text(d.Labels[i], 10);
                double x = Math.Max(Left - 10, Math.Min(Left + w - t.Width + 10, X(i) - t.Width/2));
                ctx.DrawText(t, new Point(x, Top + h + 8));
            }

            // the lines: the player's last, on top
            foreach (ChartSeries s in d.Series.OrderBy(s => s.IsMine))
            {
                var brush = new SolidColorBrush(Color.Parse(s.Color ?? "#888888"));
                var pen = new Pen(brush, s.IsMine ? 3 : 1.8);
                Point? prev = null;
                for (int i = 0; i < s.Values.Length; i++)
                {
                    if (!s.Values[i].HasValue)
                    {
                        prev = null;
                        continue;
                    }
                    var p = new Point(X(i), Y(s.Values[i].Value));
                    if (prev.HasValue) ctx.DrawLine(pen, prev.Value, p);
                    ctx.DrawEllipse(brush, null, p, s.IsMine ? 3.5 : 2.5, s.IsMine ? 3.5 : 2.5);
                    prev = p;
                }
            }

            // the period pointed at: its values
            if (_hover >= 0 && _hover < d.Labels.Length)
            {
                double x = X(_hover);
                ctx.DrawLine(HoverPen, new Point(x, Top), new Point(x, Top + h));
                var lines = new List<Tuple<FormattedText, IBrush>> {Tuple.Create(Text(d.Labels[_hover], 11, Brushes.White), (IBrush) null)};
                foreach (ChartSeries s in d.Series.OrderByDescending(s => s.Values[_hover] ?? double.MinValue))
                {
                    var b = new SolidColorBrush(Color.Parse(s.Color ?? "#888888"));
                    string v = s.Values[_hover].HasValue ? d.Format(s.Values[_hover].Value) : "-";
                    lines.Add(Tuple.Create(Text(s.Name + "  " + v, 11, Brushes.White), (IBrush) b));
                }
                double tw = lines.Max(l => l.Item1.Width) + 26, th = lines.Sum(l => l.Item1.Height + 2) + 10;
                double tx = x + 12 + tw > Left + w ? x - 12 - tw : x + 12;
                var box = new Rect(tx, Top + 6, tw, th);
                ctx.DrawRectangle(TipBack, TipBorder, box, 4, 4);
                double ty = Top + 11;
                foreach (var l in lines)
                {
                    if (l.Item2 != null) ctx.DrawEllipse(l.Item2, null, new Point(tx + 10, ty + l.Item1.Height/2), 4, 4);
                    ctx.DrawText(l.Item1, new Point(tx + (l.Item2 != null ? 20 : 8), ty));
                    ty += l.Item1.Height + 2;
                }
            }
        }
    }
}
