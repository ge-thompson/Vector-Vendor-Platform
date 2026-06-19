using System;
using System.Collections.Generic;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A stop on a load's itinerary. Used for origin/destination and intermediate stops.
    /// Adapters use this to populate vendor-specific stop payloads.
    /// </summary>
    public class StopInfo
    {
        /// <summary>1-based stop sequence number in the load's itinerary. Optional.</summary>
        public int? SequenceNumber { get; set; }

        /// <summary>Pickup, Delivery, or Intermediate.</summary>
        public StopRole Role { get; set; }

        /// <summary>Display name of the stop (e.g., "Acme Warehouse #4").</summary>
        public string Name { get; set; }

        public string AddressLine1 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }

        /// <summary>Latitude as string (precision-preserving). Optional.</summary>
        public string Latitude { get; set; }

        /// <summary>Longitude as string (precision-preserving). Optional.</summary>
        public string Longitude { get; set; }

        /// <summary>Free-text notes attached to the stop. Optional.</summary>
        public string Notes { get; set; }

        /// <summary>External stop identifier from the originating system. Optional.</summary>
        public string ExternalStopId { get; set; }

        /// <summary>Scheduled arrival in UTC. Null if not yet scheduled.</summary>
        public DateTime? ScheduledArrivalUtc { get; set; }

        /// <summary>Scheduled departure in UTC. Null if not yet scheduled.</summary>
        public DateTime? ScheduledDepartureUtc { get; set; }

        /// <summary>Reference numbers specific to this stop (PO, BOL, etc.). Optional.</summary>
        public List<ReferenceNumber> References { get; set; }
    }

    /// <summary>Carrier information for a load assignment.</summary>
    public class CarrierInfo
    {
        /// <summary>Standard Carrier Alpha Code (4-char identifier).</summary>
        public string Scac { get; set; }

        public string Name { get; set; }

        /// <summary>FMCSA Motor Carrier number.</summary>
        public string McNumber { get; set; }

        /// <summary>DOT number.</summary>
        public string DotNumber { get; set; }
    }

    /// <summary>Driver information for a load assignment.</summary>
    public class DriverInfo
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }

        /// <summary>Driver-type indicator from the source system (e.g., "company", "owner-operator", "team"). Optional, free text.</summary>
        public string DriverType { get; set; }

        /// <summary>Free-text driver notes/comments propagated from the source system. Optional.</summary>
        public string Comments { get; set; }
    }

    /// <summary>Equipment information for a load assignment.</summary>
    public class EquipmentInfo
    {
        public string TruckNumber { get; set; }
        public string TrailerNumber { get; set; }

        /// <summary>Vehicle Identification Number. Optional.</summary>
        public string Vin { get; set; }

        public string LicensePlate { get; set; }

        /// <summary>Trailer type (e.g., "DRY_VAN", "REEFER", "FLATBED"). Free text; vendor adapters map as needed.</summary>
        public string TrailerType { get; set; }
    }

    /// <summary>
    /// Dispatcher contact information for a load assignment. Optional sub-record on
    /// LoadAssignedEvent so vendors can reach back if they need to coordinate.
    /// </summary>
    public class DispatcherInfo
    {
        /// <summary>Internal dispatcher identifier from the source system. Optional.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>
    /// Shipper information for a load assignment. The shipper is the customer
    /// originating the freight (UNFI, Altium, etc. in Vector's world). Optional sub-record.
    /// </summary>
    public class ShipperInfo
    {
        /// <summary>Shipper identifier (customer code or numeric ID). Optional.</summary>
        public string ShipperId { get; set; }

        /// <summary>Customer-facing reference number for this load (often the shipper's PO/order number).</summary>
        public string ReferenceNumber { get; set; }

        /// <summary>Comma-separated email list the shipper expects notifications on. Optional.</summary>
        public string NotificationEmails { get; set; }
    }

    /// <summary>
    /// A typed reference number (BOL, PO, ShipperRef, etc.).
    /// Adapters typically pass all references through to the vendor without interpretation.
    /// </summary>
    public class ReferenceNumber
    {
        /// <summary>The type code (e.g., "BOL", "PO", "ShipperRef").</summary>
        public string Type { get; set; }

        /// <summary>The reference value.</summary>
        public string Value { get; set; }
    }
}
