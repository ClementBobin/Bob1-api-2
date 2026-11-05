namespace Bob1.Shared.Contracts.Matches;

public sealed record MatchCancelled(Guid MatchId, string Title, DateTime StartsAtUtc) : DomainEvent;
