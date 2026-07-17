using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vendor.Common.Events;

namespace Vendor.Project44.Mapping
{
    public static class PayloadBuilder
    {
        private const string P44DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

        public static BuildResult BuildAppointmentUpdate(LoadStatusEvent evt, Project44Config cfg)
        {
            if (evt == null) return BuildResult.Skipped("Event was null.");
            if (cfg == null) return BuildResult.Skipped("Project 44 config was null.");

            if (string.IsNullOrWhiteSpace(evt.ShipmentNumber))
                return BuildResult.Skipped(
                    $"ShipmentNumber missing on event (VectorLoadId={evt.VectorLoadId}). P44 requires BILL_OF_LADING.");

            if (evt.Stops == null || evt.Stops.Count == 0)
                return BuildResult.Skipped(
                    $"Event has no Stops list (VectorLoadId={evt.VectorLoadId}). P44 appointment updates require ALL stops.");

            for (int i = 0; i < evt.Stops.Count; i++)
            {
                var s = evt.Stops[i];
                if (s == null)
                    return BuildResult.Skipped($"Stop at index {i} was null (VectorLoadId={evt.VectorLoadId}).");
                if (!s.SequenceNumber.HasValue || s.SequenceNumber.Value <= 0)
                    return BuildResult.Skipped(
                        $"Stop at index {i} missing SequenceNumber (VectorLoadId={evt.VectorLoadId}).");
                if (!s.ScheduledArrivalLocal.HasValue || !s.ScheduledDepartureLocal.HasValue)
                    return BuildResult.Skipped(
                        $"Stop {s.SequenceNumber.Value} missing appointment window (VectorLoadId={evt.VectorLoadId}).");
            }

            var shipmentStops = new JArray();
            foreach (var s in evt.Stops)
            {
                // Times are LOCAL wall-clock at the stop. P44's format matches ours;
                // pass through unchanged — no UTC conversion (freight appointments
                // anchor to the stop's own address in P44's system).
                shipmentStops.Add(new JObject
                {
                    ["stopNumber"] = s.SequenceNumber.Value,
                    ["appointmentWindow"] = new JObject
                    {
                        ["startDateTime"] = s.ScheduledArrivalLocal.Value
                            .ToString(P44DateTimeFormat, CultureInfo.InvariantCulture),
                        ["endDateTime"] = s.ScheduledDepartureLocal.Value
                            .ToString(P44DateTimeFormat, CultureInfo.InvariantCulture)
                    }
                });
            }

            var body = new JObject
            {
                ["carrierIdentifier"] = new JObject
                {
                    ["type"] = cfg.CarrierIdentifierType,
                    ["value"] = cfg.CarrierIdentifier
                },
                ["shipmentIdentifiers"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "BILL_OF_LADING",
                        ["value"] = evt.ShipmentNumber
                    }
                },
                ["latitude"] = 0,
                ["longitude"] = 0,
                ["utcTimestamp"] = (evt.OccurredUtc == default ? DateTime.UtcNow : evt.OccurredUtc.ToUniversalTime())
                    .ToString(P44DateTimeFormat, CultureInfo.InvariantCulture),
                ["shipmentStops"] = shipmentStops
            };

            return BuildResult.Ready(body.ToString(Formatting.None));
        }
    }

    public sealed class BuildResult
    {
        public bool   IsReady    { get; private set; }
        public string Json       { get; private set; }
        public string SkipReason { get; private set; }

        public static BuildResult Ready(string json) => new BuildResult { IsReady = true, Json = json };
        public static BuildResult Skipped(string reason) => new BuildResult { IsReady = false, SkipReason = reason };
    }
}