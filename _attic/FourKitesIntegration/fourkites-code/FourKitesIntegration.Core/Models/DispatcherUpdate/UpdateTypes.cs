namespace FourKitesIntegration.Core.Models.DispatcherUpdate
{
    /// <summary>
    /// GPS location update. One per load per request (not an array).
    /// Latitude/longitude are STRINGS in decimal degrees format.
    /// </summary>
    public class LocationUpdate
    {
        public string Latitude { get; set; }          // REQUIRED — decimal degrees as string e.g. "41.898274"
        public string Longitude { get; set; }         // REQUIRED — decimal degrees as string
        public string LocatedAt { get; set; }         // REQUIRED — ISO 8601 timestamp; defaults to "now" if omitted
        public string City { get; set; }              // OPTIONAL — for display
        public string State { get; set; }             // OPTIONAL — 2-letter US/CA, 3-letter MX
        public string DeliveredAt { get; set; }       // CAUTION — sending marks load DELIVERED and locks it from further updates
        public string TimeZone { get; set; }          // OPTIONAL — defaults to UTC
    }

    /// <summary>
    /// Status event update. One per load per request.
    /// statusCode is free-form; EDI 214 X12 codes (X1, AF, D1, X3, etc.) are the de facto standard.
    /// </summary>
    public class EventUpdate
    {
        public string StatusCode { get; set; }        // e.g. "X1", "AF", "D1" (see Edi214Mapper)
        public string StatusDescription { get; set; } // human-readable
        public string StatusReasonCode { get; set; }  // EDI delay reason code or free-form
        public string EventTimeStamp { get; set; }    // ISO 8601
        public string City { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public bool? Delivered { get; set; }          // true marks load delivered (lighter touch than locationUpdate.deliveredAt)
        public string DeliveredAt { get; set; }       // when delivered = true
        public string PickupTime { get; set; }        // LTL only
        public string DeliveryTime { get; set; }      // LTL only
        public string TimeZone { get; set; }
    }

    /// <summary>
    /// Stop-level update — appointment changes and stop-level events.
    /// Identify the stop with ONE of: stopSequence, stopReferenceId, postalCode.
    /// </summary>
    public class StopUpdate
    {
        public string StopSequence { get; set; }              // pick one of these three
        public string StopReferenceId { get; set; }
        public string PostalCode { get; set; }

        public string AppointmentTime { get; set; }           // single fixed time
        public string EarliestAppointmentTime { get; set; }   // OR a window — both required together
        public string LatestAppointmentTime { get; set; }

        public string StatusCode { get; set; }                // status applied to this stop specifically
        public string StatusDescription { get; set; }
        public string EventTimeStamp { get; set; }
        public string TimeZone { get; set; }                  // for all datetimes in this entry
    }

    /// <summary>
    /// Assign/update the carrier, driver, equipment for a load.
    /// All fields optional — send what you have. driverPhone format: +CCXXXXXXXXXX
    /// </summary>
    public class AssignmentUpdate
    {
        public string OperatingCarrierScac { get; set; }
        public string TruckNumber { get; set; }
        public string TrailerNumber { get; set; }
        public string DeviceId { get; set; }
        public string DriverPhone { get; set; }
        public string DriverName { get; set; }
        public string DriverLicenseNumber { get; set; }
        public string RailEquipmentInitials { get; set; }   // max 4 chars
        public string RailEquipmentNumber { get; set; }     // max 6 chars
        public string WagonNumber { get; set; }
        public string OceanContainer { get; set; }           // mandatory for ocean event updates
    }

    /// <summary>
    /// Load-level metadata: tags, weight, PRO, freight charges, etc.
    /// stopTracking=true halts tracking on the load.
    /// </summary>
    public class LoadInfoUpdate
    {
        public System.Collections.Generic.List<string> Tags { get; set; }
        public string UpdatableProNumber { get; set; }
        public bool? BrokeredLoad { get; set; }
        public bool? RelayLoad { get; set; }
        public string SimultaneousTrackingReferenceNumber { get; set; }
        public string StartPoint { get; set; }
        public string EndPoint { get; set; }
        public string StartTime { get; set; }
        public string ForceStart { get; set; }
        public bool? StopTracking { get; set; }
        public string Weight { get; set; }
        public string WeightUnit { get; set; }              // "LB" or "KG"
        public string FreightCharges { get; set; }
        public string FreightChargeUnit { get; set; }       // currency code like "USD"
        public string Quantity { get; set; }
    }

    /// <summary>
    /// Carrier-reported ETA for the load (overall / final-delivery ETA).
    /// Triggers CARRIER_ETA_UPDATED webhook on 15+ minute shifts.
    /// </summary>
    public class EtaUpdate
    {
        /// <summary>Send this when you have a specific arrival time.</summary>
        public string EstimatedArrivalAtDestination { get; set; }

        /// <summary>OR send this when you have only a date.</summary>
        public string EstimatedArrivalAtDestinationDateOnly { get; set; }

        public string TimeZone { get; set; }
    }

    /// <summary>
    /// Temperature reading for reefer loads. One per load per request.
    /// IMPORTANT: temperatureUnit defaults to "F" Fahrenheit when omitted!
    /// </summary>
    public class TemperatureUpdate
    {
        public string Temperature { get; set; }                 // REQUIRED — value as string e.g. "38"
        public string SensorName { get; set; }                  // optional identifier
        public string TemperatureUnit { get; set; }             // "C" or "F"; DEFAULTS TO F IF OMITTED
        public string TemperatureCheckCallTime { get; set; }
        public string TemperatureTimestamp { get; set; }
        public string TimeZone { get; set; }
    }
}
