using System.Linq;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.Routes;

namespace TheAirline.GUIModel.PagesModel.RoutesPageModel
{
    public enum IntervalType
    {
        Day,

        Week
    }

    //the class for the route planner mvvm item
    public class RoutePlannerItemMVVM
    {
        #region Constructors and Destructors

        public RoutePlannerItemMVVM(Route route, AirlinerType type)
        {
            Route = route;

            var g2 = new Guid(Route.Id);

            byte[] bytes = g2.ToByteArray();

            byte red = bytes[0];
            byte green = bytes[1];
            byte blue = bytes[2];

            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Opacity = 0.4;

            Brush = brush;
            FlightTime = MathHelpers.GetFlightTime(Route.Destination1, Route.Destination2, type);

            Airliners = Route.GetAirliners().Count;
        }

        #endregion

        #region Public Properties

        public int Airliners { get; set; }

        public Brush Brush { get; set; }

        public TimeSpan FlightTime { get; set; }

        public Route Route { get; set; }

        #endregion
    }

    //the mvvm class for an hour item
    public class HourItemMVVM
    {
        #region Constructors and Destructors

        public HourItemMVVM(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }

        #endregion

        #region Public Properties

        public DateTime End { get; set; }

        public DateTime Start { get; set; }

        #endregion
    }

    //the mvvm class for the route planner entry
    public class RouteEntryVVM
    {
        #region Constructors and Destructors

        public RouteEntryVVM(Route route, DayOfWeek day, TimeSpan departure)
        {
            Route = route;
            Day = day;
            DepartureTime = departure;
        }

        #endregion

        #region Public Properties

        public DayOfWeek Day { get; set; }

        public TimeSpan DepartureTime { get; set; }

        public Route Route { get; set; }

        #endregion
    }

    //the converter for the homebound airport
    public class RouteHomeboundConverter : IMultiValueConverter
    {
        #region Public Methods and Operators

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var airport = (Airport)values[0];
                var route = (Route)values[1];

                return route.Destination1 == airport ? route.Destination2 : route.Destination1;
            }
            catch
            {
                return null;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
namespace TheAirline.GUIModel.PagesModel.RoutesPageModel
{
    /// <summary>
    ///     Shows the passenger demand of a route as a percentage of the route with the highest demand
    ///     among the routes of the route planner (empty when the demand is not known, e.g. for cargo).
    /// </summary>
    public class RouteDemandConverter : System.Windows.Data.IValueConverter
    {
        // filled by the route planner when it is opened
        public static System.Collections.Generic.Dictionary<Route, int> Percents { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var route = value as Route;
            int percent;

            if (route == null || Percents == null || !Percents.TryGetValue(route, out percent))
            {
                return "";
            }

            return percent + "%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>Daily passengers in both directions, all classes.</summary>
        public static int GetDemand(Route route)
        {
            int demand = 0;

            foreach (AirlinerClass.ClassType type in Enum.GetValues(typeof (AirlinerClass.ClassType)))
            {
                demand += route.Destination1.GetDestinationPassengersRate(route.Destination2, type)
                          + route.Destination2.GetDestinationPassengersRate(route.Destination1, type);
            }

            return demand;
        }

        public static System.Collections.Generic.Dictionary<Route, int> Calculate(System.Collections.Generic.IEnumerable<Route> routes)
        {
            var demands = new System.Collections.Generic.Dictionary<Route, int>();

            foreach (Route route in routes)
            {
                demands[route] = GetDemand(route);
            }

            int max = demands.Count == 0 ? 0 : demands.Values.Max();
            var percents = new System.Collections.Generic.Dictionary<Route, int>();

            foreach (var pair in demands)
            {
                percents[pair.Key] = max <= 0 ? 0 : (int) Math.Round(100.0*pair.Value/max);
            }

            return percents;
        }
    }
}
