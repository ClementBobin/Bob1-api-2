namespace Bob1.Ranking.Domain;

/// <summary>
/// Ledger row: one per point change (bonus or penalty). A person's score is SUM(Points), so every
/// score is explainable and can be recomputed if the rules change. Rows are never edited or deleted:
/// corrections are new, opposite rows.
/// </summary>
internal sealed class PointEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PersonId { get; private set; }  // Changed from FamilyId to PersonId
    public string Category { get; private set; } = default!;   // presence | volunteer | staff | carpool | penalty
    public int Points { get; private set; }
    public string Reason { get; private set; } = default!;
    /// <summary>Deduplication key (unique): makes every handler idempotent.</summary>
    public string SourceKey { get; private set; } = default!;
    public string Season { get; private set; } = default!;      // "2026-2027"
    public Guid? MatchId { get; private set; }
    public Guid? ActorUserId { get; private set; }               // the admin, for manual penalties
    public DateTime CreatedAtUtc { get; private set; }

    private PointEntry() { }

    public static PointEntry Create(Guid personId, string category, int points, string reason, string sourceKey,
        string season, Guid? matchId, Guid? actorUserId, DateTime nowUtc) =>
        new()
        {
            PersonId = personId, Category = category, Points = points, Reason = reason, SourceKey = sourceKey,
            Season = season, MatchId = matchId, ActorUserId = actorUserId, CreatedAtUtc = nowUtc
        };
}

/// <summary>Admin-editable scoring rule, e.g. key "slot.volunteer" = +15.</summary>
internal sealed class RankingRule
{
    public string Key { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Label { get; set; } = default!;
    public int Points { get; set; }
    // J-15 / J-4 system for rules that support it
    public int? PointsJ15 { get; set; }
    public int? PointsJ4 { get; set; }
    public bool HasJ15J4 => PointsJ15.HasValue || PointsJ4.HasValue;
}
