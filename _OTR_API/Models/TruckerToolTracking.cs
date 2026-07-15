using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.TruckerToolsTracking.Models
{


    public class Load
    {
        public int partnerId { get; set; }
        public string loadTrackExternalId { get; set; }
        public string accountId { get; set; }
        public string loadNumber { get; set; }
        public string dispatcherId { get; set; }
        public string dispatcherEmail { get; set; }
        public string dispatcherPhoneNumber { get; set; }
        public string textmessage { get; set; }
        public string loadType { get; set; }
        public string trailerType { get; set; }
        public string revenueType { get; set; }
        public string autoStartTime { get; set; }
        public string driverCell { get; set; }
        public string trailerNumber { get; set; }
        public string truckNumber { get; set; }
        public string driverName { get; set; }
        public string driverType { get; set; }
        public string driverComments { get; set; }
        public string loadNotes { get; set; }
        public bool isTeamLoad { get; set; }
        public string carrierDispatcherEmail { get; set; }
        public Carrier carrier { get; set; }
        public Broker broker { get; set; }
        public Shipper shipper { get; set; }
        public List<Metadata> metadata { get; set; }
        public List<Stop> stops { get; set; }

        [JsonIgnore]
        public int ID { get; set; }

        public int VectorID { get; set; }
        public string Message { get; set; }

        public int BillToID { get; set; }
        public bool ShouldSerializeBillToID() => false;
    }

    public class Action
    {
        public string name { get; set; }
        public bool driverInput { get; set; }
       // public List<Option> options { get; set; }
        public bool required { get; set; }
        public bool isLastAction { get; set; }
        public string item { get; set; }
        public string id { get; set; }

        [JsonIgnore]
        public int ActionID { get; set; }
        public int TrackingStopID { get; set; }
    }

    public class Broker
    {
        public string companyName { get; set; }
        public string docketNumber { get; set; }
        public string contactName { get; set; }
        public string contactPhone { get; set; }
        public string contactPhoneExt { get; set; }
        public string contactEmail { get; set; }

        [JsonIgnore]
        public int ID { get; set; }
        public int TrackingID { get; set; }
    }

    public class Carrier
    {
        public string companyName { get; set; }
        public string docketNumber { get; set; }
        public string contactName { get; set; }
        public string contactPhone { get; set; }
        public string contactPhoneExt { get; set; }
        public string contactEmail { get; set; }

        [JsonIgnore]
        public int ID { get; set; }
        public int TrackingID { get; set; }
    }

    public class Metadata
    {
        public string name { get; set; }
        public string value { get; set; }

        [JsonIgnore]
        public int ID { get; set; }
        public int TrackingStopID { get; set; }
        public int TrackingID { get; set; }
    }

    public class Option
    {
        public string name { get; set; }
    }

    public class Shipper
    {
        public string loadNumber { get; set; }
        public string name { get; set; }
        public string shipperId { get; set; }
        public string emails { get; set; }
        public int emailInterval { get; set; }
        public string referenceNumber { get; set; }

        public int ID { get; set; }
        public bool ShouldSerializeID() => false;

        public int TrackingID { get; set; }
        public bool ShouldSerializeTrackingID() => false;
    }



    public class Stop
    {
        public int orderNumber { get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string zipcode { get; set; }
        public decimal lat { get; set; }
        public decimal lon { get; set; }
        public string datetime { get; set; }
        public string datetimeExit { get; set; }
        public int geofenceRadius { get; set; }
        public string notes { get; set; }
        public string stopExternalId { get; set; }
        public List<Metadata> metadata { get; set; }
        public List<Action> actions { get; set; }

        [JsonIgnore]
        public int ID { get; set; }
        public int TrackingID { get; set; }
        public int loadNumber { get; set; }
    }


    public class TrackingResponse
    {

        public Response response { get; set; }
    }

    public class Response
    { 
        //101 - Account not found
        //104 - Partner not found
        //105 – Load Track not found
        //301 - Invalid parameters
        //401 - Can’t update Load Track as it is already started
        public int ID { get; set; }
        public int TrackingID { get; set; }
        public int loadID { get; set; }
        public DateTime ResponseDate { get; set; }
        public bool status { get; set; }
        public string timeStamp { get; set; }
        public string mapLink { get; set; }
        public string carrierLink { get; set; }
        public string shipperLink { get; set; }
        public string statusPageLink { get; set; }
        public string detailsLink { get; set; }
        public string detailsLinkNoAuth { get; set; }
        public string trackingMethod { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public string Message { get; set; }

    //"trackingMethod": "APP",
    //"mapLink": "https://loadtracking.truckertools.com/us/a1dv7562d62cd8b4195",
    //"statusPageLink": "https://loadtracking.truckertools.com/us/6ntf0j62d62cd8b528e",
    //"detailsLink": "https://loadtracking.truckertools.com/us/c08wq762d62cd8b056d",
    //"detailsLinkNoAuth": "https://loadtracking.truckertools.com/us/0kt2k062d62cd8b14d7",
    //"carrierLink": "https://loadtracking.truckertools.com/us/pghws862d62cd8b232d",
    //"shipperLink": "https://loadtracking.truckertools.com/us/m7na2v62d62cd8b3010"
  }





    public class StatusUpdate
    {
        public int ID { get; set; }
        public int partnerid { get; set; }
        public string accountid { get; set; }
        public int loadTrackExternalId { get; set; }
        public string ltExternalId { get; set; }
        public string driverPhone { get; set; }
        public string loadNumber { get; set; }
        public string eventType { get; set; }
        public DateTime StatusDate { get; set; }
        public Location latestLocation { get; set; }
        public Status latestStatus { get; set; }
        public List<Location> locations { get; set; }
        public Status status { get; set; }
        public string Message { get; set; }
    }

    public class Status
    {
        public int ID { get; set; }
        public string name { get; set; }
        public string code { get; set; }
        public string timeStamp { get; set; }
        public string timeStampSec { get; set; }
        public Location location { get; set; }
        public int associatedId { get; set; }
        public int type { get; set; }
    }

    public class Location
    {
        public int ID { get; set; }

        public string lat { get; set; }
        public string lon { get; set; }
        public string accuracy { get; set; }
        public string timeStampSec { get; set; }
        public string timeStamp { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public int associatedId { get; set; }
        public int type { get; set; }
    }

    public class StatusResponse
    {
        //100 - Invalid account
        //200 - An internal system error occurred
        //301 - The external load Id is required
        //302 - The latitude is required
        //303 - The longitude is required
        //304 - The device time stamp is required

        public int ID { get; set; }
        public int StatusID { get; set; }
        public string loadID { get; set; }
        public bool status { get; set; }
        public string timeStamp { get; set; }
        public int errorCode { get; set; }
        public string errorMessage { get; set; }
        public string Message { get; set; }
    }


}
