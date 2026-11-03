using Bob1.Carpool.Features;
using Bob1.Carpool.Infrastructure;
using Bob1.Carpool.Public;

namespace Bob1.Carpool;

public sealed class CarpoolModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CarpoolDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Carpool")));
        services.AddScoped<ICarpoolApi, CarpoolApi>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => CarpoolEndpoints.Map(api);

    public async Task InitializeAsync(IServiceProvider services) =>
        await services.GetRequiredService<CarpoolDbContext>().Database.MigrateAsync();
}
