using System;
using System.Runtime.CompilerServices;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     A date remembered per route or airline that doesn't keep it alive: when a game ends and its world is
    ///     dropped, its entries go with it (a plain dictionary would keep the whole old world in memory).
    /// </summary>
    public class WeakDates<T> where T : class
    {
        private ConditionalWeakTable<T, StrongBox<DateTime>> _table = new ConditionalWeakTable<T, StrongBox<DateTime>>();

        public bool TryGetValue(T key, out DateTime value)
        {
            StrongBox<DateTime> box;
            if (key != null && _table.TryGetValue(key, out box))
            {
                value = box.Value;
                return true;
            }
            value = default(DateTime);
            return false;
        }

        public DateTime this[T key]
        {
            set { _table.AddOrUpdate(key, new StrongBox<DateTime>(value)); }
        }

        public void Clear()
        {
            _table = new ConditionalWeakTable<T, StrongBox<DateTime>>();
        }
    }
}
