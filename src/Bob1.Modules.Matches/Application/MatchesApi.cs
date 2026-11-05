using Bob1.Shared.Contracts.Matches;

namespace Bob1.Modules.Matches.Application;

internal sealed class MatchesApi(MatchesDbContext db) : IMatchesApi
{
    private static MatchInfo Map(Match m) =>
        new(m.Id, m.Title, m.DivisionId, m.DivisionName, m.VenueName, m.StartsAtUtc, m.Status);

    public async Task<MatchInfo?> GetAsync(Guid matchId, CancellationToken ct)
    {
        var m = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct);
        return m is null ? null : Map(m);
    }

    public async Task<IReadOnlyDictionary<Guid, MatchInfo>> GetManyAsync(IEnumerable<Guid> matchIds, CancellationToken ct)
    {
        var ids = matchIds.Distinct().ToList();
        var rows = await db.Matches.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        return rows.ToDictionary(m => m.Id, Map);
    }

    public async Task<IReadOnlyList<MatchInfo>> GetStartingBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await db.Matches.AsNoTracking()
            .Where(m => m.StartsAtUtc >= fromUtc && m.StartsAtUtc <= toUtc && m.Status == MatchStatus.Scheduled)
            .OrderBy(m => m.StartsAtUtc).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }
}
