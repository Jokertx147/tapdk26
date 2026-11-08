using System;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Which new aircraft types can be ordered, and when an order is delivered (both apps use these). A type can be
    ///     ordered up to two years before its first deliveries, as airlines do (the 777 was ordered from 1990 and
    ///     delivered from 1995); its orders are delivered from then.
    /// </summary>
    public static class AircraftOrderRules
    {
        /// <summary>How far ahead of its first deliveries a new type can be ordered.</summary>
        public const int OrderAheadMonths = 24;

        public static bool CanOrderNew(AirlinerType t)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            return t != null && t.Produced != null && t.Produced.From <= now.AddMonths(OrderAheadMonths) && t.Produced.To > now;
        }

        /// <summary>The type's first deliveries are still to come.</summary>
        public static bool NotDeliveredYet(AirlinerType t)
        {
            return t?.Produced != null && t.Produced.From > GameObject.GetInstance().GameTime;
        }

        /// <summary>The year whose engines an order of the type gets: its first deliveries' for a type not delivered yet.</summary>
        public static int EngineYear(AirlinerType type)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            return NotDeliveredYet(type) ? type.Produced.From.Year : now.Year;
        }

        /// <summary>The delivery date from the production rate, as in the Windows version; a type not delivered yet from its first deliveries.</summary>
        public static DateTime DeliveryDate(AirlinerType type, int amount)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            var today = new DateTime(now.Year, now.Month, now.Day);
            DateTime date = type.Produced.From.Date > today ? type.Produced.From.Date : today;
            int rate = Math.Max(1, type.ProductionRate);
            if (amount <= rate / 4)
            {
                DateTime soonest = today.AddMonths(3);
                return soonest > date ? soonest : date;
            }

            for (int i = (rate / 4) + 1; i <= amount; i++)
            {
                date = date.AddDays(Math.Round((double) (365 / rate), 0, MidpointRounding.AwayFromZero));
            }

            return date;
        }
    }
}
