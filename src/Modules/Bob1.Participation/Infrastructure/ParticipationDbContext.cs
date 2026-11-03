using Bob1.Participation.Domain;

namespace Bob1.Participation.Infrastructure;

internal sealed class ParticipationDbContext(DbContextOptions<ParticipationDbContext> options) : DbContext(options)
{
    public DbSet<PresenceConfirmation> PresenceConfirmations => Set<PresenceConfirmation>();
    public DbSet<ChildRegistration> ChildRegistrations => Set<ChildRegistration>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<SlotSignup> SlotSignups => Set<SlotSignup>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PresenceConfirmation>(e =>
        {
            e.ToTable("presence_confirmations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Ignore(x => x.DomainEvents);
            e.HasIndex(x => new { x.MatchId, x.FamilyId }).IsUnique();
        });

        b.Entity<ChildRegistration>(e =>
        {
            e.ToTable("child_registrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.MatchId, x.ChildId }).IsUnique();
            e.HasIndex(x => x.FamilyId);
        });

        b.Entity<Slot>(e =>
        {
            e.ToTable("slots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Ignore(x => x.DomainEvents);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Role).HasMaxLength(50);
            // Optimistic concurrency: a token bumped on every slot update (SQLite has no xmin/rowversion).
            e.Property<int>("Version").IsConcurrencyToken();
            e.HasMany(x => x.Signups).WithOne().HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MatchId);
        });

        b.Entity<SlotSignup>(e =>
        {
            e.ToTable("slot_signups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.SlotId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<ReminderLog>(e =>
        {
            e.ToTable("reminder_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Key).HasMaxLength(200);
            e.HasIndex(x => x.Key).IsUnique();
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
