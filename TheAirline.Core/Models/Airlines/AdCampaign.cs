using System;
using System.Runtime.Serialization;
using TheAirline.Infrastructure;

namespace TheAirline.Models.Airlines
{
    /// <summary>A time-limited advertising campaign aimed at one country or one airport.</summary>
    [Serializable]
    public class AdCampaign : BaseModel
    {
        public AdCampaign(string target, bool isAirport, int level, DateTime start, DateTime end, double monthlyCost)
        {
            Target = target;
            IsAirport = isAirport;
            Level = level;
            Start = start;
            End = end;
            MonthlyCost = monthlyCost;
        }

        private AdCampaign(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        /// <summary>The airport's IATA code or the country's id.</summary>
        [Versioning("target")]
        public string Target { get; set; }

        [Versioning("isairport")]
        public bool IsAirport { get; set; }

        /// <summary>1 small, 2 medium, 3 large.</summary>
        [Versioning("level")]
        public int Level { get; set; }

        [Versioning("start")]
        public DateTime Start { get; set; }

        [Versioning("end")]
        public DateTime End { get; set; }

        [Versioning("monthlycost")]
        public double MonthlyCost { get; set; }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);
            base.GetObjectData(info, context);
        }
    }
}
