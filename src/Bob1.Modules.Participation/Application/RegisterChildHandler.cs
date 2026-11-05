using Bob1.Shared.Contracts.Identity;
using Bob1.Shared.Contracts.Matches;
using Microsoft.Data.Sqlite;

namespace Bob1.Modules.Participation.Application;

internal sealed class RegisterChildHandler(
    ParticipationDbContext db, IMatchesApi matches, IIdentityApi identity, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result> RegisterAsync(Guid matchId, Guid childId, CancellationToken ct)
    {
        if (user.FamilyId is not { } familyId) return ParticipationErrors.NoFamily;

        var now = clock.GetUtcNow().UtcDateTime;
        var match = await matches.GetAsync(matchId, ct);
        if (MatchRules.EnsureOpen(match, now) is { } closed) return closed;

        var child = await identity.GetChildAsync(childId, ct);
        if (child is null || child.FamilyId != familyId)
            return Error.NotFound("child.not_found", "Child not found.");
        if (child.DivisionId is { } division && division != match!.DivisionId)
            return Error.Conflict("child.wrong_division", "This match is for another division.");

        if (await db.ChildRegistrations.AnyAsync(r => r.MatchId == matchId && r.ChildId == childId, ct))
            return Result.Success(); // idempotent

        db.ChildRegistrations.Add(ChildRegistration.Create(matchId, childId, familyId, now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }) // UNIQUE / PRIMARY KEY
        {
            // already registered by a concurrent request
        }
        return Result.Success();
    }

    public async Task<Result> UnregisterAsync(Guid matchId, Guid childId, CancellationToken ct)
    {
        if (user.FamilyId is not { } familyId) return ParticipationErrors.NoFamily;

        var match = await matches.GetAsync(matchId, ct);
        if (match is null) return ParticipationErrors.MatchNotFound;
        if (match.StartsAtUtc <= clock.GetUtcNow().UtcDateTime) return ParticipationErrors.MatchAlreadyStarted;

        var registration = await db.ChildRegistrations
            .SingleOrDefaultAsync(r => r.MatchId == matchId && r.ChildId == childId && r.FamilyId == familyId, ct);
        if (registration is null) return Result.Success();

        db.ChildRegistrations.Remove(registration);
        await db.SaveChangesAsync(ct);
        // Late cancellations are penalised manually by an admin (Ranking: POST /admin/penalties/quick).
        return Result.Success();
    }
}
