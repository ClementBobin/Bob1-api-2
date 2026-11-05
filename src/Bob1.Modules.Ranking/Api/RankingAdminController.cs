using Bob1.Shared.Contracts.Identity;
using Bob1.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Ranking.Api;

[Route(ApiRoutes.V1 + "/admin")]
[Authorize(Policy = Policies.Admin)]
[Tags("Ranking (admin)")]
internal sealed class RankingAdminController(RankingDbContext db, IIdentityApi identity, ICurrentUser user, TimeProvider clock)
    : ApiControllerBase
{
    // Person (or family) rankings (admin view, bigger page)
    [HttpGet("rankings")]
    public async Task<IActionResult> Rankings(string? category, string? season, string? view, CancellationToken ct) =>
        Ok(await RankingBuilders.BuildAsync(view, category, season, 500, db, identity, user, clock, ct));

    [HttpPut("ranking-rules/{key}")]
    public async Task<IActionResult> UpdateRule(string key, UpdateRuleRequest r, CancellationToken ct)
    {
        var rule = await db.Rules.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (rule is null) return Error.NotFound("rule.not_found", "Rule not found.").ToProblem();
        if (Math.Abs(r.Points) > 1000) return Error.Validation("points.out_of_range", "Points must be between -1000 and 1000.").ToProblem();
        if (r.PointsJ15.HasValue && Math.Abs(r.PointsJ15.Value) > 1000) return Error.Validation("points_j15.out_of_range", "Points J-15 must be between -1000 and 1000.").ToProblem();
        if (r.PointsJ4.HasValue && Math.Abs(r.PointsJ4.Value) > 1000) return Error.Validation("points_j4.out_of_range", "Points J-4 must be between -1000 and 1000.").ToProblem();

        rule.Points = r.Points;
        if (!string.IsNullOrWhiteSpace(r.Label)) rule.Label = r.Label.Trim();
        rule.PointsJ15 = r.PointsJ15;
        rule.PointsJ4 = r.PointsJ4;
        await db.SaveChangesAsync(ct);
        return Ok(new RuleDto(rule.Key, rule.Category, rule.Label, rule.Points, rule.PointsJ15, rule.PointsJ4, rule.HasJ15J4));
    }

    [HttpGet("penalties")]
    public async Task<IActionResult> Penalties(Guid? personId, string? season, CancellationToken ct)
    {
        var s = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var q = db.PointEntries.AsNoTracking().Where(p => p.Category == "penalty" && p.Season == s);
        if (personId is { } f) q = q.Where(p => p.PersonId == f);

        var rows = await q.OrderByDescending(p => p.CreatedAtUtc).Take(200).ToListAsync(ct);
        var personNames = await identity.GetPersonNamesAsync(rows.Select(p => p.PersonId), ct);
        return Ok(rows.Select(p => new PenaltyDto(p.Id, p.PersonId, personNames.GetValueOrDefault(p.PersonId, "?"),
            p.Points, p.Reason, p.MatchId, p.Season, p.CreatedAtUtc)));
    }

    // Free-form penalty (or bonus): either reference a rule, or give points + reason.
    [HttpPost("penalties")]
    public Task<IActionResult> AddPenalty(PenaltyRequest r,
        [FromServices] PointsService points, [FromServices] INotificationsApi notifications, CancellationToken ct) =>
        ApplyPenaltyAsync(r, points, notifications, ct);

    // One-tap penalty from a rule (no-show, late cancellation...).
    [HttpPost("penalties/quick")]
    public Task<IActionResult> AddQuickPenalty(QuickPenaltyRequest r,
        [FromServices] PointsService points, [FromServices] INotificationsApi notifications, CancellationToken ct) =>
        ApplyPenaltyAsync(new PenaltyRequest(r.PersonId, r.RuleKey, null, null, r.MatchId), points, notifications, ct);

    [HttpPost("penalties/{id:guid}/revoke")]
    public async Task<IActionResult> RevokePenalty(Guid id, [FromServices] PointsService points, CancellationToken ct)
    {
        var entry = await db.PointEntries.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Category == "penalty", ct);
        if (entry is null) return Error.NotFound("penalty.not_found", "Penalty not found.").ToProblem();
        await points.RevokeAsync(entry.SourceKey, $"revoke:{id}", ct);
        return NoContent();
    }

    // Points history - all entries for a person
    [HttpGet("points-history")]
    public async Task<IActionResult> PointsHistory(Guid? personId, string? category, string? season, int? skip, int? take, CancellationToken ct)
    {
        var s = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var q = db.PointEntries.AsNoTracking().Where(p => p.Season == s);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(p => p.Category == category);
        if (personId is { } f) q = q.Where(p => p.PersonId == f);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.CreatedAtUtc)
            .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 100, 1, 500)).ToListAsync(ct);

        var personNames = await identity.GetPersonNamesAsync(rows.Select(p => p.PersonId), ct);

        var items = rows.Select(p => new EntryDto(p.Id, p.Category, p.Points, p.Reason, p.MatchId, p.CreatedAtUtc, p.PersonId, personNames.GetValueOrDefault(p.PersonId, "?")));
        return Ok(new { total, items });
    }

    private async Task<IActionResult> ApplyPenaltyAsync(PenaltyRequest r, PointsService points,
        INotificationsApi notifications, CancellationToken ct)
    {
        if ((await identity.GetPersonNamesAsync([r.PersonId], ct)).Count == 0)
            return Error.NotFound("person.not_found", "Person not found.").ToProblem();

        int pts;
        string reason;
        if (!string.IsNullOrWhiteSpace(r.RuleKey))
        {
            var rule = await db.Rules.AsNoTracking().SingleOrDefaultAsync(x => x.Key == r.RuleKey, ct);
            if (rule is null || rule.Category != "penalty")
                return Error.Validation("rule.invalid", "Unknown penalty rule.").ToProblem();
            pts = rule.Points;
            reason = string.IsNullOrWhiteSpace(r.Reason) ? rule.Label : r.Reason.Trim();
        }
        else
        {
            if (r.Points is null or 0) return Error.Validation("points.required", "Points are required (non-zero).").ToProblem();
            if (Guard.NotBlank(r.Reason, "reason", 250) is { } err) return err.ToProblem();
            pts = r.Points.Value;
            reason = r.Reason!.Trim();
        }

        var entry = await points.AddManualAsync(r.PersonId, "penalty", pts, reason, r.MatchId, user.UserId, ct);

        await notifications.SendToPersonAsync(r.PersonId, pts < 0 ? "Pénalité" : "Bonus",
            $"{(pts > 0 ? "+" : "")}{pts} pts : {reason}",
            new Dictionary<string, string> { ["type"] = "ranking_entry" }, ct);

        return Created($"/api/v1/admin/penalties?personId={r.PersonId}",
            new EntryDto(entry.Id, entry.Category, entry.Points, entry.Reason, entry.MatchId, entry.CreatedAtUtc, entry.PersonId, ""));
    }
}
