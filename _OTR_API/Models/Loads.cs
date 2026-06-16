using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class Loads
    { 
        public int ID { get; set; }

        public int LoadID { get; set; }

        public int TripID { get; set; }

        public string LoadStatus { get; set; }

        public string Temp { get; set; }

        public int TotalPallets { get; set; }

        public int TotalWeight { get; set; }

        public int TotalPieces { get; set; }

        public int TotalMiles { get; set; }

        public bool HazMat { get; set; }

        public CompanyReps CompanyRep { get; set; }

        public List<LoadStops> LoadStops { get; set; }

        public string Message { get; set; }

        public int DriverTripID { get; set; }

        public int DriverID { get; set; }

        public string Driver { get; set; }

        public bool Active { get; set; }

        public DateTime ActiveDate { get; set; }

    }

    public class LoadStops
    {
        public int ID { get; set; }

        public int LoadDetailID { get; set; }

        public int LoadID { get; set; }

        public int LoadStopNumber { get; set; }

        public int StopTypeID { get; set; }

        public string StopType { get; set; }

        public DateTime ScheduleDate { get; set; }

        public string ScheduleTimeFrom { get; set; }

        public string ScheduleTimeTo { get; set; }

        public string Name { get; set; }

        public string Address1 { get; set; }

        public string Address2 { get; set; }

        public string City { get; set; }

        public string State { get; set; }

        public string Zip { get; set; }

        public string Contact { get; set; }

        public string Phone { get; set; }

        public string AltPhone { get; set; }

        public GPSLocation GPSCoordinates { get; set; }

        public string CommodityDesc { get; set; }

        public int Weight { get; set; }

        public int Pieces { get; set; }

        public int Pallets { get; set; }

        public List<ReferenceNumbers> ReferenceNumbers { get; set; }

    }

    public class ReferenceNumbers
    {
        public int ID { get; set; }

        public int ReferenceNumberID { get; set; }

        public int LoadDetailID { get; set; }

        public string ReferenceNumberType { get; set; }

        public string ReferenceNumber { get; set; }

    }

    public class LoadConfirms
    {
        public int ConfirmID { get; set; }

        public int LoadID { get; set; }

        public string ConfirmCode { get; set; }

        public DateTime ExpirationDate { get; set; }

        public DriverTrip DriverTrip { get; set; }

        public Driver Driver { get; set; }

        public GPSLocation GPS { get; set; }

        public DateTime ConfirmDate { get; set; }

        public string LoadStatus { get; set; }

        public string Message { get; set; }

        public string Timezone { get; set; }

        public string Offset { get; set; }

        public bool Active { get; set; }

    }

    public class TripPath
    {
        public string EventType { get; set; }
        public DateTime EventDate { get; set; }
        public string Message { get; set; }
        public GPSLocation GPSLocation { get; set; }
        public int ID { get; set; }
        public string Timezone { get; set; }
        public string Offset { get; set; }

    }

}