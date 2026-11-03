namespace Bob1.Matches.Public;

public sealed record MatchCancelled(Guid MatchId, string Title, DateTime StartsAtUtc) : DomainEvent;
