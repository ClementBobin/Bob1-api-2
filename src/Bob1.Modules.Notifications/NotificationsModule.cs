using Bob1.Shared.Contracts.Notifications;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

namespace Bob1.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Notifications")));

        // Firebase: set "Firebase:CredentialsPath" to your service-account JSON to enable real push.
        var credentialsPath = configuration["Firebase:CredentialsPath"];
        if (!string.IsNullOrWhiteSpace(credentialsPath))
        {
            if (FirebaseApp.DefaultInstance is null)
                FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromFile(credentialsPath) });
            services.AddSingleton<IPushSender, FcmPushSender>();
        }
        else
        {
            services.AddSingleton<IPushSender, LoggingPushSender>();
        }

        services.AddScoped<INotificationsApi, NotificationsApi>();
    }

    public async Task InitializeAsync(IServiceProvider services) =>
        await services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}
