using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using ui.Session;

namespace ui.DB;

public sealed class ApplicationDbContext(DbContextOptions options)
    : DbContext(options), IDataProtectionKeyContext
{
    // TODO: not UISession, Use DBUISession
    // FIXME: cleanup expired sessions by pg_cron
    public DbSet<PostgresUISession> Sessions { get; set; }
    public DbSet<PostgresUIUserProfile> Profiles { get; set; }

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PostgresUIUserProfile>()
            .HasKey(p => p.SessionId)
            ;

        builder.Entity<PostgresUISession>(entity =>
        {
            entity.HasKey(s => s.SessionId);
            entity.HasOne(s => s.Profile)
                .WithOne()
                .HasForeignKey<PostgresUIUserProfile>(p => p.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
