using Bob1.Shared.Contracts.Carpool;

namespace Bob1.Modules.Carpool;

public sealed class CarpoolModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CarpoolDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Carpool")));
        services.AddScoped<ICarpoolApi, CarpoolApi>();
    }

    public async Task InitializeAsync(IServiceProvider services) =>
        await services.GetRequiredService<CarpoolDbContext>().Database.MigrateAsync();
}
