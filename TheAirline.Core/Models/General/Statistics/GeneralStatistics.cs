using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheAirline.Infrastructure;

namespace TheAirline.Models.General.Statistics
{
    //the class for general statistics
    [Serializable]
    public class GeneralStatistics : BaseModel
    {
        #region Constructors and Destructors

        public GeneralStatistics()
        {
            StatValues = new List<StatisticsValue>();
        }

        protected GeneralStatistics(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("values")]
        public List<StatisticsValue> StatValues { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        // An airline's own flights are simulated on one thread, but an aircraft leased to another airline, and the
        // flights an airline's subsidiaries fly, are counted from that other airline's thread. Finding the line and
        // then adding to it is a read, a change and a write: without the lock two threads could both find none and
        // both add a line (only the first of which is ever read again), or both read the same total and write it back
        // once, and either way flights are quietly lost.
        public void AddStatisticsValue(int year, StatisticsType type, double value)
        {
            lock (StatValues)
            {
                StatisticsValue item = StatValues.Find(s => s.Year == year && s.Stat.Shortname == type.Shortname);

                if (item == null)
                {
                    StatValues.Add(new StatisticsValue(year, type, value));
                }
                else
                {
                    item.Value += value;
                }
            }
        }

        public void Clear()
        {
            lock (StatValues)
            {
                StatValues.Clear();
            }
        }

        //returns the value for a statistics type for a year
        public double GetStatisticsValue(int year, StatisticsType type)
        {
            List<StatisticsValue> stats;
            lock (StatValues) stats = new List<StatisticsValue>(StatValues);

            StatisticsValue item =
                stats.FirstOrDefault(s => s.Stat != null && s.Year == year && s.Stat.Shortname == type.Shortname);

            if (item == null)
            {
                return 0;
            }
            return item.Value;
        }

        //returns the total value for a statistics type
        public double GetStatisticsValue(StatisticsType type)
        {
            if (StatValues == null) return 0;
            lock (StatValues)
            {
                return StatValues.Where(s => s.Stat != null && s.Stat.Shortname == type.Shortname).Sum(s => s.Value);
            }
        }

        //adds the value for a statistics type for a year

        //returns all years with statistics
        public List<int> GetYears()
        {
            lock (StatValues) return StatValues.Select(s => s.Year).Distinct().ToList();
        }

        public void SetStatisticsValue(int year, StatisticsType type, double value)
        {
            lock (StatValues)
            {
                StatisticsValue item = StatValues.Find(s => s.Year == year && s.Stat.Shortname == type.Shortname);

                if (item == null)
                {
                    StatValues.Add(new StatisticsValue(year, type, value));
                }
                else
                {
                    item.Value = value;
                }
            }
        }

        #endregion

        //clears the statistics
    }
}