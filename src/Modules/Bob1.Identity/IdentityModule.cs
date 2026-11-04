using Bob1.Club.Public;
using Bob1.Identity.Domain;
using Bob1.Identity.Features;
using Bob1.Identity.Infrastructure;
using Bob1.Identity.Public;
using Microsoft.AspNetCore.Identity;

namespace Bob1.Identity;

public sealed class IdentityModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Identity")));

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IIdentityApi, IdentityApi>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        AuthEndpoints.Map(api);
        MeEndpoints.Map(api);
        FamilyEndpoints.Map(api);
        AdminPeopleEndpoints.Map(api);
        AdminFamilyEndpoints.Map(api);
        // TODO: 2FA (TOTP), password reset by email.
    }

    public async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var forceSeed = services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:Seed");
if ((!forceSeed && !services.GetRequiredService<IHostEnvironment>().IsDevelopment()) || (await db.Users.AnyAsync() && !forceSeed))
            return;

        // Development seed: three accounts you can log in with.
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var now = DateTime.UtcNow;

        var family = Family.Create("Martin", now);
        db.Families.Add(family);
        db.Children.Add(Child.Create(family.Id, "Léo", "Martin", new DateOnly(2013, 5, 14), ClubSeed.DivisionU13M));

        User Make(string email, string first, string last, UserRoles roles, string password, Guid? familyId)
        {
            var u = User.Create(email, first, last, roles, familyId, now);
            u.SetPasswordHash(hasher.HashPassword(u, password));
            return u;
        }

        db.Users.AddRange(
            Make("parent@bob1.local", "Camille", "Martin", UserRoles.Parent, "Parent123!", family.Id),
            Make("staff@bob1.local", "Sam", "Arbitre", UserRoles.Staff, "Staff123!", null),
            Make("admin@bob1.local", "Alex", "Admin", UserRoles.Admin | UserRoles.Staff, "Admin123!", null));
        await db.SaveChangesAsync();
    }
}
