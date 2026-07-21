using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vector.VVI.Admin.Data.Entities;

/// <summary>
/// NEW table dbo.AdminUsers (VendorAPI_FK). Dashboard login accounts.
/// Roles: "admin" (full CRUD) or "viewer" (read-only).
/// </summary>
[Table("AdminUsers")]
public class AdminUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int UserID { get; set; }

    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>admin | viewer.</summary>
    [MaxLength(20)]
    public string Role { get; set; } = "viewer";

    public bool IsActive { get; set; } = true;

    public DateTime? LastLoginUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    [MaxLength(255)]
    public string? CreatedBy { get; set; }
}
