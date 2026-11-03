using Bob1.Club.Domain;
using Bob1.Club.Features;
using Bob1.Club.Infrastructure;
using Bob1.Club.Public;

namespace Bob1.Club;

public sealed class ClubModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ClubDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Club")));
        services.AddScoped<IClubApi, ClubApi>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => ClubEndpoints.Map(api);

    public async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ClubDbContext>();
        await db.Database.MigrateAsync();

        if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment() || await db.Divisions.AnyAsync())
            return;

        db.Divisions.AddRange(
            new Division { Id = ClubSeed.DivisionU13M, Name = "U13 M", SortOrder = 1 },
            new Division { Id = ClubSeed.DivisionU17F, Name = "U17 F", SortOrder = 2 });
        db.Venues.Add(new Venue { Id = ClubSeed.VenueCoubertin, Name = "Gymnase Coubertin", Address = "Montpellier" });
        await db.SaveChangesAsync();
    }
}
