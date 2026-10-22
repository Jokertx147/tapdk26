using System;
using System.Runtime.Serialization;
using TheAirline.Infrastructure;

namespace TheAirline.Models.Airlines
{
    /// <summary>A block of an airline's shares: who holds them (null = the public), how many, and what they paid.</summary>
    [Serializable]
    public class ShareHolding : BaseModel
    {
        public ShareHolding(Airline holder, int count, double cost)
        {
            Holder = holder;
            Count = count;
            Cost = cost;
        }

        private ShareHolding(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        [Versioning("holder")]
        public Airline Holder { get; set; }

        [Versioning("count")]
        public int Count { get; set; }

        /// <summary>What the holder paid for these shares in all (0 for the airline's own and the public's).</summary>
        [Versioning("cost")]
        public double Cost { get; set; }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);
            base.GetObjectData(info, context);
        }
    }
}
