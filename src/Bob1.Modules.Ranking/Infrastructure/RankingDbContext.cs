
namespace Bob1.Modules.Ranking.Infrastructure;

internal sealed class RankingDbContext(DbContextOptions<RankingDbContext> options) : DbContext(options)
{
    public DbSet<PointEntry> PointEntries => Set<PointEntry>();
    public DbSet<RankingRule> Rules => Set<RankingRule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PointEntry>(e =>
        {
            e.ToTable("point_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Category).HasMaxLength(50);
            e.Property(x => x.Reason).HasMaxLength(250);
            e.Property(x => x.SourceKey).HasMaxLength(200);
            e.Property(x => x.Season).HasMaxLength(9);
            e.HasIndex(x => x.SourceKey).IsUnique();
            e.HasIndex(x => new { x.Season, x.Category, x.PersonId });
        });

        b.Entity<RankingRule>(e =>
        {
            e.ToTable("ranking_rules");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.Category).HasMaxLength(50);
            e.Property(x => x.Label).HasMaxLength(150);
        });
    }
}
