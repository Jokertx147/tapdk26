using System;

// The game was written against System.Device.Location.GeoCoordinate, which only exists in
// .NET Framework (System.Device.dll). This small replacement keeps the same namespace and
// members the game uses, so the existing "using System.Device.Location;" lines keep working.
namespace System.Device.Location
{
    [Serializable]
    public class GeoCoordinate : IEquatable<GeoCoordinate>
    {
        //mean earth radius in metres used by the original GetDistanceTo (haversine)
        private const double EarthRadiusInMetres = 6376500.0;
        private const double DegreesToRadians = 0.0174532925199433;

        public static readonly GeoCoordinate Unknown = new GeoCoordinate();

        public GeoCoordinate()
            : this(double.NaN, double.NaN)
        {
        }

        public GeoCoordinate(double latitude, double longitude)
            : this(latitude, longitude, double.NaN)
        {
        }

        public GeoCoordinate(double latitude, double longitude, double altitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
            HorizontalAccuracy = double.NaN;
            VerticalAccuracy = double.NaN;
            Speed = double.NaN;
            Course = double.NaN;
        }

        public double Altitude { get; set; }

        public double Course { get; set; }

        public double HorizontalAccuracy { get; set; }

        public bool IsUnknown
        {
            get { return double.IsNaN(Latitude) && double.IsNaN(Longitude); }
        }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public double Speed { get; set; }

        public double VerticalAccuracy { get; set; }

        //distance in metres along the earth's surface
        public double GetDistanceTo(GeoCoordinate other)
        {
            if (other == null)
            {
                throw new ArgumentNullException("other");
            }

            if (double.IsNaN(Latitude) || double.IsNaN(Longitude) || double.IsNaN(other.Latitude)
                || double.IsNaN(other.Longitude))
            {
                throw new ArgumentException("Argument latitude or longitude is not a number");
            }

            double latitude1 = Latitude*DegreesToRadians;
            double longitude1 = Longitude*DegreesToRadians;
            double latitude2 = other.Latitude*DegreesToRadians;
            double longitude2 = other.Longitude*DegreesToRadians;

            double deltaLongitude = longitude2 - longitude1;
            double deltaLatitude = latitude2 - latitude1;

            double a = Math.Pow(Math.Sin(deltaLatitude/2.0), 2.0)
                       + Math.Cos(latitude1)*Math.Cos(latitude2)*Math.Pow(Math.Sin(deltaLongitude/2.0), 2.0);

            double c = 2.0*Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

            return EarthRadiusInMetres*c;
        }

        public bool Equals(GeoCoordinate other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GeoCoordinate);
        }

        public override int GetHashCode()
        {
            return Latitude.GetHashCode() ^ Longitude.GetHashCode();
        }

        public override string ToString()
        {
            if (IsUnknown)
            {
                return "Unknown";
            }

            return string.Format("{0}, {1}", Latitude.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                Longitude.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        }

        public static bool operator ==(GeoCoordinate left, GeoCoordinate right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(GeoCoordinate left, GeoCoordinate right)
        {
            return !(left == right);
        }
    }
}
