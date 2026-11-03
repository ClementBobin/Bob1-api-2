using Bob1.Carpool.Public;
using Bob1.Participation.Public;

namespace Bob1.Ranking.Handlers;

// Ranking reacts to other modules' events; it never calls them directly.

internal sealed class PresenceConfirmedHandler(PointsService points) : IDomainEventHandler<PresenceConfirmed>
{
    public Task HandleAsync(PresenceConfirmed e, CancellationToken ct) =>
        points.AwardAsync(RuleKeys.Presence, e.UserId, $"presence:{e.MatchId}:{e.UserId}", null, e.MatchId, ct);
}

internal sealed class SlotJoinedHandler(PointsService points) : IDomainEventHandler<SlotJoined>
{
    public Task HandleAsync(SlotJoined e, CancellationToken ct) =>
        points.AwardAsync(e.IsStaff ? RuleKeys.StaffSlot : RuleKeys.VolunteerSlot, e.UserId,
            $"slot:{e.SignupId}", e.Role, e.MatchId, ct);
}

internal sealed class SlotLeftHandler(PointsService points) : IDomainEventHandler<SlotLeft>
{
    public async Task HandleAsync(SlotLeft e, CancellationToken ct) =>
        await points.RevokeAsync($"slot:{e.SignupId}", $"slot-revoked:{e.SignupId}", ct);
}

internal sealed class RideOfferedHandler(PointsService points) : IDomainEventHandler<RideOffered>
{
    public Task HandleAsync(RideOffered e, CancellationToken ct) =>
        points.AwardAsync(RuleKeys.CarpoolOffer, e.DriverUserId, $"ride:{e.RideId}", null, e.MatchId, ct);
}

internal sealed class RideCancelledHandler(PointsService points) : IDomainEventHandler<RideCancelled>
{
    public async Task HandleAsync(RideCancelled e, CancellationToken ct) =>
        await points.RevokeAsync($"ride:{e.RideId}", $"ride-revoked:{e.RideId}", ct);
}
