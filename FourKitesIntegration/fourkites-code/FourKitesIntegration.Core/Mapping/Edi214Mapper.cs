namespace FourKitesIntegration.Core.Mapping
{
    /// <summary>
    /// EDI 214 X12 status code constants for use in EventUpdate.StatusCode and StopUpdate.StatusCode.
    /// FourKites does not enforce a controlled vocabulary — these are the de facto industry standard
    /// already used in your EDI 214 messages, so they migrate to the API unchanged.
    /// </summary>
    public static class Edi214Mapper
    {
        /// <summary>
        /// Primary status codes (EDI 214 AT7-01).
        /// Place in EventUpdate.StatusCode or StopUpdate.StatusCode.
        /// </summary>
        public static class StatusCodes
        {
            /// <summary>X1 — Arrived at Pick-Up Location. Fires STOP_ARRIVAL (pickup).</summary>
            public const string ArrivedPickup = "X1";

            /// <summary>AF — Departed Pick-Up Location / In Transit. Fires STOP_DEPARTURE for LTL/Air/Rail.</summary>
            public const string DepartedPickup = "AF";

            /// <summary>CD — Carrier Departed Pick-Up Location with Shipment.</summary>
            public const string CarrierDeparted = "CD";

            /// <summary>X6 — En Route to Delivery Location. Pair with locationUpdate.</summary>
            public const string EnRoute = "X6";

            /// <summary>X4 — Arrived at Terminal Location. Pair with StopUpdate StopType=transfer.</summary>
            public const string ArrivedTerminal = "X4";

            /// <summary>X3 — Arrived at Delivery Location. Fires STOP_ARRIVAL (delivery).</summary>
            public const string ArrivedDelivery = "X3";

            /// <summary>X5 — Arrived at Delivery Location (alternate). Same effect as X3.</summary>
            public const string ArrivedDeliveryAlt = "X5";

            /// <summary>D1 — Completed Loading at Pickup OR Completed Unloading at Delivery.
            /// At delivery stop, pair with EventUpdate.Delivered = true.</summary>
            public const string LoadCompleted = "D1";

            /// <summary>OO — Paid and Delivered (terminated). Use with Delivered=true + DeliveredAt.</summary>
            public const string Delivered = "OO";

            /// <summary>K1 — Loaded at Origin (same effect as D1 at pickup).</summary>
            public const string LoadedAtOrigin = "K1";

            /// <summary>J1 — Released to Carrier (tracking begins).</summary>
            public const string ReleasedToCarrier = "J1";

            /// <summary>AG — Estimated Delivery. Prefer sending as EtaUpdate instead.</summary>
            public const string EstimatedDelivery = "AG";
        }

        /// <summary>
        /// Reason/delay codes (EDI 214 AT5-01 / AT7-04).
        /// Place in EventUpdate.StatusReasonCode or StopUpdate.StatusReasonCode (if supported).
        /// WARNING: "AF" exists as BOTH a StatusCode (Departed Pickup) AND a ReasonCode (Consignee Delay).
        /// They are different fields and must not be confused.
        /// </summary>
        public static class ReasonCodes
        {
            public const string Normal = "NS";
            public const string MissedDelivery = "A1";
            public const string IncorrectShippingAddress = "A2";
            public const string IndirectDelivery = "A3";
            public const string MisSort = "AA";
            public const string ShipperCausedDelay = "AD";
            public const string CarrierCausedDelay = "AE";
            public const string ConsigneeCausedDelay = "AF";   // NOTE: collides with StatusCodes.DepartedPickup; different field
            public const string WeightVolumeConstraint = "AG";
            public const string MechanicalBreakdown = "AH";
            public const string DriverHoursOfService = "AI";
            public const string HolidayClosed = "AJ";
            public const string CustomsHoldDelay = "AM";
            public const string LoadingEquipment = "AN";
            public const string HeldForPayment = "AO";
            public const string AwaitingDocumentation = "AP";
            public const string SevereWeather = "AT";
            public const string CivilEventDisruption = "AU";
            public const string NoRecipientAvailable = "BB";
            public const string RefusedByRecipient = "BC";
            public const string RecipientMoved = "BD";
        }

        /// <summary>Get a human-readable description for a known status code. Falls back to "Status {code}".</summary>
        public static string DescribeStatus(string code)
        {
            if (string.IsNullOrEmpty(code)) return "(none)";
            switch (code)
            {
                case StatusCodes.ArrivedPickup:        return "Arrived at Pick-Up Location";
                case StatusCodes.DepartedPickup:       return "Departed Pick-Up Location";
                case StatusCodes.CarrierDeparted:      return "Carrier Departed with Shipment";
                case StatusCodes.EnRoute:              return "En Route to Delivery";
                case StatusCodes.ArrivedTerminal:      return "Arrived at Terminal";
                case StatusCodes.ArrivedDelivery:      return "Arrived at Delivery Location";
                case StatusCodes.ArrivedDeliveryAlt:   return "Arrived at Delivery Location";
                case StatusCodes.LoadCompleted:        return "Loading/Unloading Complete";
                case StatusCodes.Delivered:            return "Delivered - Final";
                case StatusCodes.LoadedAtOrigin:       return "Loaded at Origin";
                case StatusCodes.ReleasedToCarrier:    return "Released to Carrier";
                case StatusCodes.EstimatedDelivery:    return "Estimated Delivery";
                default:                               return "Status " + code;
            }
        }

        /// <summary>Get a human-readable description for a known reason code.</summary>
        public static string DescribeReason(string code)
        {
            if (string.IsNullOrEmpty(code)) return "(none)";
            switch (code)
            {
                case ReasonCodes.Normal:                  return "Normal";
                case ReasonCodes.MissedDelivery:          return "Missed Delivery";
                case ReasonCodes.IncorrectShippingAddress:return "Incorrect Shipping Address";
                case ReasonCodes.ShipperCausedDelay:      return "Shipper Caused Delay";
                case ReasonCodes.CarrierCausedDelay:      return "Carrier Caused Delay";
                case ReasonCodes.ConsigneeCausedDelay:    return "Consignee Caused Delay";
                case ReasonCodes.MechanicalBreakdown:     return "Mechanical Breakdown";
                case ReasonCodes.DriverHoursOfService:    return "Driver Out of Hours";
                case ReasonCodes.HolidayClosed:           return "Holiday — Closed";
                case ReasonCodes.CustomsHoldDelay:        return "Customs Hold / Delay";
                case ReasonCodes.AwaitingDocumentation:   return "Awaiting Documentation";
                case ReasonCodes.SevereWeather:           return "Severe Weather Conditions";
                case ReasonCodes.NoRecipientAvailable:    return "No Recipient Available";
                case ReasonCodes.RefusedByRecipient:      return "Refused by Recipient";
                default:                                  return "Reason " + code;
            }
        }
    }
}
