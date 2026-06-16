using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class LoadDetails
    {
        public int ID { get; set; }

        public int LoadDetailID { get; set; }

        public int LoadID { get; set; }

        public int LoadStopNumber { get; set; }

        public int? StopType { get; set; }

        public int? TripID { get; set; }

        public int? LoadStopReferenceID { get; set; }

        public int? LoadStopAddressID { get; set; }

        public DateTime? ScheduleDate { get; set; }

        public string ScheduleTime { get; set; }

        public string ScheduleTime2 { get; set; }

        public DateTime? ActualDate { get; set; }

        public string ActualTime { get; set; }

        public string Name { get; set; }

        public string Address1 { get; set; }

        public string Address2 { get; set; }

        public string City { get; set; }

        public string State { get; set; }

        public string Zip { get; set; }

        public string County { get; set; }

        public string Phone { get; set; }

        public string Contact { get; set; }

        public string AlternatePhone { get; set; }

        public string EmailAddress { get; set; }

        public int? Weight { get; set; }

        public string Units { get; set; }

        public int? Pieces { get; set; }

        public string CommodityDesc { get; set; }

        public DateTime? DateCreated { get; set; }

        public int? CreatedByID { get; set; }

        public DateTime? DateModified { get; set; }

        public int? ModifiedByID { get; set; }

        public long ChangeStamp { get; set; }

        public string Lat { get; set; }

        public string Long { get; set; }

        public bool CoordByAddress { get; set; }

        public decimal CoverageAmt { get; set; }

    }

    public class RateConfirmations
    {
        public int ID { get; set; }

        public int LoadID { get; set; }

        public int TripID { get; set; }

        public string CarrierName { get; set; }

        public string CarrierCS { get; set; }

        public string PUCS { get; set; }

        public string PUDate { get; set; }

        public string PUFrom { get; set; }

        public string PUTo { get; set; }

        public string DeliveryCS { get; set; }

        public string DeliveryDate { get; set; }

        public string DeliveryFrom { get; set; }

        public string DeliveryTo { get; set; }

        public string CarrierPay { get; set; }

        public string PUPay { get; set; }

        public string DeliveryPay { get; set; }

        public string LoadPay { get; set; }

        public string TruckPay { get; set; }

        public string Commodity { get; set; }

        public string Weight { get; set; }

        public string Pallets { get; set; }

        public string Equipment { get; set; }

        public string Temp { get; set; }

        public string CarrierContact { get; set; }

        public string VectorRep { get; set; }

        public string Pieces { get; set; }

        public string Faxnumber { get; set; }

        public string Greeting { get; set; }

        public DateTime RateConfirmDate { get; set; }

        public int RateConfirmID { get; set; }

        public string MiscPay { get; set; }

        public bool UsePortal { get; set; }

        public string RepEmail { get; set; }

        public string CarrierEmail { get; set; }

        public int CarrierID { get; set; }

        public string Reason { get; set; }

        public string ConfirmType { get; set; }

        public string AssistPay { get; set; }

        public string LayoverPay { get; set; }

        public string RCVersion { get; set; }

        public string PUCS2 { get; set; }

        public string PUDate2 { get; set; }

        public string PUFrom2 { get; set; }

        public string PUTo2 { get; set; }

        public string PUCS3 { get; set; }

        public string PUDate3 { get; set; }

        public string PUFrom3 { get; set; }

        public string PUTo3 { get; set; }

        public string DeliveryCS2 { get; set; }

        public string DeliveryDate2 { get; set; }

        public string DeliveryFrom2 { get; set; }

        public string DeliveryTo2 { get; set; }

        public string DeliveryCS3 { get; set; }

        public string DeliveryDate3 { get; set; }

        public string DeliveryFrom3 { get; set; }

        public string DeliveryTo3 { get; set; }

        public string Instructions { get; set; }

    }

    public class RateConfirmationDetail
    {
        public int ID { get; set; }
        public int RateConfirmDetailID { get; set; }

        public int RateConfirmID { get; set; }

        public DateTime? ViewedDate { get; set; }

        public DateTime? ApprovedDate { get; set; }

        public string ApprovedBy { get; set; }

        public string Signature { get; set; }

        public bool ComCheck { get; set; }

        public bool RegMail { get; set; }

        public bool NextDay { get; set; }

        public string QuickPayName { get; set; }

        public string QuickPayTitle { get; set; }

        public int? ViewedBrowserID { get; set; }

        public int? ApprovedBrowserID { get; set; }

        public string Driver { get; set; }

        public string CellPhone { get; set; }

        public string TruckNumber { get; set; }

        public string TrailerNumber { get; set; }

    }

}