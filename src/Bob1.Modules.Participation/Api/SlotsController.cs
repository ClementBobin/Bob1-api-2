using Bob1.Shared.Contracts.Matches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Participation.Api;

internal sealed record SlotRequest(string Kind, string Role, int Capacity, DateTime? StartsAtUtc, DateTime? EndsAtUtc);
internal sealed record UrgentSlotDto(Guid SlotId, Guid MatchId, string MatchTitle, DateTime MatchStartsAtUtc, string Kind, string Role, int Missing);

/// <summary>Join / leave (parents for Volunteer slots, staff for Staff slots; checked in the handler).</summary>
internal sealed record UrgentQuery(int? Days, int? Skip, int? Take);
/// <summary>Page of open slots plus the grand total of missing places (for dashboard KPIs).</summary>
internal sealed record UrgentSlotPage(int Total, int TotalMissing, IReadOnlyList<UrgentSlotDto> Items);

[Route(ApiRoutes.V1 + "/slots/{slotId:guid}/join")]
[Authorize]
[Tags("Participation")]
internal sealed class SlotsController(SlotMembershipHandler h) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Join(Guid slotId, CancellationToken ct)
    {
        var r = await h.JoinAsync(slotId, ct);
        return r.IsSuccess ? NoContent() : r.Error.ToProblem();
    }

    [HttpDelete]
    public async Task<IActionResult> Leave(Guid slotId, CancellationToken ct)
    {
        var r = await h.LeaveAsync(slotId, ct);
        return r.IsSuccess ? NoContent() : r.Error.ToProblem();
    }
}

[Route(ApiRoutes.V1 + "/admin")]
[Authorize(Policy = Policies.Admin)]
[Tags("Participation (admin)")]
internal sealed class SlotsAdminController(ParticipationDbContext db, IMatchesApi matches, TimeProvider clock) : ApiControllerBase
{
    [HttpPost("matches/{matchId:guid}/slots")]
    public async Task<IActionResult> Create(Guid matchId, SlotRequest r, CancellationToken ct)
    {
        if (Validate(r, out var kind) is { } err) return err.ToProblem();
        if (await matches.GetAsync(matchId, ct) is null) return ParticipationErrors.MatchNotFound.ToProblem();

        var slot = Slot.Create(matchId, kind, r.Role.Trim(), r.Capacity, Dates.ToUtc(r.StartsAtUtc), Dates.ToUtc(r.EndsAtUtc));
        db.Slots.Add(slot);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/matches/{matchId}/participation", new { id = slot.Id });
    }

    [HttpPut("slots/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SlotRequest r, CancellationToken ct)
    {
        if (Validate(r, out _) is { } err) return err.ToProblem();
        var slot = await db.Slots.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (slot is null) return ParticipationErrors.SlotNotFound.ToProblem();

        var updated = slot.Update(r.Role.Trim(), r.Capacity, Dates.ToUtc(r.StartsAtUtc), Dates.ToUtc(r.EndsAtUtc));
        if (!updated.IsSuccess) return updated.Error.ToProblem();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("slots/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var slot = await db.Slots.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (slot is null) return ParticipationErrors.SlotNotFound.ToProblem();
        if (slot.Filled > 0)
            return Error.Conflict("slot.has_signups", "Remove the people signed up first (or lower the capacity).").ToProblem();
        db.Slots.Remove(slot);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // The back-office "urgent" list: slots still open on matches starting soon.
    [HttpQuery("slots/urgent")]
    public async Task<IActionResult> Urgent([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UrgentQuery? query, CancellationToken ct)
    {
        query ??= new(null, null, null);
        var now = clock.GetUtcNow().UtcDateTime;
        var upcoming = (await matches.GetStartingBetweenAsync(now, now.AddDays(Math.Clamp(query.Days ?? 7, 1, 60)), ct))
            .ToDictionary(m => m.Id);
        var ids = upcoming.Keys.ToList();

        var open = await db.Slots.AsNoTracking().Where(s => ids.Contains(s.MatchId) && s.Filled < s.Capacity).ToListAsync(ct);
        var all = open
            .Select(s => new UrgentSlotDto(s.Id, s.MatchId, upcoming[s.MatchId].Title, upcoming[s.MatchId].StartsAtUtc,
                s.Kind.ToString(), s.Role, s.Capacity - s.Filled))
            .OrderBy(x => x.MatchStartsAtUtc).ThenBy(x => x.Role).ToList();
        var items = all.Skip(Paging.Skip(query.Skip)).Take(Paging.Take(query.Take, 100, 500)).ToList();
        return Ok(new UrgentSlotPage(all.Count, all.Sum(x => x.Missing), items));
    }

    private static Error? Validate(SlotRequest r, out SlotKind kind)
    {
        kind = SlotKind.Volunteer;
        if (!Enum.TryParse(r.Kind, ignoreCase: true, out kind))
            return Error.Validation("kind.invalid", "Kind must be 'Volunteer' or 'Staff'.");
        return Guard.First(Guard.NotBlank(r.Role, "role", 50), Guard.InRange(r.Capacity, "capacity", 1, 50));
    }
}
