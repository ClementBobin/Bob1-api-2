using Bob1.Shared.Contracts.Matches;
using Bob1.Shared.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Matches.Api;

internal sealed record MatchDto(
    Guid Id, string Title, Guid DivisionId, string DivisionName, string Opponent, bool IsHome,
    string VenueName, DateTime StartsAtUtc, MatchStatus Status, int? HomeScore, int? AwayScore);

internal sealed record MatchRequest(Guid DivisionId, string Opponent, bool IsHome, Guid VenueId, DateTime StartsAtUtc);
internal sealed record ResultRequest(int HomeScore, int AwayScore);

internal static class MatchMapping
{
    public static MatchDto ToDto(this Match m) => new(m.Id, m.Title, m.DivisionId, m.DivisionName, m.Opponent, m.IsHome,
        m.VenueName, m.StartsAtUtc, m.Status, m.HomeScore, m.AwayScore);
}

[Route(ApiRoutes.V1 + "/matches")]
[Authorize]
[Tags("Matches")]
internal sealed class MatchesController(MatchesDbContext db, IIdentityApi identity, ICurrentUser user, TimeProvider clock)
    : ApiControllerBase
{
    // Calendar. Filters: division, child (uses the child's division), date range. Defaults to the next 120 days.
    [HttpGet]
    public async Task<IActionResult> List(Guid? divisionId, Guid? childId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        if (childId is { } cid)
        {
            var child = await identity.GetChildAsync(cid, ct);
            if (child is null || child.FamilyId != user.FamilyId)
                return Error.NotFound("child.not_found", "Child not found.").ToProblem();
            divisionId = child.DivisionId ?? divisionId;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var fromUtc = Dates.ToUtc(from) ?? now.Date;
        var toUtc = Dates.ToUtc(to) ?? fromUtc.AddDays(120);

        var q = db.Matches.AsNoTracking().Where(m => m.StartsAtUtc >= fromUtc && m.StartsAtUtc <= toUtc);
        if (divisionId is { } d) q = q.Where(m => m.DivisionId == d);

        var rows = await q.OrderBy(m => m.StartsAtUtc).Take(300).ToListAsync(ct);
        return Ok(rows.Select(m => m.ToDto()));
    }

    [HttpGet("{matchId:guid}")]
    public async Task<IActionResult> Get(Guid matchId, CancellationToken ct)
    {
        var m = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct);
        return m is null ? Error.NotFound("match.not_found", "Match not found.").ToProblem() : Ok(m.ToDto());
    }
}