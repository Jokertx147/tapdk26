using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TheAirline.Infrastructure;
using TheAirline.Presentation;

namespace TheAirline.Desktop.Views
{
    /// <summary>Draws the Web Mercator world map (the level 3 tiles of the game data) with the airline's airports, routes and aircraft. Drag to pan, wheel to zoom, click an aircraft to select it.</summary>
    public class WorldMapControl : Control
    {
        private const int TileLevel = 3;
        private static Bitmap[,] _tiles;
        private static bool _tilesTried;

        private WorldMapViewModel _vm;
        private double _scale;       // pixels per world width
        private double _cx = 0.5, _cy = 0.4;
        private bool _fitted;
        private Point _down, _last;
        private bool _dragging, _moved;

        private static readonly IBrush Sea = new SolidColorBrush(Color.Parse("#b5d0d0"));
        private static readonly Pen RoutePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 20, 90, 200)), 1.4);
        private static readonly Pen CargoPen = new Pen(new SolidColorBrush(Color.FromArgb(170, 220, 120, 0)), 1.4, new DashStyle(new double[] { 4, 3 }, 0));
        private static readonly IBrush AirportBrush = new SolidColorBrush(Color.Parse("#1d3f8a"));
        private static readonly IBrush HubBrush = new SolidColorBrush(Color.Parse("#c01818"));
        private static readonly IBrush FlyBrush = new SolidColorBrush(Color.Parse("#e8a800"));
        private static readonly IBrush GroundBrush = new SolidColorBrush(Color.Parse("#555555"));
        private static readonly Pen Outline = new Pen(Brushes.Black, 1);
        private static readonly Pen SelectPen = new Pen(Brushes.Red, 2);
        private static readonly Typeface Face = new Typeface("sans-serif");

        public WorldMapControl()
        {
            ClipToBounds = true;
            Focusable = true;
            DataContextChanged += (s, e) => Attach();
        }

        private void Attach()
        {
            if (_vm != null)
            {
                _vm.Changed -= OnChanged;
            }

            _vm = DataContext as WorldMapViewModel;
            if (_vm != null)
            {
                _vm.Changed += OnChanged;
                _vm.Reload();
                _fitted = false;
            }
        }

        private void OnChanged()
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
        }

        private static void LoadTiles()
        {
            if (_tilesTried)
            {
                return;
            }

            _tilesTried = true;
            int n = 1 << TileLevel;
            var tiles = new Bitmap[n, n];
            bool any = false;
            string dir = Path.Combine(AppSettings.GetDataPath(), "graphics", "maps", TileLevel.ToString());
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    string f = Path.Combine(dir, x.ToString(), y + ".png");
                    try
                    {
                        if (File.Exists(f))
                        {
                            tiles[x, y] = new Bitmap(f);
                            any = true;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            _tiles = any ? tiles : null;
        }

        private double MinScale => Math.Max(Bounds.Width, Bounds.Height);
        private double MaxScale => Math.Max(MinScale, 2048 * 3);

        private Point ToScreen(double u, double v)
        {
            return new Point(Bounds.Width / 2 + (u - _cx) * _scale, Bounds.Height / 2 + (v - _cy) * _scale);
        }

        private void Clamp()
        {
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale));
            double halfW = Bounds.Width / 2 / _scale, halfH = Bounds.Height / 2 / _scale;
            _cx = Math.Max(halfW, Math.Min(1 - halfW, _cx));
            _cy = Math.Max(halfH, Math.Min(1 - halfH, _cy));
        }

        private void Fit()
        {
            _fitted = true;
            _scale = MinScale * 1.0;
            if (_vm != null && _vm.Places.Count > 0)
            {
                _cx = _vm.Places.Average(p => p.U);
                _cy = _vm.Places.Average(p => p.V);
                // zoom in on the network when it is compact
                double spanU = _vm.Places.Max(p => p.U) - _vm.Places.Min(p => p.U);
                double spanV = _vm.Places.Max(p => p.V) - _vm.Places.Min(p => p.V);
                double need = Math.Min(Bounds.Width / Math.Max(0.05, spanU * 1.5), Bounds.Height / Math.Max(0.05, spanV * 1.5));
                _scale = Math.Max(_scale, Math.Min(need, MaxScale / 2));
            }

            Clamp();
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            ctx.DrawRectangle(Sea, null, new Rect(Bounds.Size));
            if (Bounds.Width < 10 || Bounds.Height < 10)
            {
                return;
            }

            if (!_fitted)
            {
                Fit();
            }
            else
            {
                Clamp();
            }

            LoadTiles();
            if (_tiles == null)
            {
                var msg = new FormattedText("World map tiles not found in the game data (graphics/maps).", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Face, 13, Brushes.Black);
                ctx.DrawText(msg, new Point(10, 10));
            }
            else
            {
                int n = 1 << TileLevel;
                double tile = _scale / n;
                for (int x = 0; x < n; x++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        Point p = ToScreen(x / (double) n, y / (double) n);
                        if (p.X > Bounds.Width || p.Y > Bounds.Height || p.X + tile < 0 || p.Y + tile < 0 || _tiles[x, y] == null)
                        {
                            continue;
                        }

                        ctx.DrawImage(_tiles[x, y], new Rect(0, 0, 256, 256), new Rect(p.X, p.Y, tile + 0.6, tile + 0.6));
                    }
                }
            }

            if (_vm == null)
            {
                return;
            }

            if (_vm.ShowRoutes)
            {
                foreach (var line in _vm.Lines.ToList())
                {
                    foreach (var piece in line.Pieces)
                    {
                        if (piece.Count < 2) continue;
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(ToScreen(piece[0][0], piece[0][1]), false);
                            for (int i = 1; i < piece.Count; i++) g.LineTo(ToScreen(piece[i][0], piece[i][1]));
                            g.EndFigure(false);
                        }

                        ctx.DrawGeometry(null, line.IsCargo ? CargoPen : RoutePen, geo);
                    }
                }
            }

            if (_vm.ShowAirports)
            {
                foreach (var pl in _vm.Places.ToList())
                {
                    Point p = ToScreen(pl.U, pl.V);
                    if (p.X < -20 || p.Y < -20 || p.X > Bounds.Width + 20 || p.Y > Bounds.Height + 20) continue;
                    double r = pl.IsHub ? 5 : 3.2;
                    ctx.DrawEllipse(pl.IsHub ? HubBrush : AirportBrush, Outline, p, r, r);
                    if (pl.IsHub || _scale > MinScale * 1.8)
                    {
                        var t = new FormattedText(pl.Label ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Brushes.Black);
                        ctx.DrawText(t, new Point(p.X + r + 2, p.Y - 7));
                    }
                }
            }

            if (_vm.ShowAircraft)
            {
                foreach (var a in _vm.Aircraft.ToList())
                {
                    Point p = ToScreen(a.U, a.V);
                    if (p.X < -20 || p.Y < -20 || p.X > Bounds.Width + 20 || p.Y > Bounds.Height + 20) continue;
                    if (a.InFlight)
                    {
                        double rad = a.Heading * Math.PI / 180, s = Math.Sin(rad), c = Math.Cos(rad);
                        Point R(double x, double y) => new Point(p.X + x * c - y * s, p.Y + x * s + y * c);
                        var plane = new StreamGeometry();
                        using (var g = plane.Open())
                        {
                            g.BeginFigure(R(0, -9), true);
                            g.LineTo(R(6, 6));
                            g.LineTo(R(0, 3));
                            g.LineTo(R(-6, 6));
                            g.EndFigure(true);
                        }

                        ctx.DrawGeometry(FlyBrush, Outline, plane);
                    }
                    else
                    {
                        ctx.DrawEllipse(GroundBrush, Outline, p, 4, 4);
                    }

                    if (_vm.Selected == a)
                    {
                        ctx.DrawEllipse(null, SelectPen, p, 12, 12);
                    }
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _moved = false;
            _down = _last = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            Point p = e.GetPosition(this);
            if (Math.Abs(p.X - _down.X) + Math.Abs(p.Y - _down.Y) > 4) _moved = true;
            if (_moved)
            {
                _cx -= (p.X - _last.X) / _scale;
                _cy -= (p.Y - _last.Y) / _scale;
                Clamp();
                InvalidateVisual();
            }

            _last = p;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            if (!_moved && _vm != null)
            {
                Point p = e.GetPosition(this);
                var hit = _vm.Aircraft.ToList()
                             .Select(a => new { a, d = Distance(ToScreen(a.U, a.V), p) })
                             .Where(x => x.d < 14)
                             .OrderBy(x => x.d)
                             .FirstOrDefault();
                _vm.Selected = hit?.a;
            }
        }

        private static double Distance(Point a, Point b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            Point p = e.GetPosition(this);
            double u = _cx + (p.X - Bounds.Width / 2) / _scale, v = _cy + (p.Y - Bounds.Height / 2) / _scale;
            _scale *= e.Delta.Y > 0 ? 1.25 : 0.8;
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale));
            _cx = u - (p.X - Bounds.Width / 2) / _scale;
            _cy = v - (p.Y - Bounds.Height / 2) / _scale;
            Clamp();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
        }
    }
}
