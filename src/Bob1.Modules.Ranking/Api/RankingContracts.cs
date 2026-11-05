using Bob1.Shared.Contracts.Identity;

namespace Bob1.Modules.Ranking.Api;

internal sealed record PersonRankingRow(int Rank, Guid PersonId, string PersonName, string? FamilyName, int Points, bool IsMine);
internal sealed record FamilyRankingRow(int Rank, Guid FamilyId, string FamilyName, int Points, bool IsMine);
internal sealed record RankingResponse(string Season, IReadOnlyList<PersonRankingRow> Top, PersonRankingRow? Mine);
internal sealed record FamilyRankingResponse(string Season, IReadOnlyList<FamilyRankingRow> Top, FamilyRankingRow? Mine);
internal sealed record EntryDto(Guid Id, string Category, int Points, string Reason, Guid? MatchId, DateTime CreatedAtUtc, Guid PersonId, string PersonName);
internal sealed record RuleDto(string Key, string Category, string Label, int Points, int? PointsJ15, int? PointsJ4, bool HasJ15J4);
internal sealed record UpdateRuleRequest(int Points, string? Label, int? PointsJ15, int? PointsJ4);
internal sealed record PenaltyDto(Guid Id, Guid PersonId, string PersonName, int Points, string Reason, Guid? MatchId, string Season, DateTime CreatedAtUtc);
internal sealed record PenaltyRequest(Guid PersonId, string? RuleKey, int? Points, string? Reason, Guid? MatchId);
internal sealed record QuickPenaltyRequest(Guid PersonId, string RuleKey, Guid? MatchId);

/// <summary>Leaderboard queries shared by the public and admin ranking controllers.</summary>
internal static class RankingBuilders
{
    public static async Task<object> BuildAsync(string? view, string? category, string? season, int take, RankingDbContext db,
        IIdentityApi identity, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        view == "family"
            ? (object)await BuildFamilyAsync(category, season, take, db, identity, user, clock, ct)
            : await BuildPersonAsync(category, season, take, db, identity, user, clock, ct);

    private static async Task<RankingResponse> BuildPersonAsync(string? category, string? season, int take, RankingDbContext db,
        IIdentityApi identity, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var seasonKey = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var q = db.PointEntries.AsNoTracking().Where(p => p.Season == seasonKey);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(p => p.Category == category);

        var totals = await q.GroupBy(p => p.PersonId)
            .Select(g => new { PersonId = g.Key, Points = g.Sum(p => p.Points) })
            .OrderByDescending(x => x.Points).ThenBy(x => x.PersonId)
            .ToListAsync(ct);

        var wanted = totals.Take(take).Select(t => t.PersonId).ToList();
        if (user.PersonId is { } mine) wanted.Add(mine);
        var names = await identity.GetPersonNamesAsync(wanted, ct);
        var familyNames = await identity.GetFamilyNamesForPersonsAsync(wanted, ct);

        var rows = totals.Select((t, i) => new PersonRankingRow(i + 1, t.PersonId, names.GetValueOrDefault(t.PersonId, "?"),
            familyNames.GetValueOrDefault(t.PersonId), t.Points, t.PersonId == user.PersonId)).ToList();

        return new RankingResponse(seasonKey, rows.Take(take).ToList(), rows.FirstOrDefault(r => r.IsMine));
    }

    private static async Task<FamilyRankingResponse> BuildFamilyAsync(string? category, string? season, int take, RankingDbContext db,
        IIdentityApi identity, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var seasonKey = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var q = db.PointEntries.AsNoTracking().Where(p => p.Season == seasonKey);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(p => p.Category == category);

        // Get person IDs and their family IDs
        var personFamilyMap = await identity.GetPersonFamilyMapAsync(ct);

        var totals = await q.GroupBy(p => p.PersonId)
            .Select(g => new { PersonId = g.Key, Points = g.Sum(p => p.Points) })
            .ToListAsync(ct);

        // Aggregate by family (filter out null family IDs)
        var familyTotals = totals
            .Where(t => personFamilyMap.TryGetValue(t.PersonId, out var fid) && fid.HasValue)
            .GroupBy(t => personFamilyMap[t.PersonId]!.Value)
            .Select(g => new { FamilyId = g.Key, Points = g.Sum(x => x.Points) })
            .OrderByDescending(x => x.Points).ThenBy(x => x.FamilyId)
            .ToList();

        var wanted = familyTotals.Take(take).Select(t => t.FamilyId).ToList();
        if (user.FamilyId is { } mine) wanted.Add(mine);
        var names = await identity.GetFamilyNamesAsync(wanted, ct);

        var rows = familyTotals.Select((t, i) => new FamilyRankingRow(i + 1, t.FamilyId, names.GetValueOrDefault(t.FamilyId, "?"),
            t.Points, t.FamilyId == user.FamilyId)).ToList();

        return new FamilyRankingResponse(seasonKey, rows.Take(take).ToList(), rows.FirstOrDefault(r => r.IsMine));
    }
}
