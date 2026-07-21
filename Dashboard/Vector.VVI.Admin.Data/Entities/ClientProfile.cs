using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vector.VVI.Admin.Data.Entities;

/// <summary>
/// Read model over VendorAPI_FK.dbo.ClientProfiles.
/// SCHEMA REALITY CHECK: this is the framework's shipper-code routing table
/// (ShipperCode + VendorName + EnabledEvents CSV + ConfigJson). It is keyed by
/// ShipperCode and already has IsActive. It does NOT carry customer name/ID.
/// The plan's "Customer Registry via Phase C column adds to ClientProfiles" needs
/// a decision (see handoff) because customer identity actually lives on VVIProfiles.
/// </summary>
[Table("ClientProfiles")]
public class ClientProfile
{
    [Key]
    public long ProfileId { get; set; }

    [MaxLength(50)]
    public string ShipperCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string VendorName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>CSV of enabled event type names.</summary>
    [MaxLength(500)]
    public string EnabledEvents { get; set; } = string.Empty;

    public string ConfigJson { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
