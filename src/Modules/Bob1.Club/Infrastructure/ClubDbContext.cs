using Bob1.Club.Domain;

namespace Bob1.Club.Infrastructure;

internal sealed class ClubDbContext(DbContextOptions<ClubDbContext> options) : DbContext(options)
{
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Venue> Venues => Set<Venue>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Division>(e =>
        {
            e.ToTable("divisions");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(50);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<Team>(e =>
        {
            e.ToTable("teams");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.DivisionId);
        });
        b.Entity<Venue>(e =>
        {
            e.ToTable("venues");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Address).HasMaxLength(250);
        });
    }
}
