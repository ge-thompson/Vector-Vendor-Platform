using System;
using System.Collections.Generic;
using Vendor.Common.Events;
using Vendor.Common.Persistence;

namespace OTR_API.DataClasses
{
    /// <summary>
    /// Translates TruckTools' status codes and names into the framework's
    /// vendor-agnostic LoadStatusType enum.
    ///
    /// Both the type AND the original raw code are sent in the dispatched event
    /// (LoadStatusEvent.SourceStatusCode), so adapters that need finer granularity
    /// (FK using EDI 214, etc.) can pass the source code through.
    ///
    /// PHASE 1 NOTE: this is a best-guess starter table based on common TT/EDI
    /// nomenclature (open item O-002). REFINE after collecting audit data on what
    /// TT actually sends. To see what TT codes appear in real traffic:
    ///
    ///   SELECT DISTINCT name, code, COUNT(*) AS Hits
    ///   FROM dbo.LoadTrackingStatusInfo
    ///   WHERE timeStamp > DATEADD(DAY, -90, GETDATE())
    ///   GROUP BY name, code
    ///   ORDER BY Hits DESC;
    ///
    /// To add or override mappings WITHOUT a code deploy:
    ///   INSERT INTO VendorAPI_FK.dbo.VendorStatusMapping
    ///     (VendorName, Direction, SourceSystem, SourceCode, TargetCode, Notes)
    ///   VALUES
    ///     ('GLOBAL', 'Inbound', 'TruckTools', '&lt;TT code&gt;', '&lt;LoadStatusType name&gt;', 'reason');
    ///
    /// Then call VendorDispatcher.Instance.RefreshStatusMappings() to reload cache.
    /// </summary>
    public static class TruckToolsStatusMapper
    {
        // ─── Hardcoded template (Phase 1 best guess) ─────────────────────────
        // Used as fallback when no row exists in dbo.VendorStatusMapping for the
        // incoming TT code. Lowercase keys; we lowercase inputs before lookup.

        private static readonly IReadOnlyDictionary<string, LoadStatusType> CodeToType =
            new Dictionary<string, LoadStatusType>(StringComparer.OrdinalIgnoreCase)
            {
                // Pickup events
                { "arrived_pickup",     LoadStatusType.ArrivedAtPickup },
                { "at_pickup",          LoadStatusType.ArrivedAtPickup },
                { "x1",                 LoadStatusType.ArrivedAtPickup },

                { "departed_pickup",    LoadStatusType.DepartedPickup },
                { "picked_up",          LoadStatusType.DepartedPickup },
                { "loaded",             LoadStatusType.DepartedPickup },
                { "af",                 LoadStatusType.DepartedPickup },

                // In-transit
                { "in_transit",         LoadStatusType.InTransit },
                { "transit",            LoadStatusType.InTransit },
                { "en_route",           LoadStatusType.InTransit },
                { "ag",                 LoadStatusType.InTransit },

                // Delivery events
                { "arrived_delivery",   LoadStatusType.ArrivedAtDelivery },
                { "at_delivery",        LoadStatusType.ArrivedAtDelivery },
                { "x3",                 LoadStatusType.ArrivedAtDelivery },

                { "departed_delivery",  LoadStatusType.DepartedDelivery },
                { "cd",                 LoadStatusType.DepartedDelivery },

                { "delivered",          LoadStatusType.Delivered },
                { "completed",          LoadStatusType.Delivered },
                { "pod",                LoadStatusType.Delivered },
                { "d1",                 LoadStatusType.Delivered },

                // Lifecycle
                { "dispatched",         LoadStatusType.Dispatched },
                { "assigned",           LoadStatusType.Dispatched },
                { "tracking_started",   LoadStatusType.Dispatched },
                { "oa",                 LoadStatusType.Dispatched },

                // Exceptions
                { "exception",          LoadStatusType.Exception },
                { "delay",              LoadStatusType.Exception },
                { "delayed",            LoadStatusType.Exception },
                { "problem",            LoadStatusType.Exception },
                { "a3",                 LoadStatusType.Exception }
            };

        /// <summary>
        /// Translates a TruckTools status code (or name) to LoadStatusType.
        /// Checks the DB override cache first; falls back to the hardcoded template;
        /// returns LoadStatusType.Other if neither matches.
        ///
        /// The caller should still set LoadStatusEvent.SourceStatusCode to the
        /// original raw value so the adapter can pass it through to the vendor when
        /// the coarse enum isn't precise enough.
        /// </summary>
        /// <param name="ttCodeOrName">
        /// The raw value from TruckTools. Can be either Status.code or Status.name.
        /// Case-insensitive. Whitespace is trimmed. Spaces become underscores so
        /// "Arrived Pickup" and "ARRIVED_PICKUP" both match.
        /// </param>
        public static LoadStatusType Map(string ttCodeOrName)
        {
            if (string.IsNullOrWhiteSpace(ttCodeOrName))
                return LoadStatusType.Other;

            var normalized = ttCodeOrName.Trim().Replace(' ', '_');

            // DB override check (only if dispatcher has initialized the cache).
            if (VendorStatusMappingStore.IsInitialized)
            {
                // Try the GLOBAL inbound mapping first; if vendors ever need
                // FK-specific or P44-specific inbound, those rows can be added too.
                var dbOverride = VendorStatusMappingStore.Instance.GetInbound(
                    "GLOBAL", "TruckTools", normalized);

                LoadStatusType dbType;
                if (!string.IsNullOrEmpty(dbOverride) &&
                    Enum.TryParse<LoadStatusType>(dbOverride, true, out dbType))
                {
                    return dbType;
                }
            }

            // Hardcoded template fallback.
            LoadStatusType type;
            return CodeToType.TryGetValue(normalized, out type) ? type : LoadStatusType.Other;
        }

        /// <summary>
        /// Diagnostic accessor: returns the hardcoded template dictionary.
        /// Useful for admin/diagnostic pages that show "what does this code map to by default."
        /// </summary>
        public static IReadOnlyDictionary<string, LoadStatusType> GetHardcodedDefaults() => CodeToType;
    }
}
