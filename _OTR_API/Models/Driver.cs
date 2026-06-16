using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class Driver
    {
        public int ID { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Cellphone { get; set; }

        public string EmailAddress { get; set; }

        public string DriversLicense { get; set; }

        public string MCNumber { get; set; }

        public string TruckNumber { get; set; }

        public string TruckMake { get; set; }

        public string TruckTag { get; set; }

        public string TruckColor { get; set; }

        public string TrailerNumber { get; set; }

        public string TrailerType { get; set; }

        public string Capacity { get; set; }

        public string Size { get; set; }

        public string Misc { get; set; }

        public string Password { get; set; }

        public string Profile { get; set; }

        public bool Notifications { get; set; }

        public bool BackgroundSync { get; set; }

        public string Message { get; set; }

        public string DeviceID { get; set; }

        public string ResetToken { get; set; }

        public DateTime Created { get; set; }

        public List<Documents> Documents { get; set; }

        public DriverDevice Devices { get; set; }

        public int GeoFenceRate { get; set; }

        public int GeoFenceUnit { get; set; }

        public int CompanyID { get; set; }        public InMotionUrl Url { get; set; }    }

    public class DriverDevice
    {
        public Int32 ID { get; set; }

        public int  DriverID {get; set;}

        public string DeviceID { get; set; }

        public string Token { get; set; }

        public string DeviceType { get; set; }

        public DateTime Created { get; set; }

        public DateTime Updated { get; set; }
    }

    public class DriverTrip
    {
        public int ID { get; set; }

        public int TripID { get; set; }

        public int DriverID { get; set; }

        public int LoadID { get; set; }

        public bool Active { get; set; }

        public DateTime ActiveDate { get; set; }

        public Driver Driver { get; set; }        public Loads Load { get; set; }

        public string Message { get; set; }

        public bool ForcedStop { get; set; }

        public bool Completed { get; set; }

        public DateTime EndDate { get; set; }    }

    public class DriverNotification
    {
        public int ID { get; set; }

        public int DriverID { get; set; }

        public int DriverTripID { get; set; }

        public string DeviceID { get; set; }

        public string DeviceType { get; set; }

        public string Token { get; set; }

        public string Title { get; set; }

        public string Message { get; set; }

        public string MessageType { get; set; }

        public string MessageData { get; set; }

        public DateTime Created { get; set; }

        public string MessageResult { get; set; }        public string SentMessageJson { get; set; }

        public string ResultMessageJson { get; set; }        public string multicast_id { get; set; }        public string message_id { get; set; }        public DateTime Viewed { get; set; }

        public GPSLocation GPSCoordinates { get; set; }

        public string Timezone { get; set; }

        public string Offset { get; set; }
    }


    public class NotificationResult
    {
        public long multicast_id { get; set; }
        public int success { get; set; }
        public int failure { get; set; }
        public int canonical_ids { get; set; }
        public List<Result> results { get; set; }
    }

    public class Result
    {
        public string message_id { get; set; }
        public string error { get; set; }
        public string registration_id { get; set; }
    }


    public class Company
    {
        public int ID { get; set; }

        public string CompanyName { get; set; }
    }
}