using Microsoft.Data.Sqlite;

namespace Bob1.Modules.Ranking.Application;

internal static class RuleKeys
{
    public const string Presence = "presence";
    public const string VolunteerSlot = "slot.volunteer";
    public const string StaffSlot = "slot.staff";
    public const string CarpoolOffer = "carpool.offer";
    public const string PenaltyNoShow = "penalty.no_show";
    public const string PenaltyLateCancel = "penalty.late_cancel";

    public static readonly RankingRule[] Defaults =
    [
        new() { Key = Presence,          Category = "presence",  Label = "Présence confirmée",        Points = 10 },
        new() { Key = VolunteerSlot,     Category = "volunteer", Label = "Bénévolat (buvette...)",    Points = 15 },
        new() { Key = StaffSlot,         Category = "staff",     Label = "Poste de staff (MAR, arbitre...)", Points = 20 },
        new() { Key = CarpoolOffer,      Category = "carpool",   Label = "Covoiturage proposé",       Points = 10 },
        new() { Key = PenaltyNoShow,     Category = "penalty",   Label = "Absence non excusée",       Points = -20 },
        new() { Key = PenaltyLateCancel, Category = "penalty",   Label = "Désistement tardif",        Points = -10 },
    ];
}

/// <summary>All writes to the ledger go through here: rule lookup, season, idempotency, compensation.</summary>
internal sealed class PointsService(RankingDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Awards the points of a rule once per sourceKey. Unknown or 0-point rules are ignored.</summary>
    public async Task AwardAsync(string ruleKey, Guid personId, string sourceKey, string? reason, Guid? matchId, CancellationToken ct)
    {
        var rule = await db.Rules.AsNoTracking().SingleOrDefaultAsync(r => r.Key == ruleKey, ct);
        if (rule is null || rule.Points == 0) return;
        if (await db.PointEntries.AnyAsync(p => p.SourceKey == sourceKey, ct)) return;

        db.PointEntries.Add(PointEntry.Create(personId, rule.Category, rule.Points, reason ?? rule.Label, sourceKey,
            Season.For(Now), matchId, null, Now));
        await SaveIgnoringDuplicateAsync(ct);
    }

    /// <summary>Cancels a previous entry by appending the opposite one (once).</summary>
    public async Task<bool> RevokeAsync(string originalKey, string revokeKey, CancellationToken ct)
    {
        var original = await db.PointEntries.AsNoTracking().SingleOrDefaultAsync(p => p.SourceKey == originalKey, ct);
        if (original is null) return false;
        if (await db.PointEntries.AnyAsync(p => p.SourceKey == revokeKey, ct)) return true;

        db.PointEntries.Add(PointEntry.Create(original.PersonId, original.Category, -original.Points,
            $"Annulation : {original.Reason}", revokeKey, original.Season, original.MatchId, null, Now));
        await SaveIgnoringDuplicateAsync(ct);
        return true;
    }

    public async Task<PointEntry> AddManualAsync(Guid personId, string category, int points, string reason, Guid? matchId, Guid actorUserId, CancellationToken ct)
    {
        var entry = PointEntry.Create(personId, category, points, reason, $"manual:{Guid.NewGuid()}",
            Season.For(Now), matchId, actorUserId, Now);
        db.PointEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    private async Task SaveIgnoringDuplicateAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }) // UNIQUE / PRIMARY KEY
        {
            db.ChangeTracker.Clear(); // a concurrent handler already recorded it
        }
    }
}
