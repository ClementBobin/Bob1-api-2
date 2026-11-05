using Bob1.Shared.Contracts.Club;
using Bob1.Shared.Contracts.Matches;

namespace Bob1.Modules.Matches;

public sealed class MatchesModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MatchesDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Matches")));
        services.AddScoped<IMatchesApi, MatchesApi>();
    }

    public async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MatchesDbContext>();
        await db.Database.MigrateAsync();

        var forceSeed = services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:Seed");
        if ((!forceSeed && !services.GetRequiredService<IHostEnvironment>().IsDevelopment()) || (await db.Matches.AnyAsync() && !forceSeed))
            return;

        db.Matches.Add(Match.Create(ClubSeed.DivisionU13M, "U13 M", "Castelnau", true,
            ClubSeed.VenueCoubertin, "Gymnase Coubertin", DateTime.UtcNow.AddDays(7), MatchesSeed.DevSeedMatchId));
        await db.SaveChangesAsync();
    }
}
