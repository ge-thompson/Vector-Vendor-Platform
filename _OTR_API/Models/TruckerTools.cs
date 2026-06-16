using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.TruckerTools.Models
{
    public class Setting
    {
        public int ID { get; set; }
        public string Description { get; set; }
        public string Detail { get; set; }
        public string Notes { get; set; }
        public DateTime LastChanged { get; set; }
    }



    #region Carrier

    public class CarrierSync
    {
        public string integrationId { get; set; }
        public string accountId { get; set; }
        public List<Carrier> carriers { get; set; }
    }

    public class Carrier
    {
        [JsonProperty("carrier_name")]
        public string carrier_name { get; set; }

        [JsonProperty("mc")]
        public string mc { get; set; }

        [JsonProperty("dot")]
        public string dot { get; set; }

        [JsonProperty("scac")]
        public string scac { get; set; }

        [JsonProperty("non_usa_mc")]
        public bool non_usa_mc { get; set; }

        [JsonProperty("external_id")]
        public string external_id { get; set; }

        [JsonProperty("contact_name")]
        public string contact_name { get; set; }

        [JsonProperty("contact_email")]
        public string contact_email { get; set; }

        [JsonProperty("contact_phone")]
        public string contact_phone { get; set; }

        [JsonProperty("in_network")]
        public bool in_network { get; set; }

        [JsonProperty("rejected")]
        public bool rejected { get; set; }

        [JsonProperty("carrierLevel")]
        public int carrierLevel { get; set; }

        [JsonProperty("book_it_now")]
        public bool book_it_now { get; set; }

        [JsonProperty("truck_numbers_range")]
        public int truck_numbers_range { get; set; }

        [JsonProperty("truck_numbers")]
        public int truck_numbers { get; set; }

        [JsonIgnore]
        public int ID { get; set; }
        public string NumberofTrucks { get; set; }
        public DateTime DateAdded { get; set; }
        public string LastEvent { get; set; }
        public DateTime LastUpdate { get; set; }
        public int VectorID { get; set; }
        public string Message { get; set; }
    }



    public class CarrierResponse
    {
        public int ID { get; set; }
        public int CarrierID { get; set; }
        public DateTime ResponseDate { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; }

        public List<CarrierResponseDetail> Details { get; set; }
    }

    public class CarrierResponseDetail
    {
        public int ID { get; set; }
        public int CarrierResponseID { get; set; }
        public bool Status { get; set; }
        public string Company { get; set; }
        public string carrier_ext_id { get; set; }
        public string Message { get; set; }
    }

    #endregion



    #region "Load"


    public class LoadSync
    {
        public string integrationId { get; set; }
        public string accountId { get; set; }
        public List<Load> loads { get; set; }
    }

    public class Extra
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonIgnore]
        public int ID { get; set; }
        public int LoadID { get; set; }
        public int AssociatedID { get; set; }
        public enum ExtraType { Load = 1, Pickup = 2, Delivery = 3, AdditionalStops = 4 }
        public ExtraType Type { get; set; }

    }

    public class Contact
    {
        public int ID { get; set; }
        public int LoadID { get; set; }
        public enum ContactType { Load = 1, Operation = 2, Sales = 3, Broker = 4, Driver = 5, Dispatcher = 6, Shipper = 7 }
        public ContactType Type { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Extension { get; set; }
        public string Email { get; set; }
        public string Team { get; set; }
        public string MemberID { get; set; }
        public string mc { get; set; }
        public string dot { get; set; }
        public string DeviceID { get; set; }
        public string scac { get; set; }
        public string NumberofTrucks { get; set; }
        public int VectorID { get; set; }
    }

    public class Stop
    {
        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("city")]
        public string City { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("postalCode")]
        public string PostalCode { get; set; }

        [JsonProperty("latitude")]
        public string Latitude { get; set; }

        [JsonProperty("longitude")]
        public string Longitude { get; set; }

        [JsonProperty("timeZone")]
        public string TimeZone { get; set; }

        [JsonProperty("sequence")]
        public int Sequence { get; set; }

        [JsonProperty("stopExternalId")]
        public string StopExternalID { get; set; }

        [JsonProperty("scheduledAtEarlyDateTime")]
        public string ScheduledAtEarlyDateTime { get; set; }

        [JsonProperty("scheduledAtLateDateTime")]
        public string ScheduledAtLateDateTime { get; set; }

        [JsonProperty("appointmentRequired")]
        public bool AppointmentRequired { get; set; }

        [JsonProperty("appointmentConfirmed")]
        public bool AppointmentConfirmed { get; set; }

        [JsonProperty("Extras")]
        public List<Extra> Extras { get; set; }

        [JsonIgnore]
        public int LoadID { get; set; }
        public enum StopType { Pickup = 1, Delivery = 2, AdditionalStops = 3, None = 4 }
        public StopType Type { get; set; }
        public int ID { get; set; }
        public int VectorID { get; set; }
    }

    public class Pay
    {
        public int ID { get; set; }
        public int LoadID { get; set; }
        public enum PayType { Revenue = 1, CarrierPay = 2, TargetPay = 3 }
        public PayType Type { get; set; }
        public string Freight { get; set; }
        public string Extra { get; set; }
        public string Total { get; set; }
        public string MinimumPay { get; set; }
        public string MaximumPay { get; set; }
    }

    public class LoadStops
    {
        public Pickup pickUp { get; set; }
        public Delivery delivery { get; set; }
        public List<AdditionalStop> additionalstops { get; set; }
    }

    public class LoadContacts
    {
        public LoadContact loadcontact { get; set; }

        public SalesPerson salesperson { get; set; }

        public OperationUser operationuser { get; set; }

        public LoadCarrier carrier { get; set; }

        public Driver driver { get; set; }

        public Dispatcher dispatcher { get; set; }

        public Shipper shipper { get; set; }

        public Broker broker { get; set; }
    }

    public class LoadPays
    {
        public Revenue revenue { get; set; }

        public CarrierPay carrierpay { get; set; }

        public TargetPay targetpay { get; set; }
    }


    public class Load
    {
        public string status { get; set; }
        public string equipmentType { get; set; }
        public string loadNumber { get; set; }
        public string externalId { get; set; }
        public Pickup pickup { get; set; }
        public Delivery delivery { get; set; }
        public string loadType { get; set; }
        public LoadContact loadContact { get; set; }
        public OperationUser operationUser { get; set; }
        public SalesPerson salesPerson { get; set; }
        public Revenue revenue { get; set; }
        public CarrierPay carrierPay { get; set; }
        public TargetPay targetPay { get; set; }
        public int trucksCount { get; set; }
        public string length { get; set; }
        public string weight { get; set; }
        public string quantity { get; set; }
        public string rate { get; set; }
        public string billToId { get; set; }
        public string orderType { get; set; }
        public string temperatureMinimum { get; set; }
        public string temperatureMaximum { get; set; }
        public string commodityId { get; set; }
        public bool hazmat { get; set; }
        public bool highValue { get; set; }
        public bool teamsRequired { get; set; }
        public string comments { get; set; }
        public int numberOfAdditionalStops { get; set; }
        public List<AdditionalStop> additionalStops { get; set; }
        public Broker broker { get; set; }
        public string shipperLoadNumber { get; set; }
        public LoadCarrier carrier { get; set; }
        public Driver driver { get; set; }
        public Dispatcher dispatcher { get; set; }
        public Shipper shipper { get; set; }
        public string tractorNumber { get; set; }
        public string trailerNumber { get; set; }
        public bool publishToCarrier { get; set; }
        public string bookItNowPrice { get; set; }
        public decimal totalMiles { get; set; }
        public decimal ratePerMile { get; set; }
        public decimal ratePerMileFuel { get; set; }
        public bool triggerTracking { get; set; }
        public List<Extra> extras { get; set; }


        [JsonIgnore]
        public int ID { get; set; }
        public int VectorID { get; set; }
        public int VectorCarrierID { get; set; }
        public string Message { get; set; }
    }


    public class Pickup : Stop
    {

    }

    public class Delivery : Stop
    {

    }

    public class AdditionalStop : Stop
    {

    }



    public class LoadContact
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("contactPhone")]
        public string ContactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string ContactEmail { get; set; }

        [JsonProperty("phoneExtension")]
        public string PhoneExtension { get; set; }
    }

    public class OperationUser
    {
        [JsonProperty("id")]
        public string ID { get; set; }

        [JsonProperty("team")]
        public string Team { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("contactPhone")]
        public string ContactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string ContactEmail { get; set; }

        [JsonProperty("phoneExtension")]
        public string PhoneExtension { get; set; }

        [JsonIgnore]
        public int VectorID { get; set; }
    }

    public class SalesPerson
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("contactPhone")]
        public string ContactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string ContactEmail { get; set; }

        [JsonProperty("phoneExtension")]
        public string PhoneExtension { get; set; }

        [JsonIgnore]
        public int VectorID { get; set; }
    }

    public class Broker
    {
        [JsonProperty("companyName")]
        public string CompanyName { get; set; }

        [JsonProperty("mc")]
        public string mc { get; set; }

        [JsonProperty("dotNumber")]
        public string dot { get; set; }

        [JsonProperty("contactPhone")]
        public string ContactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string ContactEmail { get; set; }
    }

    public class Driver
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("phone")]
        public string Phone { get; set; }

        [JsonProperty("deviceId")]
        public string DeviceID { get; set; }
    }

    public class Dispatcher
    {
        [JsonProperty("id")]
        public string ID { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("contactPhone")]
        public string ContactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string ContactEmail { get; set; }
    }

    public class Shipper
    {
        [JsonProperty("companyName")]
        public string CompanyName { get; set; }

        [JsonProperty("contactPhone")]
        public string ContactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string ContactEmail { get; set; }

        [JsonProperty("ShipperId")]
        public string ShipperId { get; set; }

        [JsonIgnore]
        public int VectorID { get; set; }
    }

    public class LoadCarrier
    {
        [JsonProperty("companyName")]
        public string companyName { get; set; }

        [JsonProperty("mc")]
        public string mc { get; set; }

        [JsonProperty("contactPhone")]
        public string contactPhone { get; set; }

        [JsonProperty("contactEmail")]
        public string contactEmail { get; set; }

        [JsonProperty("dotNumber")]
        public string dotNumber { get; set; }

        [JsonProperty("scac")]
        public string scac { get; set; }

        [JsonProperty("numberOfTrucks")]
        public string numberOfTrucks { get; set; }

        [JsonIgnore]
        public int VectorID { get; set; }
    }


    public class Revenue
    {
        [JsonProperty("freight")]
        public string Freight { get; set; }

        [JsonProperty("extra")]
        public string Extra { get; set; }

        [JsonProperty("total")]
        public string Total { get; set; }
    }

    public class CarrierPay
    {
        [JsonProperty("freight")]
        public string Freight { get; set; }

        [JsonProperty("extra")]
        public string Extra { get; set; }

        [JsonProperty("total")]
        public string Total { get; set; }
    }

    public class TargetPay
    {
        [JsonProperty("minimumPay")]
        public string MinimumPay { get; set; }

        [JsonProperty("maximumPay")]
        public string MaximumPay { get; set; }
    }



    public class LoadResponse
    {
        public int ID { get; set; }
        public int LoadID { get; set; }
        public DateTime ResponseDate { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; }
        public string[] loadNumbers { get; set; }
        public List<LoadResponseDetail> Details { get; set; }
    }

    public class LoadResponseDetail
    {
        public int ID { get; set; }

        public int LoadResponseID { get; set; }

        public string LoadNumber { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; }
    }

    #endregion


    public class RAW
    {
        public int ID { get; set; }
        public string Json { get; set; }
        public DateTime EntryDate { get; set; }
        public string Filter { get; set; }
        public string Value { get; set; }

        public string key { get; set; }
    }



    public class RateConfirm
    {
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
        public List<LoadDetail> LoadDetails { get; set; }

        public int ID { get; set; }
        public int VectorID { get; set; }
        public int VectorCarrierID { get; set; }
        public string Message { get; set; }
    }

    public class RateConfirmResponse
    {
        public int ID { get; set; }
        public int RateConfirmID { get; set; }
        public DateTime ResponseDate { get; set; }
        public bool Status { get; set; }
        public string Message { get; set; }
    }

    public class LoadDetail
    {
        public int LoadDetailID { get; set; }
        public int LoadID { get; set; }
        public int LoadStopNumber { get; set; }
        public int StopType { get; set; }
        public int TripID { get; set; }
        public int LoadStopReferenceID { get; set; }
        public int LoadStopAddressID { get; set; }
        public DateTime ScheduleDate { get; set; }
        public string ScheduleTime { get; set; }
        public string ScheduleTime2 { get; set; }
        public DateTime ActualDate { get; set; }
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
        public int Weight { get; set; }
        public string Units { get; set; }
        public int Pieces { get; set; }
        public string CommodityDesc { get; set; }
        public DateTime DateCreated { get; set; }
        public int CreatedByID { get; set; }
        public DateTime DateModified { get; set; }
        public int ModifiedByID { get; set; }
        public int Lat { get; set; }
        public int Long { get; set; }
        public bool CoordByAddress { get; set; }
        public decimal CoverageAmt { get; set; }

        public List<LoadDetailRefNumber> LoadDetailRefNumbers { get; set; }
    }

    public class LoadDetailRefNumber
    {
        public int ReferenceNumberID { get; set; }
        public int LoadDetailId { get; set; }
        public int LoadId { get; set; }
        public string ReferenceNumber { get; set; }
        public int ReferenceNumberTypeID { get; set; }
        public DateTime DateAdded { get; set; }
        public int AddedByID { get; set; }
        public DateTime DateModified { get; set; }
        public int ModifiedByID { get; set; }
    }

}