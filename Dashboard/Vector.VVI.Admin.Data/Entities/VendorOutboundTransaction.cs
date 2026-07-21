using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vector.VVI.Admin.Data.Entities;

/// <summary>
/// Read model over VendorAPI_FK.dbo.VendorOutboundTransactions.
/// Every outbound dispatch writes one row here (success, failure, skipped, error).
/// Source of the Live Ops feed and Transaction Search.
/// </summary>
[Table("VendorOutboundTransactions")]
public class VendorOutboundTransaction
{
    [Key]
    public long TransactionId { get; set; }

    [MaxLength(50)]
    public string VendorName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EventTypeName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string VectorLoadId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ShipperCode { get; set; }

    [MaxLength(50)]
    public string? SourceSystem { get; set; }

    /// <summary>PENDING | ACK | SKIPPED | HTTP_FAIL | TRANSPORT_FAIL | RATE_LIMITED.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "PENDING";

    public int? HttpStatusCode { get; set; }

    [MaxLength(20)]
    public string? ErrorCategory { get; set; }

    public string? ErrorMessage { get; set; }

    [MaxLength(100)]
    public string? VendorRequestId { get; set; }

    [MaxLength(100)]
    public string? VendorLoadId { get; set; }

    [MaxLength(50)]
    public string? ExpectedCallbackType { get; set; }

    public string? RequestPayload { get; set; }

    public string? ResponseBody { get; set; }

    /// <summary>Real UTC (DB default sysutcdatetime()). Display converts to Central.</summary>
    public DateTime CreatedUtc { get; set; }

    public DateTime? AckUtc { get; set; }

    public DateTime? ConfirmedUtc { get; set; }

    public int? DurationMs { get; set; }
}
