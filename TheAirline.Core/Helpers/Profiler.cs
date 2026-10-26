using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace TheAirline.Helpers
{
    /// <summary>Adds up the time spent in parts of the simulation (for finding what is slow; off in normal play).</summary>
    public static class Profiler
    {
        public static bool Enabled;
        private static readonly ConcurrentDictionary<string, long[]> Totals = new ConcurrentDictionary<string, long[]>();

        public struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long _start;

            public Scope(string name)
            {
                _name = name;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_name != null) Add(_name, Stopwatch.GetTimestamp() - _start);
            }
        }

        /// <summary>A start time for Stop (0 when off).</summary>
        public static long Start()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0;
        }

        public static void Stop(string name, long start)
        {
            if (start != 0) Add(name, Stopwatch.GetTimestamp() - start);
        }

        public static Scope Measure(string name)
        {
            return Enabled ? new Scope(name) : default(Scope);
        }

        public static void Add(string name, long ticks)
        {
            var a = Totals.GetOrAdd(name, _ => new long[2]);
            Interlocked.Add(ref a[0], ticks);
            Interlocked.Increment(ref a[1]);
        }

        public static void Reset()
        {
            Totals.Clear();
        }

        /// <summary>Each part's total time (summed over threads), calls and time per call, biggest first.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            foreach (var kv in Totals.OrderByDescending(k => k.Value[0]))
            {
                double ms = kv.Value[0]*1000.0/Stopwatch.Frequency;
                sb.AppendLine(string.Format("  {0,-40} {1,10:N0} ms {2,10:N0} calls {3,9:N3} ms each", kv.Key, ms, kv.Value[1], kv.Value[1] > 0 ? ms/kv.Value[1] : 0));
            }
            return sb.ToString();
        }
    }
}
