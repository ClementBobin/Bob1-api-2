using Bob1.Shared.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Ranking.Api;

[Route(ApiRoutes.V1)]
[Authorize]
[Tags("Ranking")]
internal sealed class RankingController(RankingDbContext db, IIdentityApi identity, ICurrentUser user, TimeProvider clock)
    : ApiControllerBase
{
    // Person (or family) rankings by category
    [HttpQuery("rankings")]
    public async Task<IActionResult> Rankings([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RankingQuery? query, CancellationToken ct) =>
        Ok(await RankingBuilders.BuildAsync(query ?? new(null, null, null, null, null, null), 50, 100, db, identity, user, clock, ct));

    // "Why do we have these points?": my person's recent ledger entries.
    [HttpQuery("rankings/me/entries")]
    public async Task<IActionResult> MyEntries([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MyEntriesQuery? query, CancellationToken ct)
    {
        var personId = user.PersonId;
        var (season, take) = (query?.Season, query?.Take);
        var s = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var rows = await db.PointEntries.AsNoTracking().Where(p => p.PersonId == personId && p.Season == s)
            .OrderByDescending(p => p.CreatedAtUtc).Take(Math.Clamp(take ?? 50, 1, 200))
            .Select(p => new EntryDto(p.Id, p.Category, p.Points, p.Reason, p.MatchId, p.CreatedAtUtc, p.PersonId, "")).ToListAsync(ct);
        return Ok(rows);
    }

    // The rules screen (read-only for everyone).
    [HttpQuery("ranking-rules")]
    public async Task<IActionResult> Rules([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RuleQuery? query, CancellationToken ct)
    {
        query ??= new(null, null, null, null);
        var q = db.Rules.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Category)) q = q.Where(r => r.Category == query.Category);
        if (Paging.Term(query.Search) is { } s) q = q.Where(r => r.Label.ToLower().Contains(s) || r.Key.ToLower().Contains(s));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(r => r.Category).ThenBy(r => r.Key)
            .Skip(Paging.Skip(query.Skip)).Take(Paging.Take(query.Take, 100, 500))
            .Select(r => new RuleDto(r.Key, r.Category, r.Label, r.Points, r.PointsJ15, r.PointsJ4, r.HasJ15J4)).ToListAsync(ct);
        return Ok(new PagedResult<RuleDto>(total, items));
    }
}
