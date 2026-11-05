using Bob1.Shared.Contracts.Matches;

namespace Bob1.Modules.Matches.Infrastructure;

internal sealed class MatchesDbContext(DbContextOptions<MatchesDbContext> options) : DbContext(options)
{
    public DbSet<Match> Matches => Set<Match>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Match>(e =>
        {
            e.ToTable("matches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Ignore(x => x.DomainEvents);
            e.Ignore(x => x.Title);
            e.Property(x => x.DivisionName).HasMaxLength(50);
            e.Property(x => x.Opponent).HasMaxLength(100);
            e.Property(x => x.VenueName).HasMaxLength(150);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.StartsAtUtc);
            e.HasIndex(x => x.DivisionId);
        });
    }
}
