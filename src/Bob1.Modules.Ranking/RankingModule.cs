using Bob1.Shared.Contracts.Carpool;
using Bob1.Shared.Contracts.Participation;

namespace Bob1.Modules.Ranking;

public sealed class RankingModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RankingDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Ranking")));

        services.AddScoped<PointsService>();

        services.AddScoped<IDomainEventHandler<PresenceConfirmed>, PresenceConfirmedHandler>();
        services.AddScoped<IDomainEventHandler<SlotJoined>, SlotJoinedHandler>();
        services.AddScoped<IDomainEventHandler<SlotLeft>, SlotLeftHandler>();
        services.AddScoped<IDomainEventHandler<RideOffered>, RideOfferedHandler>();
        services.AddScoped<IDomainEventHandler<RideCancelled>, RideCancelledHandler>();
    }

    public async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<RankingDbContext>();
        await db.Database.MigrateAsync();

        // Default scoring rules (all environments). Existing rules are never overwritten: admins edit them.
        var existing = await db.Rules.Select(r => r.Key).ToListAsync();
        var missing = RuleKeys.Defaults.Where(d => !existing.Contains(d.Key)).ToList();
        if (missing.Count > 0)
        {
            db.Rules.AddRange(missing);
            await db.SaveChangesAsync();
        }
    }
}
