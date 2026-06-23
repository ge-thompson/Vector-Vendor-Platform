using System.Collections.Generic;

namespace FourKitesIntegration.Core.Models.Webhooks
{
    /// <summary>
    /// Base fields present on all (or nearly all) Shipments callback payloads.
    /// FourKites uses PascalCase for webhook field names. Newtonsoft maps automatically
    /// when using the default contract resolver, but our outbound serializer is camelCase —
    /// for webhooks specifically, use FourKitesWebhookJson (defined below) for deserialization.
    /// </summary>
    public abstract class WebhookPayload
    {
        public string MessageType { get; set; }
        public long? FourKitesLoadId { get; set; }
        public string LoadNumber { get; set; }
        public string ProNumber { get; set; }
        public string Shipper { get; set; }
        public string Scac { get; set; }
        public List<string> ReferenceNumbers { get; set; }
        public List<string> Tags { get; set; }
        public string Timestamp { get; set; }
        public string Timezone { get; set; }
        public string TimezoneShortName { get; set; }
        public int? TimezoneOffset { get; set; }
        public string EncryptedAccessToken { get; set; }
        public string EncryptedUrl { get; set; }
        public string DataSource { get; set; }
    }

    public class LoadCreationCallback : WebhookPayload
    {
        public bool? IsSuccess { get; set; }
        public List<string> Errors { get; set; }
        public List<string> DeliveryNumbers { get; set; }
        public string MasterTrackingNumber { get; set; }
    }

    public class LoadUpdateCallback : WebhookPayload
    {
        public bool? IsSuccess { get; set; }
        public List<string> Errors { get; set; }
        public List<string> UpdatedFields { get; set; }
        public string CarrierServiceType { get; set; }
    }

    public class LoadDeletedCallback : WebhookPayload { }

    public class LoadExpiredCallback : WebhookPayload
    {
        public string TotalDistance { get; set; }
        public string Carrier { get; set; }
        public string Status { get; set; }
        public string TerminationTime { get; set; }
    }

    public class StopCallback : WebhookPayload
    {
        public string StopName { get; set; }
        public string StopReferenceId { get; set; }
        public int? StopSequence { get; set; }
        public string StopStatus { get; set; }
        public string StopType { get; set; }                // "pickup" | "delivery" | "transfer" | etc.
        public string StopUnlocode { get; set; }
        public List<string> StopReferenceNumbers { get; set; }
        public string DeviceID { get; set; }
        public double? OdometerReading { get; set; }
    }

    public class StopArrivalCallback : StopCallback { }

    public class StopDepartureCallback : StopCallback
    {
        public bool? AutoDeparture { get; set; }
    }

    /// <summary>
    /// STOP_AUTO_COMPLETED / STOP_AUTO_DELIVERED / STOP_AUTO_PICKED_UP — payloads are the same shape.
    /// </summary>
    public class StopCompletionCallback : StopCallback
    {
        public string DepartedAt { get; set; }
    }

    public class StopEtaUpdateCallback : StopCallback
    {
        public string CarrierETA { get; set; }
        public string TerminalName { get; set; }
        public string VesselName { get; set; }
        public string VoyageNumber { get; set; }
        public string BillofLading { get; set; }
        public string BookingNumber { get; set; }
        public string ContainerNumber { get; set; }
        public string ContainerType { get; set; }
    }

    public class CarrierEtaUpdateCallback : StopCallback
    {
        public string Carrier { get; set; }
    }

    public class StopAppointmentRescheduledCallback : WebhookPayload
    {
        public string StopName { get; set; }
        public int? StopSequence { get; set; }
        public string StopType { get; set; }
        public long? StopId { get; set; }
        public string NewAppointmentTime { get; set; }
        public string NewEarliestAppointmentTime { get; set; }
        public string NewLatestAppointmentTime { get; set; }
        public string OldAppointmentTime { get; set; }
        public string OldEarliestAppointmentTime { get; set; }
        public string OldLatestAppointmentTime { get; set; }
        public string RescheduledAt { get; set; }
    }

    /// <summary>Well-known MessageType values dispatched to webhook handlers.</summary>
    public static class WebhookMessageTypes
    {
        public const string LoadCreation = "LOAD_CREATION";
        public const string LoadUpdate = "LOAD_UPDATE";
        public const string LoadDeleted = "LOAD_DELETED";
        public const string LoadExpired = "LOAD_EXPIRED";
        public const string StopCreated = "STOP_CREATED";
        public const string StopUpdated = "STOP_UPDATED";
        public const string StopDeleted = "STOP_DELETED";
        public const string StopArrival = "STOP_ARRIVAL";
        public const string StopDeparture = "STOP_DEPARTURE";
        public const string StopAutoCompleted = "STOP_AUTO_COMPLETED";
        public const string StopAutoDelivered = "STOP_AUTO_DELIVERED";
        public const string StopAutoPickedUp = "STOP_AUTO_PICKED_UP";
        public const string StopEtaUpdated = "STOP_ETA_UPDATED";
        public const string CarrierEtaUpdated = "CARRIER_ETA_UPDATED";
        public const string StopAppointmentRescheduled = "STOP_APPOINTMENT_RESCHEDULED";
    }
}
