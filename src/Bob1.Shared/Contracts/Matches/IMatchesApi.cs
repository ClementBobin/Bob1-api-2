namespace Bob1.Shared.Contracts.Matches;

public enum MatchStatus { Scheduled, Cancelled, Played }

/// <summary>The only thing other modules may know about matches.</summary>
public interface IMatchesApi
{
    Task<MatchInfo?> GetAsync(Guid matchId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, MatchInfo>> GetManyAsync(IEnumerable<Guid> matchIds, CancellationToken ct);
    /// <summary>Scheduled matches starting in [fromUtc, toUtc].</summary>
    Task<IReadOnlyList<MatchInfo>> GetStartingBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed record MatchInfo(
    Guid Id, string Title, Guid DivisionId, string DivisionName, string VenueName, DateTime StartsAtUtc, MatchStatus Status);
