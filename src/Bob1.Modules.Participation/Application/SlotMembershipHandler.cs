using Bob1.Shared.Contracts.Matches;

namespace Bob1.Modules.Participation.Application;

internal sealed class SlotMembershipHandler(
    ParticipationDbContext db, IMatchesApi matches, ICurrentUser user, IDomainEventDispatcher events, TimeProvider clock)
{
    private const int MaxAttempts = 3;

    public async Task<Result> JoinAsync(Guid slotId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var slot = await db.Slots.Include(s => s.Signups).SingleOrDefaultAsync(s => s.Id == slotId, ct);
            if (slot is null) return ParticipationErrors.SlotNotFound;

            var role = slot.Kind == SlotKind.Staff ? Policies.Staff : Policies.Parent;
            if (!user.IsInRole(role)) return ParticipationErrors.WrongAudience;

            var now = clock.GetUtcNow().UtcDateTime;
            if (MatchRules.EnsureOpen(await matches.GetAsync(slot.MatchId, ct), now) is { } closed) return closed;

            var joined = slot.Join(user.UserId, user.FamilyId, now);
            if (!joined.IsSuccess) return joined;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)   // includes DbUpdateConcurrencyException: someone else took the place, re-check
            {
                db.ChangeTracker.Clear();
                continue;
            }

            await events.DispatchAsync(slot.DomainEvents, ct);
            slot.ClearDomainEvents();
            return Result.Success();
        }
        return Error.Conflict("slot.contention", "Too many simultaneous requests, please retry.");
    }

    public async Task<Result> LeaveAsync(Guid slotId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var slot = await db.Slots.Include(s => s.Signups).SingleOrDefaultAsync(s => s.Id == slotId, ct);
            if (slot is null) return ParticipationErrors.SlotNotFound;

            var now = clock.GetUtcNow().UtcDateTime;
            var match = await matches.GetAsync(slot.MatchId, ct);
            if (match is not null && match.StartsAtUtc <= now) return ParticipationErrors.MatchAlreadyStarted;

            var signup = slot.Signups.SingleOrDefault(s => s.UserId == user.UserId);
            var left = slot.Leave(user.UserId);
            if (!left.IsSuccess) return left;
            if (signup is not null) db.SlotSignups.Remove(signup);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            await events.DispatchAsync(slot.DomainEvents, ct);
            slot.ClearDomainEvents();
            return Result.Success();
        }
        return Error.Conflict("slot.contention", "Too many simultaneous requests, please retry.");
    }
}
