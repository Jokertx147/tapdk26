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
    ///     What earned the most and what lost the most, as bars from a line down the middle: profits to the right in
    ///     green, losses to the left in red, the best at the top and the worst at the bottom. Pointing at a bar names
    ///     it and says why.
    /// </summary>
    public class RankBarChart : Control
    {
        public static readonly StyledProperty<ProfitRankingViewModel> DataProperty =
            AvaloniaProperty.Register<RankBarChart, ProfitRankingViewModel>(nameof(Data));

        static RankBarChart()
        {
            AffectsRender<RankBarChart>(DataProperty);
        }

        public ProfitRankingViewModel Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        private static readonly Typeface Face = new Typeface("sans-serif");
        private static readonly IBrush Back = new SolidColorBrush(Color.Parse("#202020"));
        private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#C8C8C8"));
        private static readonly IBrush Faint = new SolidColorBrush(Color.Parse("#8A8A8A"));
        private static readonly IBrush Earns = new SolidColorBrush(Color.Parse("#4F9D69"));
        private static readonly IBrush Loses = new SolidColorBrush(Color.Parse("#B0524B"));
        private static readonly IBrush EarnsLit = new SolidColorBrush(Color.Parse("#63C086"));
        private static readonly IBrush LosesLit = new SolidColorBrush(Color.Parse("#D4675E"));
        private static readonly Pen Middle = new Pen(new SolidColorBrush(Color.Parse("#707070")), 1);
        private static readonly Pen Divider = new Pen(new SolidColorBrush(Color.Parse("#3C3C3C")), 1, new DashStyle(new double[] { 3, 3 }, 0));

        private const double RowHeight = 26, Gap = 4, NameWidth = 150, AmountWidth = 104, Pad = 8;

        private int _hover = -1;
        private List<RankBar> _rows = new List<RankBar>();
        private int _gapAfter = -1;

        private FormattedText Text(string s, double size = 11.5, IBrush brush = null)
        {
            return new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush ?? TextBrush);
        }

        // the best, then the worst under a dividing line
        private void Gather()
        {
            _rows = new List<RankBar>();
            _gapAfter = -1;
            var d = Data;
            if (d == null) return;
            _rows.AddRange(d.Best);
            if (d.Worst.Count > 0)
            {
                _gapAfter = _rows.Count - 1;
                _rows.AddRange(d.Worst);
            }
        }

        protected override Size MeasureOverride(Size available)
        {
            Gather();
            double rows = Math.Max(1, _rows.Count);
            return new Size(available.Width, rows*(RowHeight + Gap) + 44);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            int i = RowAt(e.GetPosition(this).Y);
            if (i == _hover) return;
            _hover = i;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = -1;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            int i = RowAt(e.GetPosition(this).Y);
            if (i >= 0 && i < _rows.Count && Data != null) Data.Selected = _rows[i];
        }

        private int RowAt(double y)
        {
            double top = 22;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i == _gapAfter + 1 && _gapAfter >= 0) top += Gap*2;
                if (y >= top && y <= top + RowHeight) return i;
                top += RowHeight + Gap;
            }

            return -1;
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            ctx.DrawRectangle(Back, null, new Rect(Bounds.Size));
            Gather();
            var d = Data;
            if (d == null || _rows.Count == 0)
            {
                ctx.DrawText(Text(d?.Summary ?? "Nothing to show yet.", 13), new Point(Pad + 6, 18));
                return;
            }

            // the bars run from a line down the middle, so the longest either way just fits
            double left = Pad + NameWidth;
            double right = Bounds.Width - Pad - AmountWidth;
            double zero = (left + right)/2;
            double half = Math.Max(10, (right - left)/2 - 6);
            double largest = Math.Max(1, d.Largest);

            ctx.DrawText(Text("loses", 10.5, Faint), new Point(left + 2, 4));
            var earnsLabel = Text("earns", 10.5, Faint);
            ctx.DrawText(earnsLabel, new Point(right - earnsLabel.Width - 2, 4));

            double top = 22;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i == _gapAfter + 1 && _gapAfter >= 0)
                {
                    double y = top - Gap;
                    ctx.DrawLine(Divider, new Point(Pad, y), new Point(Bounds.Width - Pad, y));
                    top += Gap*2;
                }

                RankBar bar = _rows[i];
                bool lit = i == _hover || (d.Selected != null && ReferenceEquals(d.Selected, bar));
                double length = Math.Abs(bar.Value)/largest*half;
                var rect = bar.Value < 0
                    ? new Rect(zero - length, top + 3, length, RowHeight - 6)
                    : new Rect(zero, top + 3, length, RowHeight - 6);
                ctx.DrawRectangle(bar.Value < 0 ? (lit ? LosesLit : Loses) : (lit ? EarnsLit : Earns), null, rect, 2, 2);

                var name = Text(bar.Name, 11.5, lit ? Brushes.White : TextBrush);
                using (ctx.PushClip(new Rect(Pad, top, NameWidth - 6, RowHeight)))
                {
                    ctx.DrawText(name, new Point(Pad, top + (RowHeight - name.Height)/2));
                }

                var amount = Text(bar.Amount, 11.5, lit ? Brushes.White : TextBrush);
                ctx.DrawText(amount, new Point(Bounds.Width - Pad - amount.Width, top + (RowHeight - amount.Height)/2));
                top += RowHeight + Gap;
            }

            ctx.DrawLine(Middle, new Point(zero, 18), new Point(zero, top - Gap));

            // what the bar under the pointer is
            if (_hover >= 0 && _hover < _rows.Count)
            {
                var detail = Text(_rows[_hover].Name + "  —  " + _rows[_hover].Detail, 11, Brushes.White);
                double y = top + 2;
                var box = new Rect(Pad, y, Math.Min(Bounds.Width - Pad*2, detail.Width + 16), detail.Height + 8);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(235, 40, 40, 40)),
                                  new Pen(new SolidColorBrush(Color.Parse("#606060")), 1), box, 4, 4);
                using (ctx.PushClip(box))
                {
                    ctx.DrawText(detail, new Point(Pad + 8, y + 4));
                }
            }
        }
    }
}
