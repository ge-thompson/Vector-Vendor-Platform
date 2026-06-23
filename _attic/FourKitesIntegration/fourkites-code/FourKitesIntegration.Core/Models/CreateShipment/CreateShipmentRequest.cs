using System.Collections.Generic;

namespace FourKitesIntegration.Core.Models.CreateShipment
{
    /// <summary>
    /// POST https://api.fourkites.com/api/v1/tracking
    /// Top-level request envelope for Create Shipment.
    /// </summary>
    public class CreateShipmentRequest
    {
        public AdditionalData AdditionalData { get; set; }   // optional mode hints
        public LoadCreatePayload Load { get; set; }          // REQUIRED — all load info nested here
    }

    public class AdditionalData
    {
        public ModeDetails ModeDetails { get; set; }
    }

    public class ModeDetails
    {
        public string ShipperModes { get; set; }   // "TL" | "LTL" | "Ocean" | "Rail" | "Parcel" | "Air"
        public string CarrierModes { get; set; }
    }

    /// <summary>
    /// The load object that gets created in FourKites. Only the fields most relevant for TL brokers are
    /// surfaced here; the full schema has many more (ocean container/voyage, air flight numbers, etc.) —
    /// add them as nullable properties if/when needed.
    /// </summary>
    public class LoadCreatePayload
    {
        public string LoadNumber { get; set; }                // REQUIRED — primary BOL or load ref
        public string DisplayLoadNumber { get; set; }         // optional alias shown on load card
        public string ProNumber { get; set; }                 // PRO if assigned at creation
        public string Carrier { get; set; }                   // REQUIRED — carrier SCAC
        public string CarrierName { get; set; }
        public string CarrierPermalink { get; set; }          // FourKites carrier ID; leave blank if unknown
        public List<string> HaulType { get; set; }            // e.g. ["inbound_load"]
        public string Priority { get; set; }                  // "normal" | "high" | "hot"
        public string BillOfLading { get; set; }
        public string BookingNumber { get; set; }
        public string MasterSequenceNumber { get; set; }
        public string MasterReferenceNumber { get; set; }
        public string SimultaneousTrackingReferenceNumber { get; set; }
        public int? SimultaneousTrackingSequenceNumber { get; set; }
        public string MasterTrackingNumber { get; set; }
        public bool? RelayLoad { get; set; }
        public List<string> ReferenceNumbers { get; set; }    // shipper PO/refs that become searchable
        public string DeliveryNumbers { get; set; }           // comma-separated string per docs
        public List<string> Tags { get; set; }
        public List<Stop> Stops { get; set; }                 // REQUIRED — at least one pickup + one delivery
        public TrackingInfo TrackingInfo { get; set; }
        public ShipmentCost Cost { get; set; }
    }

    public class Stop
    {
        public string StopId { get; set; }
        public string StopType { get; set; }                  // "pickup" | "delivery" | "transfer" | "originAirport" | "hubAirport" | "destinationAirport"
        public int? Sequence { get; set; }
        public string StopReferenceId { get; set; }           // YOUR stable ID — used to match stops in webhooks
        public string Name { get; set; }
        public string AddressLine1 { get; set; }
        public string AddressLine2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }                     // 2-char US/CA, 3-char MX
        public string PostalCode { get; set; }
        public string Country { get; set; }                   // ISO 3166-1 alpha-2
        public string Latitude { get; set; }                  // decimal degrees as string
        public string Longitude { get; set; }
        public string ExternalAddressId { get; set; }
        public string ShipTo { get; set; }
        public string EarliestAppointmentTime { get; set; }   // pair these together
        public string LatestAppointmentTime { get; set; }
        public string EarliestWantTime { get; set; }
        public string LatestWantTime { get; set; }
        public string WantTime { get; set; }
        public string LoadingType { get; set; }               // "drop" | "live"
        public int? UnloadTimeInMinutes { get; set; }
        public List<string> ReferenceNumbers { get; set; }
        public Customer Customer { get; set; }
        public string TrackingNumber { get; set; }
    }

    public class Customer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> PoNumbers { get; set; }
        public List<CustomerPoField> CustomerPoFields { get; set; }
        public PointOfContact PointOfContact { get; set; }
    }

    public class CustomerPoField
    {
        public string DeliveryNumber { get; set; }
        public string DeliveryReference { get; set; }
        public string OrderReference { get; set; }
    }

    public class PointOfContact
    {
        public bool? Alert { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }                     // E.164 e.g. "+19999999999"
    }

    public class TrackingInfo
    {
        public string DeviceId { get; set; }
        public string DriverId { get; set; }
        public string DriverPhone { get; set; }               // E.164 format; required for CarrierLink mobile tracking
        public string Mawb { get; set; }                      // air
        public string TrailerNumber { get; set; }
        public string TruckNumber { get; set; }
    }

    public class ShipmentCost
    {
        public decimal? ProductCost { get; set; }
        public decimal? ExtendedCost { get; set; }
        public decimal? FreightCost { get; set; }
        public decimal? AdditionalCost { get; set; }
        public decimal? TariffCost { get; set; }
        public decimal? LandedCost { get; set; }
        public string CountryOfOrigin { get; set; }
        public string Currency { get; set; }
        public decimal? FourkitesCalculatedTariffCost { get; set; }
    }

    /// <summary>Well-known stop type values.</summary>
    public static class StopTypes
    {
        public const string Pickup = "pickup";
        public const string Delivery = "delivery";
        public const string Transfer = "transfer";
        public const string OriginAirport = "originAirport";
        public const string HubAirport = "hubAirport";
        public const string DestinationAirport = "destinationAirport";
    }

    /// <summary>Well-known priority values.</summary>
    public static class LoadPriority
    {
        public const string Normal = "normal";
        public const string High = "high";
        public const string Hot = "hot";
    }
}
