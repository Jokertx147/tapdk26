using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheAirline.Infrastructure;
using TheAirline.Models.Airlines;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Pilots;
using TheAirline.Models.Routes;

namespace TheAirline.Models.Airliners
{
    [Serializable]
    public class FleetAirliner : BaseModel
    {
        #region Constructors and Destructors

        public FleetAirliner(
            PurchasedType purchased,
            DateTime purchasedDate,
            Airline airline,
            Airliner airliner,
            Airport homebase)
        {
            Airliner = airliner;
            Purchased = purchased;
            PurchasedDate = purchasedDate;
            Airliner.Airline = airline;
            Homebase = homebase;
            Name = airliner.TailNumber;
            Statistics = new AirlinerStatistics(this);
            LastCMaintenance = Airliner.BuiltDate;
            LastAMaintenance = Airliner.BuiltDate;
            LastBMaintenance = Airliner.BuiltDate;
            LastDMaintenance = Airliner.BuiltDate;
            Status = AirlinerStatus.Stopped;
            MaintRoutes = new List<Route>();

            CurrentPosition = Homebase;
            //new GeoCoordinate(this.Homebase.Profile.Coordinates.Latitude,this.Homebase.Profile.Coordinates.Longitude);

            Routes = new List<Route>();
            Pilots = new List<Pilot>();
            InsurancePolicies = new List<AirlinerInsurance>();
            MaintenanceHistory = new Dictionary<Invoice, string>();

            Data = new OperatingData();

            if (Purchased == PurchasedType.Bought || Purchased == PurchasedType.BoughtDownPayment)
                Airliner.Owner = Airliner.Airline;
        }

        private FleetAirliner(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
                Data = new OperatingData();
        }

        #endregion

        #region Enums

        public enum AirlinerStatus
        {
            Stopped,

            OnRoute,

            OnService,

            OnCharter,

            Resting,

            ToHomebase,

            ToRouteStart
        }

        public enum PurchasedType
        {
            Bought,

            Leased,

            BoughtDownPayment
        }

        #endregion

        #region Public Properties

        [Versioning("ainterval")]
        public int AMaintenanceInterval { get; set; }

        /// <summary>While a spare aircraft covers this one during a heavy check, its flights carry on until this date.</summary>
        [Versioning("covereduntil")]
        public DateTime CoveredUntil { get; set; }

        /// <summary>A spare aircraft is tied up covering another one until this date.</summary>
        [Versioning("coveruntil")]
        public DateTime CoverUntil { get; set; }

        /// <summary>Registration of the other aircraft in a cover arrangement (the spare, or the one being covered).</summary>
        [Versioning("covername")]
        public string CoverName { get; set; }

        /// <summary>When on, the checks that fall due are done automatically.</summary>
        [Versioning("automaintenance")]
        public bool AutoMaintenance { get; set; }

        /// <summary>The aircraft is out on a charter flight until this date.</summary>
        [Versioning("charteruntil")]
        public DateTime CharterUntil { get; set; }

        [Versioning("charterpay")]
        public double CharterPay { get; set; }

        [Versioning("chartercost")]
        public double CharterCost { get; set; }

        [Versioning("charterdestination")]
        public Airport CharterDestination { get; set; }

        [Versioning("chartertext")]
        public string CharterText { get; set; }

        /// <summary>The airline this aircraft is leased out to, until LeaseOutUntil.</summary>
        [Versioning("leaseoutto")]
        public Airline LeaseOutTo { get; set; }

        [Versioning("leaseoutuntil")]
        public DateTime LeaseOutUntil { get; set; }

        [Versioning("leaseoutrent")]
        public double LeaseOutRent { get; set; }

        [Versioning("leaseoutstart")]
        public DateTime LeaseOutStart { get; set; }

        /// <summary>Asking price when the aircraft is listed for sale (0 when not listed).</summary>
        [Versioning("saleask")]
        public double SaleAsk { get; set; }

        [Versioning("salesince")]
        public DateTime SaleSince { get; set; }

        /// <summary>An automatic ferry flight is under way to this airport until FerryUntil.</summary>
        [Versioning("ferryto")]
        public Airport FerryTo { get; set; }

        [Versioning("ferryuntil")]
        public DateTime FerryUntil { get; set; }

        /// <summary>The last day this aircraft flew a scheduled flight.</summary>
        [Versioning("lastflightdate")]
        public DateTime LastFlightDate { get; set; }

        [Versioning("airliner")]
        public Airliner Airliner { get; set; }

        [Versioning("binterval")]
        public int BMaintenanceInterval { get; set; }

        [Versioning("cinterval")]
        public int CMaintenanceInterval { get; set; }

        [Versioning("currentflight")]
        public Flight CurrentFlight { get; set; }

        [Versioning("currentposition")]
        public Airport CurrentPosition { get; set; }

        [Versioning("dinterval")]
        public int DMaintenanceInterval { get; set; }

        [Versioning("duecmaintenance")]
        public DateTime DueCMaintenance { get; set; }

        [Versioning("duedmaintenance")]
        public DateTime DueDMaintenance { get; set; }

        [Versioning("groundedto")]
        public DateTime GroundedToDate { get; set; }

        [Versioning("data", Version = 2)]
        public OperatingData Data { get; set; }

        public bool HasRoute => Routes.Count > 0;

        [Versioning("homebase")]
        public Airport Homebase { get; set; }

        [Versioning("insurancepolicies")]
        public List<AirlinerInsurance> InsurancePolicies { get; set; }

        [Versioning("lastamaintenance")]
        public DateTime LastAMaintenance { get; set; }

        [Versioning("lastbmaintenance")]
        public DateTime LastBMaintenance { get; set; }

        [Versioning("lastcmaintenance")]
        public DateTime LastCMaintenance { get; set; }

        [Versioning("lastdmaintenance")]
        public DateTime LastDMaintenance { get; set; }

        [Versioning("maintroutes")]
        public List<Route> MaintRoutes { get; set; }

        [Versioning("maintenancehistory")]
        public IDictionary<Invoice, string> MaintenanceHistory { get; set; }

        [Versioning("name")]
        private string _name;

        //the name of a fleet airliner is its registration, so it follows registration changes
        public string Name
        {
            get { return Airliner?.TailNumber ?? _name; }
            set { _name = value; }
        }

        public int NumberOfPilots => Pilots.Count;

        [Versioning("oosdate")]
        public DateTime OOSDate { get; set; }

        [Versioning("pilots")]
        public List<Pilot> Pilots { get; set; }

        [Versioning("purchased")]
        public PurchasedType Purchased { get; set; }

        //what is still to be paid on delivery day for an aircraft ordered with a down payment (0 in older saves: then the list price is charged)
        [Versioning("remainingpayment")]
        public double RemainingPayment { get; set; }

        [Versioning("date")]
        public DateTime PurchasedDate { get; set; }

        [Versioning("routes")]
        private List<Route> _routes;
        public List<Route> Routes
        {
            get { return _routes ?? (_routes = new List<Route>()); }
            set { _routes = value; }
        }

        [Versioning("schedamaintenance")]
        public DateTime SchedAMaintenance { get; set; }

        [Versioning("schedbmaintenance")]
        public DateTime SchedBMaintenance { get; set; }

        [Versioning("schedcmaintenance")]
        public DateTime SchedCMaintenance { get; set; }

        [Versioning("scheddmaintenance")]
        public DateTime SchedDMaintenance { get; set; }

        [Versioning("statistics")]
        public AirlinerStatistics Statistics { get; set; }

        [Versioning("status")]
        public AirlinerStatus Status { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        //adds a pilot to the airliner
        public void AddPilot(Pilot pilot)
        {
            lock (Pilots)
            {
                Pilots.Add(pilot);
                pilot.Airliner = this;
            }
        }

        //removes a pilot from the airliner

        //adds a route to the airliner
        public void AddRoute(Route route)
        {
            Routes.Add(route);
        }

        public void RemovePilot(Pilot pilot)
        {
            lock (Pilots)
            {
                Pilots.Remove(pilot);
                pilot.Airliner = null;
            }
        }

        //removes a route from the airliner
        public void RemoveRoute(Route route)
        {
            Routes.Remove(route);
            route.TimeTable.Entries.RemoveAll(e => e.Airliner == this);
        }

        #endregion
    }
}