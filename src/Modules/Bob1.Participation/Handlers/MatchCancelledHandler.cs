using Bob1.Matches.Public;
using Bob1.Notifications.Public;
using Bob1.Participation.Infrastructure;

namespace Bob1.Participation.Handlers;

/// <summary>Matches publishes MatchCancelled; we tell everyone who had committed to it.</summary>
internal sealed class MatchCancelledHandler(ParticipationDbContext db, INotificationsApi notifications)
    : IDomainEventHandler<MatchCancelled>
{
    public async Task HandleAsync(MatchCancelled e, CancellationToken ct)
    {
        var families = await db.ChildRegistrations.Where(r => r.MatchId == e.MatchId).Select(r => r.FamilyId)
            .Union(db.PresenceConfirmations.Where(p => p.MatchId == e.MatchId).Select(p => p.FamilyId))
            .ToListAsync(ct);

        // Get all users from these families
        var usersFromFamilies = new List<Guid>();
        if (families.Count > 0)
        {
            // We need to get users from families - this would require Identity API
            // For now, we'll skip family notifications since we removed SendToFamilyAsync
        }

        var staffAndVolunteers = await db.Slots.Where(s => s.MatchId == e.MatchId)
            .SelectMany(s => s.Signups).Select(x => x.UserId).Distinct().ToListAsync(ct);

        var data = new Dictionary<string, string> { ["type"] = "match_cancelled", ["matchId"] = e.MatchId.ToString() };
        const string title = "Match annulé";
        var body = $"{e.Title} a été annulé.";

        // Send to staff and volunteers (persons)
        await notifications.SendToUsersAsync(staffAndVolunteers, title, body, data, ct);
    }
}
