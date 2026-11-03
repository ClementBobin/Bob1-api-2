using Bob1.Carpool.Public;
using Bob1.Participation.Public;
using Bob1.Ranking.Features;
using Bob1.Ranking.Handlers;
using Bob1.Ranking.Infrastructure;

namespace Bob1.Ranking;

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

    public void MapEndpoints(IEndpointRouteBuilder api) => RankingEndpoints.Map(api);

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
