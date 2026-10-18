using System;
using System.Collections.Generic;
using System.Linq;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Statistics;
using TheAirline.Models.Routes;

namespace TheAirline.Presentation
{
    /// <summary>A place on the map. U and V run from 0 to 1 across the (Web Mercator) world image.</summary>
    public class MapPlace
    {
        public double U { get; set; }
        public double V { get; set; }
        public string Label { get; set; }
        public bool IsHub { get; set; }
        public Airport Airport { get; set; }
    }

    /// <summary>A line on the map, split in pieces where it crosses the edge of the picture.</summary>
    public class MapLine
    {
        public List<List<double[]>> Pieces { get; set; } = new List<List<double[]>>();
        public bool IsCargo { get; set; }
    }

    public class MapAircraft
    {
        public FleetAirliner Aircraft { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        /// <summary>Direction of travel in degrees on the picture, 0 = up (north), clockwise.</summary>
        public double Heading { get; set; }
        public bool InFlight { get; set; }
        public string Name { get; set; }
        public string Info { get; set; }
    }

    /// <summary>A world map with the airports, routes and aircraft of the airline.</summary>
    public class WorldMapViewModel : ObservableObject
    {
        private bool _showRoutes = true, _showAirports = true, _showAircraft = true;
        private MapAircraft _selected;
        private string _caption = "";

        public List<MapPlace> Places { get; private set; } = new List<MapPlace>();
        public List<MapLine> Lines { get; private set; } = new List<MapLine>();
        public List<MapAircraft> Aircraft { get; private set; } = new List<MapAircraft>();

        /// <summary>Raised after Reload so the view can redraw.</summary>
        public event Action Changed;

        public bool ShowRoutes
        {
            get { return _showRoutes; }
            set { if (SetProperty(ref _showRoutes, value)) Changed?.Invoke(); }
        }

        public bool ShowAirports
        {
            get { return _showAirports; }
            set { if (SetProperty(ref _showAirports, value)) Changed?.Invoke(); }
        }

        public bool ShowAircraft
        {
            get { return _showAircraft; }
            set { if (SetProperty(ref _showAircraft, value)) Changed?.Invoke(); }
        }

        public MapAircraft Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(SelectedText));
                    Changed?.Invoke();
                }
            }
        }

        public string SelectedText => _selected == null ? "Click an aircraft on the map for details." : _selected.Name + "  |  " + _selected.Info;

        public string Caption
        {
            get { return _caption; }
            private set { SetProperty(ref _caption, value); }
        }

        private static string Num(double v)
        {
            return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The text of the tooltip over an airport: where it is, how big, the gates and the traffic, and what you fly from it.</summary>
        public string AirportTip(MapPlace place)
        {
            var airport = place?.Airport;
            var game = GameObject.GetInstance();
            var airline = game?.HumanAirline;
            if (airport == null || airline == null)
            {
                return null;
            }

            try
            {
                var p = airport.Profile;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(p.IATACode + " / " + p.ICAOCode + "  " + p.Name + (place.IsHub ? "  (your hub)" : ""));
                sb.AppendLine(p.Town?.Name + ", " + p.Country?.Name);
                sb.AppendLine("Size " + p.Size.ToString().Replace('_', ' ') + "  |  longest runway " + DistanceFormatter.Metres(airport.GetMaxRunwayLength()));

                int total = airport.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
                int free = airport.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
                int mine = airport.GetAirlineContracts(airline).Where(c => c.TerminalType == Terminal.TerminalType.Passenger).Sum(c => c.NumberOfGates);
                string gates = "Gates " + total + " (" + free + " free, " + mine + " yours)";
                int cargo = airport.Terminals.GetNumberOfGates(Terminal.TerminalType.Cargo);
                if (cargo > 0)
                {
                    gates += "  |  cargo gates " + cargo + " (" + airport.Terminals.GetFreeGates(Terminal.TerminalType.Cargo) + " free)";
                }
                sb.AppendLine(gates);

                int year = game.GameTime.Year;
                var pax = StatisticsTypes.GetStatisticsType("Passengers");
                var dep = StatisticsTypes.GetStatisticsType("Arrivals");
                double paxAll = airport.Statistics.GetTotalValue(year, pax);
                double paxMine = airport.Statistics.GetStatisticsValue(year, airline, pax);
                double depAll = airport.Statistics.GetTotalValue(year, dep);
                double depMine = airport.Statistics.GetStatisticsValue(year, airline, dep);
                sb.AppendLine("Passengers in " + year + ": " + Num(paxAll) + " (" + Num(paxMine) + " with you)");
                sb.AppendLine("Arrivals in " + year + ": " + Num(depAll) + " (" + Num(depMine) + " yours)");
                sb.AppendLine("Airlines here: " + airport.AirlineContracts.Select(c => c.Airline).Distinct().Count() + "  |  routes: " + AirportHelpers.GetAirportRoutes(airport).Count);

                var mineRoutes = airline.Routes.Where(r => r.Destination1 == airport || r.Destination2 == airport).ToList();
                if (mineRoutes.Count == 0)
                {
                    sb.Append("You fly no route from here.");
                }
                else
                {
                    var dests = mineRoutes.Select(r => (r.Destination1 == airport ? r.Destination2 : r.Destination1).Profile.IATACode).Distinct().ToList();
                    sb.Append("Your routes: " + mineRoutes.Count + " to " + string.Join(", ", dests.Take(8)) + (dests.Count > 8 ? " and " + (dests.Count - 8) + " more" : ""));
                }

                return sb.ToString();
            }
            catch (Exception)
            {
                return place.Label;
            }
        }

        /// <summary>The text of the tooltip over an aircraft.</summary>
        public string AircraftTip(MapAircraft a)
        {
            if (a == null)
            {
                return null;
            }

            try
            {
                var f = a.Aircraft;
                return a.Name + Environment.NewLine + a.Info + Environment.NewLine
                       + string.Format(
                           "Condition {0:0}%  |  pilots {1}/{2}  |  {3} route(s)", f.Airliner.Condition, f.Pilots.Count, f.Airliner.Type.CockpitCrew, f.Routes.Count)
                       + Environment.NewLine + "Speed " + DistanceFormatter.Speed(f.Airliner.Type.CruisingSpeed) + "  |  range " + DistanceFormatter.Km(f.Airliner.Type.Range);
            }
            catch (Exception)
            {
                return a.Name + Environment.NewLine + a.Info;
            }
        }

        public static double MercatorV(double latitude)
        {
            double lat = Math.Max(-85.05, Math.Min(85.05, latitude)) * Math.PI / 180;
            return (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2;
        }

        public static double MercatorU(double longitude)
        {
            return (longitude + 180) / 360;
        }

        private static double[] Project(double lat, double lon)
        {
            return new[] { MercatorU(lon), MercatorV(lat) };
        }

        private static void Coords(Airport a, out double lat, out double lon)
        {
            var g = a.Profile.Coordinates.ConvertToGeoCoordinate();
            lat = g.Latitude;
            lon = g.Longitude;
        }

        //a point on the great circle between two places, f from 0 to 1
        private static void Between(double lat1, double lon1, double lat2, double lon2, double f, out double lat, out double lon)
        {
            double p1 = lat1 * Math.PI / 180, l1 = lon1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, l2 = lon2 * Math.PI / 180;
            double d = 2 * Math.Asin(Math.Sqrt(
                Math.Pow(Math.Sin((p2 - p1) / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin((l2 - l1) / 2), 2)));
            if (d < 1e-9)
            {
                lat = lat1;
                lon = lon1;
                return;
            }

            double a = Math.Sin((1 - f) * d) / Math.Sin(d), b = Math.Sin(f * d) / Math.Sin(d);
            double x = a * Math.Cos(p1) * Math.Cos(l1) + b * Math.Cos(p2) * Math.Cos(l2);
            double y = a * Math.Cos(p1) * Math.Sin(l1) + b * Math.Cos(p2) * Math.Sin(l2);
            double z = a * Math.Sin(p1) + b * Math.Sin(p2);
            lat = Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI;
            lon = Math.Atan2(y, x) * 180 / Math.PI;
        }

        private static MapLine MakeLine(Airport a, Airport b, bool cargo)
        {
            double la1, lo1, la2, lo2;
            Coords(a, out la1, out lo1);
            Coords(b, out la2, out lo2);
            var line = new MapLine { IsCargo = cargo };
            var piece = new List<double[]>();
            double lastU = double.NaN;
            const int steps = 48;
            for (int i = 0; i <= steps; i++)
            {
                double lat, lon;
                Between(la1, lo1, la2, lo2, i / (double) steps, out lat, out lon);
                double[] p = Project(lat, lon);
                if (!double.IsNaN(lastU) && Math.Abs(p[0] - lastU) > 0.5)
                {
                    line.Pieces.Add(piece);
                    piece = new List<double[]>();
                }

                piece.Add(p);
                lastU = p[0];
            }

            line.Pieces.Add(piece);
            return line;
        }

        private static string Status(FleetAirliner f)
        {
            return f.Status.ToString().Replace("To", "To ").Replace("On", "On ").Trim();
        }

        private static MapAircraft Place(FleetAirliner f, DateTime now)
        {
            double lat, lon, heading = 0;
            bool flying = false;
            string info = Status(f);
            Flight flight = f.CurrentFlight;
            Airport at = f.CurrentPosition ?? f.Homebase;

            try
            {
                if (flight != null && flight.Entry != null && f.Status != FleetAirliner.AirlinerStatus.Stopped)
                {
                    Airport from = flight.Entry.DepartureAirport, to = flight.Entry.Destination.Airport;
                    DateTime takeoff = flight.FlightTime, landing = flight.GetExpectedLandingTime();
                    double la1, lo1, la2, lo2;
                    Coords(from, out la1, out lo1);
                    Coords(to, out la2, out lo2);
                    if (now >= takeoff && now < landing && landing > takeoff)
                    {
                        double fr = (now - takeoff).TotalSeconds / (landing - takeoff).TotalSeconds;
                        Between(la1, lo1, la2, lo2, fr, out lat, out lon);
                        double la3, lo3;
                        Between(la1, lo1, la2, lo2, Math.Min(1, fr + 0.01), out la3, out lo3);
                        double[] p0 = Project(lat, lon), p1 = Project(la3, lo3);
                        double du = p1[0] - p0[0];
                        if (du > 0.5) du -= 1; else if (du < -0.5) du += 1;
                        heading = Math.Atan2(du, -(p1[1] - p0[1])) * 180 / Math.PI;
                        flying = true;
                        info = "flying " + from.Profile.IATACode + " - " + to.Profile.IATACode + ", " + (int) (fr * 100) + "% of the way, lands "
                               + DateFormatter.DateTime(landing);
                        double[] pos = Project(lat, lon);
                        return Make(f, pos, heading, true, info);
                    }

                    if (now < takeoff && from != null)
                    {
                        at = from;
                        info = "at " + at.Profile.IATACode + ", departs " + DateFormatter.DateTime(takeoff) + " to " + to.Profile.IATACode;
                    }
                    else if (now >= landing)
                    {
                        at = to;
                        info = "landed at " + at.Profile.IATACode;
                    }
                }
                else
                {
                    info = (f.Status == FleetAirliner.AirlinerStatus.Stopped ? "stopped" : Status(f)) + " at " + at?.Profile.IATACode;
                }
            }
            catch (NullReferenceException)
            {
            }

            if (at == null)
            {
                return null;
            }

            Coords(at, out lat, out lon);
            return Make(f, Project(lat, lon), heading, flying, info);
        }

        private static MapAircraft Make(FleetAirliner f, double[] p, double heading, bool flying, string info)
        {
            return new MapAircraft
            {
                Aircraft = f,
                U = p[0],
                V = p[1],
                Heading = heading,
                InFlight = flying,
                Name = f.Name + " (" + f.Airliner.Type.Name + ")",
                Info = info
            };
        }

        /// <summary>Rebuild the markers from the game. Safe to call on every refresh.</summary>
        public void Reload()
        {
            Airline airline = GameObject.GetInstance()?.HumanAirline;
            if (airline == null)
            {
                return;
            }

            DateTime now = GameObject.GetInstance().GameTime;
            string keep = _selected?.Aircraft.Name;

            List<FleetAirliner> fleet;
            List<Route> routes;
            List<Airport> airports;
            try
            {
                fleet = airline.Fleet.ToList();
                routes = airline.Routes.ToList();
                airports = airline.Airports.ToList();
            }
            catch (InvalidOperationException)
            {
                return; // the game thread changed a list; try again on the next refresh
            }

            var hubs = new HashSet<Airport>();
            try { hubs = new HashSet<Airport>(airline.GetHubs()); } catch (Exception) { }
            foreach (FleetAirliner f in fleet.Where(f => f.Homebase != null)) hubs.Add(f.Homebase);

            var places = new List<MapPlace>();
            foreach (Airport a in airports.Union(routes.SelectMany(r => new[] { r.Destination1, r.Destination2 })).Distinct())
            {
                if (a?.Profile?.Coordinates == null) continue;
                double lat, lon;
                Coords(a, out lat, out lon);
                places.Add(new MapPlace { U = MercatorU(lon), V = MercatorV(lat), Label = a.Profile.IATACode, IsHub = hubs.Contains(a), Airport = a });
            }

            var lines = new List<MapLine>();
            var seen = new HashSet<string>();
            foreach (Route r in routes)
            {
                if (r.Destination1?.Profile?.Coordinates == null || r.Destination2?.Profile?.Coordinates == null) continue;
                string key = string.CompareOrdinal(r.Destination1.Profile.IATACode, r.Destination2.Profile.IATACode) < 0
                    ? r.Destination1.Profile.IATACode + r.Destination2.Profile.IATACode + r.IsCargoRoute
                    : r.Destination2.Profile.IATACode + r.Destination1.Profile.IATACode + r.IsCargoRoute;
                if (seen.Add(key)) lines.Add(MakeLine(r.Destination1, r.Destination2, r.IsCargoRoute));
            }

            var aircraft = fleet.Where(f => f.Airliner.BuiltDate <= now).Select(f => Place(f, now)).Where(a => a != null).ToList();

            Places = places;
            Lines = lines;
            Aircraft = aircraft;
            _selected = aircraft.FirstOrDefault(a => a.Aircraft.Name == keep);
            OnPropertyChanged(nameof(SelectedText));
            int flying = aircraft.Count(a => a.InFlight);
            Caption = string.Format("{0} aircraft ({1} in the air), {2} routes, {3} airports", aircraft.Count, flying, lines.Count, places.Count);
            Changed?.Invoke();
        }
    }
}
