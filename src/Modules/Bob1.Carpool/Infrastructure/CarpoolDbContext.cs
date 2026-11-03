using Bob1.Carpool.Domain;

namespace Bob1.Carpool.Infrastructure;

internal sealed class CarpoolDbContext(DbContextOptions<CarpoolDbContext> options) : DbContext(options)
{
    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<RideBooking> Bookings => Set<RideBooking>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Ride>(e =>
        {
            e.ToTable("rides");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Ignore(x => x.DomainEvents);
            e.Ignore(x => x.SeatsLeft);
            e.Property(x => x.MeetingPoint).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(500);
            // Optimistic concurrency: a token bumped on every ride update (SQLite has no xmin/rowversion).
            e.Property<int>("Version").IsConcurrencyToken();
            e.HasMany(x => x.Bookings).WithOne().HasForeignKey(x => x.RideId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MatchId);
        });

        b.Entity<RideBooking>(e =>
        {
            e.ToTable("ride_bookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();   // client-generated ids: avoids EF treating new children as existing
            e.HasIndex(x => new { x.RideId, x.UserId }).IsUnique();
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void BumpConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("Version") is null) continue;
            var version = entry.Property("Version");
            version.CurrentValue = (int)(version.CurrentValue ?? 0) + 1;
        }
    }
}
