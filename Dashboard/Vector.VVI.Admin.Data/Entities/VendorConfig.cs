using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vector.VVI.Admin.Data.Entities;

/// <summary>
/// Read/write model over VendorAPI_FK.dbo.VendorConfigs.
/// Shared, reusable vendor connection blocks (ConfigJson). Behind the
/// Vendor Connections screen (plan section 3.5). Secrets live inside ConfigJson
/// and are shown in plain text per design decision.
/// </summary>
[Table("VendorConfigs")]
public class VendorConfig
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ConfigID { get; set; }

    [MaxLength(100)]
    public string ConfigName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string VendorName { get; set; } = string.Empty;

    public string ConfigJson { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
