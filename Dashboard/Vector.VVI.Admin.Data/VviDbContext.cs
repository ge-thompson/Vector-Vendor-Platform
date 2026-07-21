using Microsoft.EntityFrameworkCore;
using Vector.VVI.Admin.Data.Entities;

namespace Vector.VVI.Admin.Data;

/// <summary>
/// EF Core context over the VendorAPI_FK database. Read models for the existing
/// VVI tables plus the two new admin tables. VectorOTR / VectorOTR_TT joins
/// (load + tracking context, Tier 2) are intentionally not mapped yet.
/// </summary>
public class VviDbContext : DbContext
{
    public VviDbContext(DbContextOptions<VviDbContext> options) : base(options) { }

    public DbSet<VendorOutboundTransaction> OutboundTransactions => Set<VendorOutboundTransaction>();
    public DbSet<VviProfile> VviProfiles => Set<VviProfile>();
    public DbSet<VendorConfig> VendorConfigs => Set<VendorConfig>();
    public DbSet<ClientProfile> ClientProfiles => Set<ClientProfile>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminTwoFactorCode> AdminTwoFactorCodes => Set<AdminTwoFactorCode>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Timestamps are written by SQL Server defaults; treat as store-generated on add.
        b.Entity<VendorOutboundTransaction>()
            .Property(x => x.CreatedUtc).ValueGeneratedOnAdd();

        b.Entity<VendorConfig>(e =>
        {
            e.Property(x => x.CreatedUtc).ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedUtc).ValueGeneratedOnAdd();
        });

        b.Entity<ClientProfile>(e =>
        {
            e.Property(x => x.CreatedUtc).ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedUtc).ValueGeneratedOnAdd();
        });

        b.Entity<VviProfile>(e =>
        {
            e.Property(x => x.CreatedDate).ValueGeneratedOnAdd();
            e.Property(x => x.ModifiedDate).ValueGeneratedOnAdd();
            e.HasOne(x => x.VendorConfig)
             .WithMany()
             .HasForeignKey(x => x.VendorConfigID)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<AdminUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.CreatedUtc).ValueGeneratedOnAdd();
        });

        b.Entity<AdminTwoFactorCode>(e =>
        {
            e.Property(x => x.CreatedUtc).ValueGeneratedOnAdd();
            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserID)
             .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(b);
    }
}
