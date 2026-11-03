namespace Bob1.Carpool.Public;

public sealed record RideOffered(Guid RideId, Guid MatchId, Guid DriverUserId, Guid? DriverFamilyId) : DomainEvent;
public sealed record RideCancelled(Guid RideId, Guid MatchId, Guid DriverUserId, Guid? DriverFamilyId) : DomainEvent;

public interface ICarpoolApi
{
    /// <summary>Rides the user drives or has booked, departing at or after fromUtc (used by "my commitments").</summary>
    Task<IReadOnlyList<CarpoolCommitment>> GetCommitmentsAsync(Guid userId, DateTime fromUtc, CancellationToken ct);
}

public sealed record CarpoolCommitment(Guid RideId, Guid MatchId, bool IsDriver, int Seats, DateTime DepartureUtc, string MeetingPoint);
