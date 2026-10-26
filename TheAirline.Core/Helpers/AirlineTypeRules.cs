using TheAirline.Models.Airliners;
using TheAirline.Models.Airlines;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     A passenger airline flies passenger routes with passenger aircraft and a cargo airline cargo routes with
    ///     freighters. To do both, an airline owns a subsidiary of the other kind. An airline's kind can only change while
    ///     it has no routes and no aircraft. Every airline that isn't a cargo airline (passenger, helicopter, or the old
    ///     "passengers and cargo" profile setting) is a passenger airline.
    /// </summary>
    public static class AirlineTypeRules
    {
        public static bool IsCargoAirline(Airline a)
        {
            return a != null && a.AirlineRouteFocus == Route.RouteType.Cargo;
        }

        /// <summary>Only "no airline" (game setup checks) is not bound to a kind.</summary>
        private static bool FliesBoth(Airline a)
        {
            return a == null;
        }

        public static bool CanFlyRoute(Airline a, Route.RouteType type)
        {
            if (FliesBoth(a)) return true;
            return IsCargoAirline(a) ? type == Route.RouteType.Cargo : type != Route.RouteType.Cargo;
        }

        public static bool CanUseType(Airline a, AirlinerType type)
        {
            if (FliesBoth(a) || type == null) return true;
            bool freighter = type.TypeAirliner == AirlinerType.TypeOfAirliner.Cargo;
            return IsCargoAirline(a) ? freighter : !freighter;
        }

        public static string Kind(Airline a)
        {
            if (FliesBoth(a)) return "passenger and cargo airline";
            return IsCargoAirline(a) ? "cargo airline" : "passenger airline";
        }

        private static string Other(Airline a)
        {
            return IsCargoAirline(a) ? "passenger" : "cargo";
        }

        /// <summary>Why the airline can't fly a route of this kind, or null.</summary>
        public static string RouteBlocker(Airline a, Route.RouteType type)
        {
            if (CanFlyRoute(a, type)) return null;
            return a.Profile.Name + " is a " + Kind(a) + " and can only fly " + (IsCargoAirline(a) ? "cargo" : "passenger") +
                   " routes. To fly " + Other(a) + " routes, start a " + Other(a) + " subsidiary (Airlines > Subsidiaries).";
        }

        /// <summary>Why the airline can't buy or lease an aircraft of this type, or null.</summary>
        public static string TypeBlocker(Airline a, AirlinerType type)
        {
            if (CanUseType(a, type)) return null;
            return a.Profile.Name + " is a " + Kind(a) + ": " + (IsCargoAirline(a) ? "only freighters" : "no freighters") +
                   ". A " + Other(a) + " subsidiary can fly the " + type.Name + ".";
        }

        /// <summary>Why the airline's kind can't change now, or null.</summary>
        public static string KindChangeBlocker(Airline a, Route.RouteType newFocus)
        {
            if (a == null || IsCargoAirline(a) == (newFocus == Route.RouteType.Cargo)) return null;
            if (a.Routes.Count > 0 || a.Fleet.Count > 0)
                return "An airline's kind (passenger or cargo) can only change while it has no routes and no aircraft. Start a subsidiary of the other kind instead (Airlines > Subsidiaries).";
            return null;
        }

        /// <summary>Why two airlines can't be merged into one, or null.</summary>
        public static string MergeBlocker(Airline into, Airline other)
        {
            if (into == null || other == null || FliesBoth(into) || FliesBoth(other)) return null;
            if (IsCargoAirline(into) == IsCargoAirline(other)) return null;
            return other.Profile.Name + " is a " + Kind(other) + " and " + into.Profile.Name + " a " + Kind(into) +
                   ": they can't become one airline. Keep it as a subsidiary instead.";
        }
    }
}
