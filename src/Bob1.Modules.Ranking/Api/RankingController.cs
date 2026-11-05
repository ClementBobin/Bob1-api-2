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
    [HttpGet("rankings")]
    public async Task<IActionResult> Rankings(string? category, string? season, string? view, CancellationToken ct) =>
        Ok(await RankingBuilders.BuildAsync(view, category, season, 50, db, identity, user, clock, ct));

    // "Why do we have these points?": my person's recent ledger entries.
    [HttpGet("rankings/me/entries")]
    public async Task<IActionResult> MyEntries(string? season, int? take, CancellationToken ct)
    {
        var personId = user.PersonId;
        var s = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var rows = await db.PointEntries.AsNoTracking().Where(p => p.PersonId == personId && p.Season == s)
            .OrderByDescending(p => p.CreatedAtUtc).Take(Math.Clamp(take ?? 50, 1, 200))
            .Select(p => new EntryDto(p.Id, p.Category, p.Points, p.Reason, p.MatchId, p.CreatedAtUtc, p.PersonId, "")).ToListAsync(ct);
        return Ok(rows);
    }

    // The rules screen (read-only for everyone).
    [HttpGet("ranking-rules")]
    public async Task<IActionResult> Rules(CancellationToken ct) =>
        Ok(await db.Rules.AsNoTracking().OrderBy(r => r.Category).ThenBy(r => r.Key)
            .Select(r => new RuleDto(r.Key, r.Category, r.Label, r.Points, r.PointsJ15, r.PointsJ4, r.HasJ15J4)).ToListAsync(ct));
}
