using Bob1.Club.Public;
using Bob1.Matches.Domain;
using Bob1.Matches.Features;
using Bob1.Matches.Infrastructure;
using Bob1.Matches.Public;

namespace Bob1.Matches;

public sealed class MatchesModule : IModule
{
    public static readonly Guid DevSeedMatchId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MatchesDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Matches")));
        services.AddScoped<IMatchesApi, MatchesApi>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => MatchesEndpoints.Map(api);

    public async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MatchesDbContext>();
        await db.Database.MigrateAsync();

        var forceSeed = services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:Seed");
        if ((!forceSeed && !services.GetRequiredService<IHostEnvironment>().IsDevelopment()) || (await db.Matches.AnyAsync() && !forceSeed))
            return;

        db.Matches.Add(Match.Create(ClubSeed.DivisionU13M, "U13 M", "Castelnau", true,
            ClubSeed.VenueCoubertin, "Gymnase Coubertin", DateTime.UtcNow.AddDays(7), DevSeedMatchId));
        await db.SaveChangesAsync();
    }
}
