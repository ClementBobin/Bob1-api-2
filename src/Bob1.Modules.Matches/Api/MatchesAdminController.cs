using Bob1.Shared.Contracts.Club;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Matches.Api;

internal sealed record Resolved(DivisionInfo Division, VenueInfo Venue);

[Route(ApiRoutes.V1 + "/admin/matches")]
[Authorize(Policy = Policies.Admin)]
[Tags("Matches (admin)")]
internal sealed class MatchesAdminController(MatchesDbContext db, IClubApi club, IDomainEventDispatcher events, TimeProvider clock)
    : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(MatchRequest r, CancellationToken ct)
    {
        var resolved = await ResolveAsync(r, ct);
        if (!resolved.IsSuccess) return resolved.Error.ToProblem();
        var (division, venue) = (resolved.Value.Division, resolved.Value.Venue);

        var m = Match.Create(division.Id, division.Name, r.Opponent.Trim(), r.IsHome, venue.Id, venue.Name, Dates.ToUtc(r.StartsAtUtc));
        db.Matches.Add(m);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/matches/{m.Id}", m.ToDto());
    }

    [HttpPut("{matchId:guid}")]
    public async Task<IActionResult> Update(Guid matchId, MatchRequest r, CancellationToken ct)
    {
        var m = await db.Matches.SingleOrDefaultAsync(x => x.Id == matchId, ct);
        if (m is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();

        var resolved = await ResolveAsync(r, ct);
        if (!resolved.IsSuccess) return resolved.Error.ToProblem();
        var (division, venue) = (resolved.Value.Division, resolved.Value.Venue);

        var updated = m.Update(division.Id, division.Name, r.Opponent.Trim(), r.IsHome, venue.Id, venue.Name, Dates.ToUtc(r.StartsAtUtc));
        if (!updated.IsSuccess) return updated.Error.ToProblem();
        // TODO: publish MatchRescheduled so Participation can notify registered families.
        await db.SaveChangesAsync(ct);
        return Ok(m.ToDto());
    }

    [HttpPost("{matchId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid matchId, CancellationToken ct)
    {
        var m = await db.Matches.SingleOrDefaultAsync(x => x.Id == matchId, ct);
        if (m is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();

        var cancelled = m.Cancel();
        if (!cancelled.IsSuccess) return cancelled.Error.ToProblem();
        await db.SaveChangesAsync(ct);

        await events.DispatchAsync(m.DomainEvents, ct);   // Participation notifies the families
        m.ClearDomainEvents();
        return Ok(m.ToDto());
    }

    [HttpPut("{matchId:guid}/result")]
    public async Task<IActionResult> SetResult(Guid matchId, ResultRequest r, CancellationToken ct)
    {
        if (r.HomeScore < 0 || r.AwayScore < 0)
            return Error.Validation("score.invalid", "Scores cannot be negative.").ToProblem();
        var m = await db.Matches.SingleOrDefaultAsync(x => x.Id == matchId, ct);
        if (m is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();

        var set = m.SetResult(r.HomeScore, r.AwayScore);
        if (!set.IsSuccess) return set.Error.ToProblem();
        await db.SaveChangesAsync(ct);
        return Ok(m.ToDto());
    }

    private async Task<Result<Resolved>> ResolveAsync(MatchRequest r, CancellationToken ct)
    {
        if (Guard.NotBlank(r.Opponent, "opponent", 100) is { } err) return err;
        if (Dates.ToUtc(r.StartsAtUtc) <= clock.GetUtcNow().UtcDateTime.AddDays(-1))
            return Error.Validation("starts_at.invalid", "The match date cannot be in the past.");

        var division = await club.GetDivisionAsync(r.DivisionId, ct);
        if (division is null) return Error.NotFound("division.not_found", "Division not found.");
        var venue = await club.GetVenueAsync(r.VenueId, ct);
        if (venue is null) return Error.NotFound("venue.not_found", "Venue not found.");

        return new Resolved(division, venue);
    }
}
