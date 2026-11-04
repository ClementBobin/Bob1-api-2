using Bob1.Matches.Public;
using Bob1.Participation.Features.ConfirmPresence;
using Bob1.Participation.Features.Queries;
using Bob1.Participation.Features.Registration;
using Bob1.Participation.Features.Slots;
using Bob1.Participation.Handlers;
using Bob1.Participation.Infrastructure;

namespace Bob1.Participation;

public sealed class ParticipationModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ParticipationDbContext>(o => o.UseSqlite(
            configuration.GetConnectionString("Default"),
            sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory_Participation")));

        services.AddScoped<ConfirmPresenceHandler>();
        services.AddScoped<RegisterChildHandler>();
        services.AddScoped<SlotMembershipHandler>();

        // Reacts to Matches' event (Participation depends on Matches, never the reverse).
        services.AddScoped<IDomainEventHandler<MatchCancelled>, MatchCancelledHandler>();
        services.AddHostedService<PresenceReminderService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        ConfirmPresenceEndpoint.Map(api);
        RegistrationEndpoints.Map(api);
        SlotsEndpoints.Map(api);
        QueryEndpoints.Map(api);
    }

    public async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ParticipationDbContext>();
        await db.Database.MigrateAsync();

        var forceSeed = services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:Seed");
        if ((!forceSeed && !services.GetRequiredService<IHostEnvironment>().IsDevelopment()) || (await db.Slots.AnyAsync() && !forceSeed))
            return;

        // Dev seed: a buvette slot (parents) and two staff slots on the seeded match.
        var matchId = Bob1.Matches.MatchesModule.DevSeedMatchId;
        db.Slots.AddRange(
            Domain.Slot.Create(matchId, Domain.SlotKind.Volunteer, "Buvette", 2, null, null),
            Domain.Slot.Create(matchId, Domain.SlotKind.Staff, "MAR", 1, null, null),
            Domain.Slot.Create(matchId, Domain.SlotKind.Staff, "Arbitre 1", 1, null, null));
        await db.SaveChangesAsync();
    }
}
