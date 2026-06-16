using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{

    public class LoadMessages
    {
        public int ID { get; set; }

        public int DriverTripID { get; set; }

        public int LoadID { get; set; }

        public int TripID { get; set; }

        public MessageType MessageType { get; set; }

        public DateTime MessageDate { get; set; }

        public string Message { get; set; }

        public Messenger MessageFrom { get; set; }

        public int DriverID { get; set; }

        public string DriverName { get; set; }

        public int RepID { get; set; }

        public string CompanyRepName { get; set; }

        public DateTime ViewedDate { get; set; }

        public bool Deleted { get; set; }

        public string ResultMessage { get; set; }

        public GPSLocation GPSCoordinates { get; set; }

        public string Timezone { get; set; }

        public string Offset { get; set; }    }

    public enum MessengerTypeOption : int { Driver = 1, CompanyRep = 2};
    public class Messenger
    {

        public int MessengerID { get; set; }

        public string MessengerName { get; set; }

        public MessengerTypeOption MessengerType { get; set; }

    }

    public enum MessageTypeOption : int { Text = 1, Email = 2, Call = 3};
    public class MessageType
    {
        public MessageTypeOption TypeOption { get; set; }

    }



    public class ResponseMessage
    {
        public string Message { get; set; }
    }


}