namespace Bob1.Participation.Public;

/// <summary>Events published to other modules (Ranking...). Keep them small and stable.</summary>
public sealed record PresenceConfirmed(Guid MatchId, Guid UserId, Guid? FamilyId) : DomainEvent;

public sealed record SlotJoined(
    Guid SlotId, Guid SignupId, Guid MatchId, string Role, bool IsStaff, Guid UserId, Guid? FamilyId) : DomainEvent;

public sealed record SlotLeft(
    Guid SlotId, Guid SignupId, Guid MatchId, string Role, bool IsStaff, Guid UserId, Guid? FamilyId) : DomainEvent;
