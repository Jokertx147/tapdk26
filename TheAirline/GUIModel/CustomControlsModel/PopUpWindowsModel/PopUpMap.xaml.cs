using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Device.Location;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.GUIModel.PagesModel.AirportPageModel;
using TheAirline.Helpers;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.Routes;
using Path = System.Windows.Shapes.Path;

namespace TheAirline.GraphicsModel.UserControlModel.PopUpWindowsModel
{
    /// <summary>
    /// Interaction logic for PopUpMap.xaml
    /// </summary>
    public partial class PopUpMap : PopUpWindow
    {
        private int MapSize;
        private const int ImageSize = 256;
        private int Zoom;
        private List<Airport> AirportsList;
        private List<Route> RoutesList;
        private GeoCoordinate ZoomCoordinates;
        private Boolean ShowingAirports;
        //shows the pop up for an airport
        public static void ShowPopUp(Airport airport)
        {
            PopUpMap window = new PopUpMap(airport);
            window.ShowDialog();
        }
        //shows the pop up for an airliner
        public static void ShowPopUp(FleetAirliner airliner)
        {
            PopUpMap window = new PopUpMap(airliner);
            window.ShowDialog();
        }
        //shows the pop up for some routes
        public static void ShowPopUp(List<Route> routes)
        {
            PopUpMap window = new PopUpMap(routes);
            window.ShowDialog();
        }
        //shows the pop for a list of airports
        public static void ShowPopUp(List<Airport> airports)
        {
            PopUpMap window = new PopUpMap(airports);
            window.ShowDialog();
        }
        public static void ShowPopUp(Route route)
        {
            List<Route> routes = new List<Route>();
            routes.Add(route);

            ShowPopUp(routes);
        }
        public PopUpMap(int mapSize)
        {
            InitializeComponent();
            this.Uid = "1000";

            this.Title = Translator.GetInstance().GetString("PopUpMap", this.Uid);

            this.Width = mapSize;

            this.Height = mapSize;

            this.MapSize = mapSize;

            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        }
        public PopUpMap(FleetAirliner airliner)
            : this(ImageSize)
        {

            showMap(airliner.HasRoute ? airliner.CurrentPosition.Profile.Coordinates.ConvertToGeoCoordinate() : airliner.Homebase.Profile.Coordinates.ConvertToGeoCoordinate(), false);

        }
        public PopUpMap(List<Airport> airports)
            : this(ImageSize * 3)
        {
            this.ShowingAirports = true;
            this.AirportsList = airports;

            PrepareWorldMap();

            foreach (Airport airport in airports)
            {
                showAirport(airport, _world, 3, new Point(0, 0));
            }
        }

        public PopUpMap(List<Route> routes)
            : this(ImageSize * 3)
        {
            this.RoutesList = routes;

            PrepareWorldMap();

            foreach (Route route in routes)
            {
                showRoute(route, _world, 3, new Point(0, 0));
            }
        }

        #region Scrolling and zooming world map

        // the whole world as 8 x 8 tiles; routes and airports are drawn in the same coordinates
        private const int WorldSize = ImageSize * 8;
        private const double MaxScale = 6;
        private Canvas _world;
        private Border _viewport;
        private double _scale = 1;
        private double _offsetX;
        private double _offsetY;
        private bool _viewFitted;
        private bool _dragging;
        private bool _dragMoved;
        private Point _dragStart;
        private Point _dragOrigin;
        private readonly ScaleTransform _worldScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _worldOffset = new TranslateTransform();
        private readonly ScaleTransform _pinCounterScale = new ScaleTransform(1, 1);
        private readonly List<Path> _routeLines = new List<Path>();
        private readonly List<Point> _focusPoints = new List<Point>();

        private double MinScale
        {
            get
            {
                if (_viewport == null || _viewport.ActualHeight < 10)
                {
                    return 0.3;
                }

                return Math.Max(0.1, Math.Min(_viewport.ActualWidth, _viewport.ActualHeight) / WorldSize);
            }
        }

        //builds the map window: the world tiles in a viewport that can be dragged and zoomed with the mouse
        private void PrepareWorldMap()
        {
            this.Width = 1150;
            this.Height = 800;
            this.MinWidth = 600;
            this.MinHeight = 400;
            this.ResizeMode = ResizeMode.CanResize;

            _world = new Canvas { Width = WorldSize, Height = WorldSize };

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    string name = string.Format(@"{0}\{1}\{2}.png", 3, x, y);

                    try
                    {
                        var imgMap = new Image
                        {
                            Width = ImageSize,
                            Height = ImageSize,
                            Source = new BitmapImage(new Uri(AppSettings.GetDataPath() + "\\graphics\\maps\\" + name, UriKind.RelativeOrAbsolute))
                        };
                        RenderOptions.SetBitmapScalingMode(imgMap, BitmapScalingMode.HighQuality);

                        Canvas.SetTop(imgMap, y * ImageSize);
                        Canvas.SetLeft(imgMap, x * ImageSize);

                        _world.Children.Add(imgMap);
                    }
                    catch (Exception)
                    {
                        // a missing tile only leaves a hole in the map
                    }
                }
            }

            var transforms = new TransformGroup();
            transforms.Children.Add(_worldScale);
            transforms.Children.Add(_worldOffset);
            _world.RenderTransform = transforms;
            _world.RenderTransformOrigin = new Point(0, 0);

            // the canvas is bigger than the window, so it sits in a host that clips it
            var host = new Canvas { ClipToBounds = true };
            host.Children.Add(_world);

            _viewport = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x2A, 0x44)),
                ClipToBounds = true,
                Child = host,
                Cursor = Cursors.SizeAll
            };

            _viewport.PreviewMouseWheel += Viewport_MouseWheel;
            _viewport.PreviewMouseLeftButtonDown += Viewport_MouseDown;
            _viewport.PreviewMouseMove += Viewport_MouseMove;
            _viewport.PreviewMouseLeftButtonUp += Viewport_MouseUp;
            _viewport.SizeChanged += (o, e) =>
            {
                if (!_viewFitted && _viewport.ActualWidth > 10 && _viewport.ActualHeight > 10)
                {
                    _viewFitted = true;
                    FitInitialView();
                }
                else
                {
                    ClampView();
                    ApplyView();
                }
            };

            StackPanel sidePanel = createAirportSizeSidePanel();
            sidePanel.Margin = new Thickness(8, 0, 8, 0);
            sidePanel.Width = 180;

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(_viewport, 0);
            Grid.SetColumn(sidePanel, 1);
            layout.Children.Add(_viewport);
            layout.Children.Add(sidePanel);

            this.Content = layout;
            this.KeyDown += PopUpMap_KeyDown;
        }

        //starts with the routes or airports in view, or the whole world when there are none
        private void FitInitialView()
        {
            double vw = _viewport.ActualWidth;
            double vh = _viewport.ActualHeight;

            if (_focusPoints.Count == 0)
            {
                _scale = MinScale;
                _offsetX = (vw - WorldSize * _scale) / 2;
                _offsetY = (vh - WorldSize * _scale) / 2;
            }
            else
            {
                double minX = _focusPoints.Min(pt => pt.X);
                double maxX = _focusPoints.Max(pt => pt.X);
                double minY = _focusPoints.Min(pt => pt.Y);
                double maxY = _focusPoints.Max(pt => pt.Y);

                const double margin = 70;
                double fit = Math.Min(vw / (maxX - minX + 2 * margin), vh / (maxY - minY + 2 * margin));

                _scale = Math.Max(MinScale, Math.Min(MaxScale / 2, fit));
                _offsetX = vw / 2 - (minX + maxX) / 2 * _scale;
                _offsetY = vh / 2 - (minY + maxY) / 2 * _scale;
            }

            ClampView();
            ApplyView();
        }

        //keeps the world covering the window, or centred when it is smaller
        private void ClampView()
        {
            if (_viewport == null)
            {
                return;
            }

            double vw = _viewport.ActualWidth;
            double vh = _viewport.ActualHeight;
            double size = WorldSize * _scale;

            _offsetX = size <= vw ? (vw - size) / 2 : Math.Min(0, Math.Max(vw - size, _offsetX));
            _offsetY = size <= vh ? (vh - size) / 2 : Math.Min(0, Math.Max(vh - size, _offsetY));
        }

        private void ApplyView()
        {
            _worldScale.ScaleX = _scale;
            _worldScale.ScaleY = _scale;
            _worldOffset.X = _offsetX;
            _worldOffset.Y = _offsetY;

            // pins and lines keep their size on screen while the map zooms
            _pinCounterScale.ScaleX = 1 / _scale;
            _pinCounterScale.ScaleY = 1 / _scale;

            double thickness = Math.Max(0.6, 1.4 / _scale);
            foreach (Path line in _routeLines)
            {
                line.StrokeThickness = thickness;
            }
        }

        //zooms in or out around a point in the window
        private void ZoomAt(Point at, double factor)
        {
            double newScale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));

            if (Math.Abs(newScale - _scale) < 0.0001)
            {
                return;
            }

            _offsetX = at.X - (at.X - _offsetX) * newScale / _scale;
            _offsetY = at.Y - (at.Y - _offsetY) * newScale / _scale;
            _scale = newScale;

            ClampView();
            ApplyView();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ZoomAt(e.GetPosition(_viewport), Math.Pow(1.2, e.Delta / 120.0));
            e.Handled = true;
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragMoved = false;
            _dragStart = e.GetPosition(_viewport);
            _dragOrigin = new Point(_offsetX, _offsetY);
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point now = e.GetPosition(_viewport);
            double dx = now.X - _dragStart.X;
            double dy = now.Y - _dragStart.Y;

            // a small movement is still a click on a pin
            if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) > 5)
            {
                _dragMoved = true;
                _viewport.CaptureMouse();
            }

            if (_dragMoved)
            {
                _offsetX = _dragOrigin.X + dx;
                _offsetY = _dragOrigin.Y + dy;

                ClampView();
                ApplyView();
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;

            if (_viewport.IsMouseCaptured)
            {
                _viewport.ReleaseMouseCapture();
            }
        }

        private void PopUpMap_KeyDown(object sender, KeyEventArgs e)
        {
            if (_viewport == null)
            {
                return;
            }

            var center = new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);

            switch (e.Key)
            {
                case Key.OemPlus:
                case Key.Add:
                    ZoomAt(center, 1.25);
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomAt(center, 0.8);
                    break;
                case Key.Left:
                    _offsetX += 80;
                    ClampView();
                    ApplyView();
                    break;
                case Key.Right:
                    _offsetX -= 80;
                    ClampView();
                    ApplyView();
                    break;
                case Key.Up:
                    _offsetY += 80;
                    ClampView();
                    ApplyView();
                    break;
                case Key.Down:
                    _offsetY -= 80;
                    ClampView();
                    ApplyView();
                    break;
            }
        }

        #endregion

        //creates the side panel for the airport size and zooming
        private StackPanel createAirportSizeSidePanel()
        {
            StackPanel sidePanel = new StackPanel();
            sidePanel.Margin = new Thickness(5, 0, 0, 0);

            foreach (GeneralHelpers.Size size in Enum.GetValues(typeof(GeneralHelpers.Size)))
            {
                WrapPanel panelSize = new WrapPanel();
                panelSize.Margin = new Thickness(0, 5, 0, 5);

                Ellipse eSize = new Ellipse();
                eSize.Width = 20;
                eSize.Height = 20;
                eSize.StrokeThickness = 2;
                eSize.Stroke = Brushes.Black;
                eSize.Fill = getSizeColor(size);

                panelSize.Children.Add(eSize);

                TextBlock txtSize = new TextBlock();
                txtSize.Text = new TextUnderscoreConverter().Convert(size).ToString();
                txtSize.Margin = new Thickness(5, 0, 0, 0);

                panelSize.Children.Add(txtSize);

                sidePanel.Children.Add(panelSize);
            }
            StackPanel panelZoom = new StackPanel();
            panelZoom.Margin = new Thickness(0, 10, 0, 0);

            TextBlock txtMapHeader = UIHelpers.CreateTextBlock(Translator.GetInstance().GetString("PopUpMap", "1000"));
            txtMapHeader.FontWeight = FontWeights.Bold;
            txtMapHeader.FontSize = 14;
            txtMapHeader.TextDecorations = TextDecorations.Underline;

            panelZoom.Children.Add(txtMapHeader);

            panelZoom.Children.Add(UIHelpers.CreateTextBlock(Translator.GetInstance().GetString("PopUpMap", "1001")));
            panelZoom.Children.Add(UIHelpers.CreateTextBlock(Translator.GetInstance().GetString("PopUpMap", "1002")));
            panelZoom.Children.Add(UIHelpers.CreateTextBlock(Translator.GetInstance().GetString("PopUpMap", "1003")));
            panelZoom.Children.Add(UIHelpers.CreateTextBlock(Translator.GetInstance().GetString("PopUpMap", "1004")));
            panelZoom.Children.Add(UIHelpers.CreateTextBlock(Translator.GetInstance().GetString("PopUpMap", "1005"))); 

            var mouseHint = UIHelpers.CreateTextBlock("Mouse wheel: zoom. Drag: move the map.");
            mouseHint.TextWrapping = TextWrapping.Wrap;
            mouseHint.Margin = new Thickness(0, 6, 0, 0);
            panelZoom.Children.Add(mouseHint);

            sidePanel.Children.Add(panelZoom);

            //var regions = airports.Select(a => a.Profile.Country.Region).Distinct();//from a in airports select a.Profile.Country.Region;

            WrapPanel panelZoomButtons = new WrapPanel();

            Button btnZoomIn = new Button();
            btnZoomIn.SetResourceReference(StyleProperty, "StandardButtonStyle");
            //btnZoomIn.Height = Double.NaN;
            btnZoomIn.Width = 30;
            btnZoomIn.Content = "+";
            btnZoomIn.Click += btnZoomIn_Click;
          //  btnZoomIn.SetResourceReference(Button.BackgroundProperty, "ButtonBrush");

            panelZoomButtons.Children.Add(btnZoomIn);

            Button btnZoomOut = new Button();
            btnZoomOut.SetResourceReference(StyleProperty, "StandardButtonStyle");
            //btnZoomOut.Height = Double.NaN;
            btnZoomOut.Width = 30;
            btnZoomOut.Content = "-";
            btnZoomOut.Margin = new Thickness(5, 0, 0, 0);
           // btnZoomOut.SetResourceReference(Button.BackgroundProperty, "ButtonBrush");
            btnZoomOut.Click += btnZoomOut_Click;

            panelZoomButtons.Children.Add(btnZoomOut);

            sidePanel.Children.Add(panelZoomButtons);

            return sidePanel;


        }
        private void btnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            zoomMap(false);
        }

        private void btnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            zoomMap(true);
        }
        //zooms the map (the + and - buttons)
        private void zoomMap(Boolean zoomIn)
        {
            if (_viewport == null)
            {
                return;
            }

            ZoomAt(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2), zoomIn ? 1.25 : 0.8);
        }

        //returns the brush for a specific airport size
        private Brush getSizeColor(GeneralHelpers.Size size)
        {
            Color color = Colors.DarkRed;
            switch (size)
            {
                case GeneralHelpers.Size.Large:
                    color= Colors.DarkBlue;
                    break;
                case GeneralHelpers.Size.Largest:
                    color= Colors.DarkRed;
                    break;
                case GeneralHelpers.Size.Medium:
                    color= Colors.Black;
                    break;
                case GeneralHelpers.Size.Small:
                    color = Colors.White;
                    break;
                case GeneralHelpers.Size.Smallest:
                    color= Colors.Gray;
                    break;
                case GeneralHelpers.Size.VeryLarge:
                    color= Colors.Yellow;
                    break;
                case GeneralHelpers.Size.VerySmall:
                    color=Colors.Violet;
                    break;
              
            }

            TypeConverter colorConverter = new ColorConverter();
          
            Color c2 = Color.FromArgb(25, color.R, color.G, color.B);

            LinearGradientBrush colorBrush = new LinearGradientBrush();
            colorBrush.StartPoint = new Point(0, 0);
            colorBrush.EndPoint = new Point(0, 1);
            colorBrush.GradientStops.Add(new GradientStop(c2, 0.15));
            colorBrush.GradientStops.Add(new GradientStop(color, 0.85));
            colorBrush.GradientStops.Add(new GradientStop(c2, 1));

            return colorBrush;
        }
        //shows an airport
        private void showAirport(Airport airport, Panel panelMap, int zoom, Point margin)
        {
            Point pos = UIHelpers.WorldToTilePos(airport.Profile.Coordinates.ConvertToGeoCoordinate(), zoom);

            Point p = new Point(pos.X * ImageSize - margin.X * ImageSize, pos.Y * ImageSize - margin.Y * ImageSize);

            if (p.X < panelMap.Width)
                panelMap.Children.Add(createPin(p, airport));
        }
        //shows a route
        private void showRoute(Route route, Panel panelMap, int zoom, Point margin, Boolean isStopoverRoute=false, Airline airline = null)
        {
            if (route.HasStopovers)
            {
                foreach (Route leg in route.Stopovers.SelectMany(s => s.Legs))
                    showRoute(leg, panelMap, zoom, margin,true,route.Airline);
            }
            else
            {
                Point pos = UIHelpers.WorldToTilePos(route.Destination1.Profile.Coordinates.ConvertToGeoCoordinate(), zoom);

                Point p = new Point(pos.X * ImageSize - margin.X * ImageSize, pos.Y * ImageSize - margin.Y * ImageSize);

                if (p.X < panelMap.Width)
                    panelMap.Children.Add(createPin(p, route.Destination1));

                pos = UIHelpers.WorldToTilePos(route.Destination2.Profile.Coordinates.ConvertToGeoCoordinate(), zoom);

                p = new Point(pos.X * ImageSize - margin.X * ImageSize, pos.Y * ImageSize - margin.Y * ImageSize);

                if (p.X < panelMap.Width)
                    panelMap.Children.Add(createPin(p, route.Destination2));

                if (airline == null)
                    airline = route.Airline;

                createRouteLine(route.Destination1, route.Destination2, panelMap, zoom, margin, airline,isStopoverRoute);
            }
        }
        //creates the line between two airports
        private void createRouteLine(Airport a1, Airport a2,Panel panelMap, int zoom, Point margin, Airline airline, Boolean isStopoverRoute = false)
        {
            DoubleCollection dottedDash = new DoubleCollection();
            dottedDash.Add(2);
            dottedDash.Add(2);

           int d = 50;

           double distance = MathHelpers.GetDistance(a1, a2);

           GeoCoordinate c1 = a1.Profile.Coordinates.ConvertToGeoCoordinate();

          int i = 0;

          Path flightPath = new Path();
          flightPath.Stroke = new AirlineBrushConverter().Convert(airline) as SolidColorBrush;
          flightPath.StrokeThickness = 1;
          flightPath.Fill = new AirlineBrushConverter().Convert(airline) as SolidColorBrush;

          GeometryGroup flightGeometryGroup = new GeometryGroup();


          while (i < distance)
          {
              GeoCoordinate c3 = MathHelpers.GetRoutePoint(c1, a2.Profile.Coordinates.ConvertToGeoCoordinate(), d);

              Point pos1 = UIHelpers.WorldToTilePos(c1, zoom);
              Point pos2 = UIHelpers.WorldToTilePos(c3, zoom);

              /*
              Line line = new Line();

              if (isStopoverRoute)
                  line.StrokeDashArray = dottedDash;
              /*
              line.Stroke = new AirlineBrushConverter().Convert(airline) as SolidColorBrush;
              line.X1 = Math.Min(panelMap.Width, pos1.X * ImageSize - margin.X * ImageSize);
              line.X2 = Math.Min(panelMap.Width, pos2.X * ImageSize - margin.X * ImageSize);
              line.Y1 = pos1.Y * ImageSize - margin.Y * ImageSize;
              line.Y2 = pos2.Y * ImageSize - margin.Y * ImageSize;

              if (Math.Abs(line.X1 - line.X2) > ImageSize)
                  line.X2 = (line.X1 - line.X2) < 0 ? 0 : ImageSize * Math.Pow(2,zoom);
              */
              double x1 = Math.Min(panelMap.Width, pos1.X * ImageSize - margin.X * ImageSize);
              double x2 = Math.Min(panelMap.Width, pos2.X * ImageSize - margin.X * ImageSize);
              double y1 = pos1.Y * ImageSize - margin.Y * ImageSize;
              double y2 = pos2.Y * ImageSize - margin.Y * ImageSize;

              if (Math.Abs(x1 - x2) > ImageSize)
                  x2 = (x1 - x2) < 0 ? 0 : ImageSize * Math.Pow(2, zoom);
           

              LineGeometry flightGeometry = new LineGeometry();
              flightGeometry.StartPoint = new Point(x1, y1);
              flightGeometry.EndPoint = new Point(x2, y2);

              flightGeometryGroup.Children.Add(flightGeometry);
 
             //panelMap.Children.Add(line);

              i += Math.Min(d, (int)(distance - i) + 1);

              c1 = c3;
          }
          flightPath.Data = flightGeometryGroup;
          panelMap.Children.Add(flightPath);
          _routeLines.Add(flightPath);

        }
        public PopUpMap(Airport airport)
            : this(ImageSize)
        {
            showMap(airport.Profile.Coordinates.ConvertToGeoCoordinate(), true);
        }
        //creates the map for coordinates
        private void showMap(GeoCoordinate coordinates, Boolean isAirport)
        {
            this.Zoom = 3;


            Canvas c = new Canvas();
            c.Width = this.Width;
            c.Height = this.Height;


            StringReader stringReader = new StringReader(GeneralHelpers.BigMapXaml);
            XmlReader xmlReader = XmlReader.Create(stringReader);

            Canvas panelMap = (Canvas)XamlReader.Load(xmlReader);


            Point pos = UIHelpers.WorldToTilePos(coordinates, this.Zoom);

            Point p = new Point(pos.X * ImageSize, pos.Y * ImageSize);


            double tMapSize = Math.Sqrt(panelMap.Children.Count) * ImageSize;

            double x = p.X - this.Width / 2;
            double y = p.Y - this.Height / 2;

            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + this.Width > tMapSize) x = tMapSize - this.Width;
            if (y + this.Height > tMapSize) y = tMapSize - this.Height;

            //panelMap.ClipToBounds = true;

            panelMap.Width = this.Width;
            panelMap.Height = this.Height;

            if (isAirport)
                panelMap.Children.Add(createPin(p, Airports.GetAirport(coordinates)));
            else
                panelMap.Children.Add(createPin(p));


            panelMap.Clip = new RectangleGeometry(new Rect(x, y, this.Width, this.Height));
            // panelMap.ClipToBounds = false;

            Canvas.SetLeft(panelMap, -x);
            Canvas.SetTop(panelMap, -y);
            c.Children.Add(panelMap);
            c.ClipToBounds = true;

            this.Content = c;
        }
        //creates the pin at  a position for an airliner
        private Image createPin(Point position)
        {
            Image imgPin = new Image();
            imgPin.Source = new BitmapImage(new Uri(@"/Resources/images/airplanepin.png", UriKind.RelativeOrAbsolute));

            imgPin.Height = 24;
            RenderOptions.SetBitmapScalingMode(imgPin, BitmapScalingMode.HighQuality);

            Canvas.SetTop(imgPin, position.Y - imgPin.Height + 5);
            Canvas.SetLeft(imgPin, position.X - imgPin.Height / 2);

            return imgPin;

        }
        //creates the pin at a position with airport
        private UIElement createPin(Point position, Airport airport)
        {
            Ellipse imgPin = new Ellipse();
            imgPin.Tag = airport;
            imgPin.Fill = getSizeColor(airport.Profile.Size);
            imgPin.Height = 8;
            imgPin.Width = imgPin.Height;
            imgPin.Stroke = Brushes.Black;
            imgPin.StrokeThickness = 1;
            imgPin.MouseLeftButtonUp += new MouseButtonEventHandler(imgPin_MouseDown);
            imgPin.RenderTransform = _pinCounterScale;
            imgPin.RenderTransformOrigin = new Point(0.5, 0.5);
            _focusPoints.Add(position);


            Border brdToolTip = new Border();
            brdToolTip.Margin = new Thickness(-4, 0, -4, -3);
            brdToolTip.Padding = new Thickness(5);
           // brdToolTip.SetResourceReference(Border.BackgroundProperty, "HeaderBackgroundBrush2");
       
            ContentControl lblAirport = new ContentControl();
            lblAirport.SetResourceReference(ContentTemplateProperty, "AirportCountryItem");
            lblAirport.Content = airport;

            brdToolTip.Child = lblAirport;


            imgPin.ToolTip = brdToolTip;

            Canvas.SetTop(imgPin, position.Y - imgPin.Height + 5);
            Canvas.SetLeft(imgPin, position.X - imgPin.Height / 2);

            return imgPin;

        }

          private void imgPin_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_dragMoved)
            {
                Airport airport = (Airport)((Ellipse)sender).Tag;
                PageNavigator.NavigateTo(new PageAirport(airport));

                this.Close();
            }
          

        }
    }
}
