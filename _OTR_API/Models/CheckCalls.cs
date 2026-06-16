using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class CheckCalls
    {
        public int ID { get; set; }

        public int DriverTripID { get; set; }

        public int CheckTypeID { get; set; }

        public string CheckType { get; set; }

        public DateTime CheckDate { get; set; }

        public string Comments { get; set; }

        public string Timezone { get; set; }

        public string Offset { get; set; }

        public GPSLocation GPSCoordinates { get; set; }

        public List<Documents> Documents { get; set; }

        public string Message { get; set; }

    }

    public class GPSLocation
    {
        public string Lat { get; set; }

        public string Long { get; set; }
    }

    public class CheckCallTypes
    {
        public int ID { get; set; }

        public string CheckTypes { get; set; }

        public bool Default { get; set; }
    }


    public enum LocationTypeOption : int { GeoFence = 1, Other = 2 };

    public class DriverTripLocation
    {
        public int ID { get; set; }

        public int DriverTripID { get; set; }

        public LocationTypeOption LocationType { get; set; }

        public string LocationTypeName { get; set; }

        public DateTime LocationDate { get; set; }

        public string Comments { get; set; }

        public GPSLocation GPSCoordinates { get; set; }

        public int NextFence { get; set; }

        public string Message { get; set; }

        public string Timezone { get; set; }

        public string Offset { get; set; }
    }

    public class FBSCheckCall
    {
        public CheckCalls CheckCall { get; set; }

        public Loads Load { get; set; }

        public Driver Driver { get; set; }
    }
}
