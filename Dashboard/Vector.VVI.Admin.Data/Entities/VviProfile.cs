using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vector.VVI.Admin.Data.Entities;

/// <summary>
/// Read/write model over VendorAPI_FK.dbo.VVIProfiles.
/// NOTE (schema reality check): this table already carries CustomerID + Customer,
/// the per-event flags, inline connection/auth fields, AND a nullable link to
/// VendorConfigs (VendorConfigID). Customer identity lives HERE, not on ClientProfiles.
/// This is the table behind the Profile Management screen (plan section 3.4).
/// </summary>
[Table("VVIProfiles")]
public class VviProfile
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public int CustomerID { get; set; }

    [MaxLength(255)]
    public string Customer { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Vendor { get; set; } = string.Empty;

    [MaxLength(50)]
    public string AdapterName { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    // ---- Event flags (the quick-toggle switches on the Profiles grid) ----
    public bool LoadPosted { get; set; }
    public bool CheckCall { get; set; }
    public bool AppointmentChanged { get; set; }
    public bool POD { get; set; }
    public bool CancelLoad { get; set; }
    public bool TrackingStatus { get; set; }
    public bool Invoice { get; set; }

    // ---- Inline connection / auth (secrets stored plain per design decision) ----
    [MaxLength(500)]
    public string EndpointUrl { get; set; } = string.Empty;

    [MaxLength(50)]
    public string AuthType { get; set; } = "none";

    [MaxLength(500)]
    public string ApiKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string HeaderName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Secret { get; set; } = string.Empty;

    [MaxLength(100)]
    public string SignatureHeader { get; set; } = string.Empty;

    [MaxLength(10)]
    public string SignatureEncoding { get; set; } = "hex";

    public string Instructions { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public DateTime ModifiedDate { get; set; }

    /// <summary>Optional link to a shared VendorConfigs row (VendorConfig.ConfigID).</summary>
    public int? VendorConfigID { get; set; }

    [ForeignKey(nameof(VendorConfigID))]
    public VendorConfig? VendorConfig { get; set; }
}
