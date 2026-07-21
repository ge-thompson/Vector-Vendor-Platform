using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vector.VVI.Admin.Data.Entities;

/// <summary>
/// NEW table dbo.AdminTwoFactorCodes (VendorAPI_FK). One row per 2FA challenge.
/// The 6-digit code is never stored in the clear — only its hash.
/// </summary>
[Table("AdminTwoFactorCodes")]
public class AdminTwoFactorCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CodeID { get; set; }

    public int UserID { get; set; }

    [MaxLength(255)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresUtc { get; set; }

    public int Attempts { get; set; }

    public DateTime? UsedUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    [ForeignKey(nameof(UserID))]
    public AdminUser? User { get; set; }
}
