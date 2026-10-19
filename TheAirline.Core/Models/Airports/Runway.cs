using System;
using System.Runtime.Serialization;
using TheAirline.Infrastructure;

namespace TheAirline.Models.Airports
{
    //the class for a runway at an airport
    [Serializable]
    public class Runway : BaseModel
    {
        #region Constructors and Destructors

        public Runway(string name, long length, RunwayType type, SurfaceType surface, DateTime builtDate, Boolean standard)
        {
            Name = name;
            Length = length;
            Surface = surface;
            BuiltDate = builtDate;
            Standard = standard;
            Type = type;
        }

        private Runway(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
                Type = RunwayType.Regular;
        }

        #endregion

        #region Enums

        public enum RunwayType
        {
            Regular,

            Helipad
        }

        public enum SurfaceType
        {
            Asphalt,

            Concrete,

            Grass,

            Bitumen,

            Dirt,

            Gravel,

            Ice,

            Salt,

            Paved,

            Steel,

            Unpaved,

            Sand,

            PSP,
            Clay
        }

        #endregion

        #region Public Properties

        [Versioning("builtdate")]
        public DateTime BuiltDate { get; set; }

        [Versioning("length")]
        public long Length { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("standard")]
        public bool Standard { get; set; }

        [Versioning("surface")]
        public SurfaceType Surface { get; set; }

        [Versioning("type", Version = 2)]
        public RunwayType Type { get; set; }

        /// <summary>The airline that paid for the runway or its last extension, or null for the airport's own.</summary>
        [Versioning("builtby")]
        public TheAirline.Models.Airlines.Airline BuiltBy { get; set; }

        /// <summary>The length the runway is being extended to (0 when not being extended).</summary>
        [Versioning("extendto")]
        public long ExtendTo { get; set; }

        /// <summary>When the extension is finished.</summary>
        [Versioning("extenddate")]
        public DateTime ExtendDate { get; set; }

        public bool IsExtending => ExtendTo > Length;

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 2);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}