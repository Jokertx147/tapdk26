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
        private bool _dragging, _moved, _leftDrag;
        private string _hoverKey;

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

        private static readonly Pen PathPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 200, 30, 30)), 2, new DashStyle(new double[] { 5, 4 }, 0));

        // about 30 frames a second while aircraft are moving
        private readonly DispatcherTimer _frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        // the route lines in screen points, made again only when the view or the routes change
        private List<StreamGeometry> _routeGeo;
        private readonly Dictionary<StreamGeometry, bool> _routeCargo = new Dictionary<StreamGeometry, bool>();
        private object _routeLines;
        private (double, double, double, double, double) _routeView;

        public WorldMapControl()
        {
            ClipToBounds = true;
            Focusable = true;
            DataContextChanged += (s, e) => Attach();
            _frames.Tick += (s, e) =>
            {
                if (_vm != null && _vm.Animate && _vm.ShowAircraft && _vm.Clock.Running && IsVisible) InvalidateVisual();
            };
            AttachedToVisualTree += (s, e) => _frames.Start();
            DetachedFromVisualTree += (s, e) => _frames.Stop();
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
        private double MaxScale => Math.Max(MinScale, 2048 * 6);

        private Point ToScreen(double u, double v)
        {
            return new Point(Bounds.Width / 2 + (u - _cx) * _scale, Bounds.Height / 2 + (v - _cy) * _scale);
        }

        /// <summary>Zooms around the middle of the view (buttons and keys), or around a point.</summary>
        public void ZoomBy(double factor, Point? around = null)
        {
            Point p = around ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
            double u = _cx + (p.X - Bounds.Width / 2) / _scale, v = _cy + (p.Y - Bounds.Height / 2) / _scale;
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
            _cx = u - (p.X - Bounds.Width / 2) / _scale;
            _cy = v - (p.Y - Bounds.Height / 2) / _scale;
            Clamp();
            InvalidateVisual();
        }

        public void PanBy(double dx, double dy)
        {
            _cx += dx / _scale;
            _cy += dy / _scale;
            Clamp();
            InvalidateVisual();
        }

        /// <summary>Back to the view that fits the airline's network.</summary>
        public void FitView()
        {
            Fit();
            InvalidateVisual();
        }

        /// <summary>Centres the view on the selected aircraft and zooms in a little if the view is wide.</summary>
        public void CenterOnSelected()
        {
            if (_vm?.Selected == null) return;
            _cx = _vm.Selected.ScreenU;
            _cy = _vm.Selected.ScreenV;
            if (_scale < MinScale * 3) _scale = MinScale * 3;
            Clamp();
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double step = 60;
            switch (e.Key)
            {
                case Key.Left: PanBy(-step, 0); break;
                case Key.Right: PanBy(step, 0); break;
                case Key.Up: PanBy(0, -step); break;
                case Key.Down: PanBy(0, step); break;
                case Key.OemPlus: case Key.Add: ZoomBy(1.25); break;
                case Key.OemMinus: case Key.Subtract: ZoomBy(0.8); break;
                case Key.Home: FitView(); break;
                default: return;
            }
            e.Handled = true;
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
                var lines = _vm.Lines;
                var view = (_scale, _cx, _cy, Bounds.Width, Bounds.Height);
                if (_routeGeo == null || !ReferenceEquals(_routeLines, lines) || _routeView != view)
                {
                    _routeGeo = new List<StreamGeometry>();
                    _routeCargo.Clear();
                    _routeLines = lines;
                    _routeView = view;
                    foreach (var line in lines.ToList())
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

                            _routeGeo.Add(geo);
                            _routeCargo[geo] = line.IsCargo;
                        }
                    }
                }

                foreach (var geo in _routeGeo)
                {
                    bool cargo;
                    ctx.DrawGeometry(null, _routeCargo.TryGetValue(geo, out cargo) && cargo ? CargoPen : RoutePen, geo);
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
                bool animate = _vm.Animate;
                DateTime now = animate ? _vm.Clock.Now(WorldMapViewModel.WallSeconds) : _vm.Clock.Latest;
                var aircraft = _vm.Aircraft.ToList();

                // the selected aircraft's whole flight
                var sel = _vm.Selected;
                if (sel?.Path != null && sel.Fraction(now) > 0 && sel.Fraction(now) < 1)
                {
                    foreach (var piece in sel.Path.Pieces)
                    {
                        if (piece.Count < 2) continue;
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(ToScreen(piece[0][0], piece[0][1]), false);
                            for (int i = 1; i < piece.Count; i++) g.LineTo(ToScreen(piece[i][0], piece[i][1]));
                            g.EndFigure(false);
                        }

                        ctx.DrawGeometry(null, PathPen, geo);
                    }
                }

                foreach (var a in aircraft)
                {
                    double u = a.U, v = a.V, heading = a.Heading;
                    bool flying = a.InFlight;
                    if (animate) flying = a.At(now, out u, out v, out heading);

                    Point p = ToScreen(u, v);
                    if (p.X < -40 || p.Y < -40 || p.X > Bounds.Width + 40 || p.Y > Bounds.Height + 40) continue;
                    a.ScreenU = u;
                    a.ScreenV = v;
                    if (flying)
                    {
                        if (animate) DrawTrail(ctx, a, now);
                        double rad = heading * Math.PI / 180, s = Math.Sin(rad), c = Math.Cos(rad);
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

        // a short fading line behind a flying aircraft: a few segments, lighter towards the tail
        private void DrawTrail(DrawingContext ctx, MapAircraft a, DateTime now)
        {
            // about 40 screen points long, whatever the zoom and the flight's length
            double flightPx = 0;
            var ends = new[] { WorldMapViewModel.Project(a.Lat1, a.Lon1), WorldMapViewModel.Project(a.Lat2, a.Lon2) };
            double du = Math.Abs(ends[1][0] - ends[0][0]);
            if (du > 0.5) du = 1 - du;
            flightPx = Math.Sqrt(du * du + Math.Pow(ends[1][1] - ends[0][1], 2)) * _scale;
            if (flightPx < 1) return;
            double back = Math.Min(0.5, 40 / flightPx);
            var points = a.Trail(now, back, 6);
            for (int i = 1; i < points.Count; i++)
            {
                if (Math.Abs(points[i][0] - points[i - 1][0]) > 0.5) continue; // across the edge of the picture
                byte alpha = (byte) (30 + 170 * i / points.Count);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 232, 168, 0)), 1 + 2.0 * i / points.Count);
                ctx.DrawLine(pen, ToScreen(points[i - 1][0], points[i - 1][1]), ToScreen(points[i][0], points[i][1]));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed && !props.IsRightButtonPressed) return;
            Focus();
            _leftDrag = props.IsLeftButtonPressed;
            if (e.ClickCount == 2 && _leftDrag)
            {
                ZoomBy(2, e.GetPosition(this));
                e.Handled = true;
                return;
            }
            _dragging = true;
            _moved = false;
            _down = _last = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging)
            {
                UpdateTip(e.GetPosition(this));
                return;
            }

            if (_hoverKey != null) { _hoverKey = null; ToolTip.SetTip(this, null); }
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
            if (!_moved && _vm != null && _leftDrag)
            {
                Point p = e.GetPosition(this);
                var hit = _vm.Aircraft.ToList()
                             .Select(a => new { a, d = Distance(ToScreen(a.ScreenU, a.ScreenV), p) })
                             .Where(x => x.d < 14)
                             .OrderBy(x => x.d)
                             .FirstOrDefault();
                _vm.Selected = hit?.a;
            }
        }

        /// <summary>Shows the tooltip of the aircraft or airport under the pointer (aircraft first), and a hand cursor over them.</summary>
        private void UpdateTip(Point p)
        {
            string key = null, text = null;
            if (_vm != null)
            {
                if (_vm.ShowAircraft)
                {
                    var plane = _vm.Aircraft.ToList().Select(a => new { a, d = Distance(ToScreen(a.ScreenU, a.ScreenV), p) }).Where(x => x.d < 12).OrderBy(x => x.d).FirstOrDefault();
                    if (plane != null)
                    {
                        key = "a:" + plane.a.Aircraft.Name + plane.a.Info;
                        if (key != _hoverKey) text = _vm.AircraftTip(plane.a);
                    }
                }

                if (key == null && _vm.ShowAirports)
                {
                    var place = _vm.Places.ToList().Select(a => new { a, d = Distance(ToScreen(a.U, a.V), p) }).Where(x => x.d < (x.a.IsHub ? 9 : 7)).OrderBy(x => x.d).FirstOrDefault();
                    if (place != null)
                    {
                        key = "p:" + place.a.Label + _vm.Caption;
                        if (key != _hoverKey) text = _vm.AirportTip(place.a);
                    }
                }
            }

            if (key == _hoverKey) return;
            _hoverKey = key;
            Cursor = key != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            ToolTip.SetShowDelay(this, 150);
            ToolTip.SetTip(this, key == null ? null : text);
        }

        private static double Distance(Point a, Point b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _hoverKey = null;
            ToolTip.SetTip(this, null);
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
