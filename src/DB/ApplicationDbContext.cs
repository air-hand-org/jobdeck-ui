using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace ui.DB;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options
) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<PostgresUISession> Sessions { get; set; }
    public DbSet<PostgresUIUserProfile> Profiles { get; set; }

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PostgresUISession>(entity =>
        {
            entity.HasOne(s => s.Profile)
                .WithOne()
                .HasForeignKey<PostgresUIUserProfile>(p => p.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
