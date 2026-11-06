using Bob1.Shared.Contracts.Carpool;
using Bob1.Shared.Contracts.Identity;
using Bob1.Shared.Contracts.Matches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Participation.Api;

internal sealed record SlotDto(Guid Id, string Kind, string Role, int Capacity, int Filled, DateTime? StartsAtUtc, DateTime? EndsAtUtc, bool Mine);
internal sealed record ChildStatusDto(Guid ChildId, string FirstName, string LastName, bool Registered);
internal sealed record ParticipationDto(bool PresenceConfirmed, IReadOnlyList<ChildStatusDto> Children, IReadOnlyList<SlotDto> Slots);

/// <param name="Type">presence | child_registration | volunteer | staff | carpool_driver | carpool_passenger</param>
internal sealed record CommitmentDto(string Type, Guid MatchId, string MatchTitle, DateTime StartsAtUtc, string Label);

internal sealed record StaffAssignmentDto(Guid SlotId, Guid MatchId, string MatchTitle, DateTime StartsAtUtc, string Role);
internal sealed record OpenStaffMatchDto(Guid MatchId, string Title, string DivisionName, DateTime StartsAtUtc, IReadOnlyList<SlotDto> Slots);

internal sealed record RosterChildDto(Guid ChildId, string ChildName, Guid FamilyId, string FamilyName);
internal sealed record RosterPersonDto(Guid UserId, string Name);
internal sealed record RosterSlotDto(Guid SlotId, string Kind, string Role, int Capacity, IReadOnlyList<RosterPersonDto> People);
internal sealed record RosterDto(IReadOnlyList<RosterChildDto> RegisteredChildren, IReadOnlyList<string> FamiliesConfirmed, IReadOnlyList<RosterSlotDto> Slots);

internal static class SlotMapping
{
    public static SlotDto ToDto(this Slot s, Guid userId) =>
        new(s.Id, s.Kind.ToString(), s.Role, s.Capacity, s.Filled, s.StartsAtUtc, s.EndsAtUtc, s.Signups.Any(x => x.UserId == userId));
}

[Route(ApiRoutes.V1)]
[Authorize]
[Tags("Participation")]
internal sealed class ParticipationController(ParticipationDbContext db, IMatchesApi matches, IIdentityApi identity,
    ICurrentUser user, TimeProvider clock) : ApiControllerBase
{
    // Everything the "match detail" screen needs in one call.
    [HttpGet("matches/{matchId:guid}/participation")]
    public async Task<IActionResult> GetForMatch(Guid matchId, CancellationToken ct)
    {
        var match = await matches.GetAsync(matchId, ct);
        if (match is null) return ParticipationErrors.MatchNotFound.ToProblem();

        var children = new List<ChildStatusDto>();
        var confirmed = false;
        if (user.FamilyId is { } familyId)
        {
            confirmed = await db.PresenceConfirmations.AnyAsync(p => p.MatchId == matchId && p.FamilyId == familyId, ct);
            var registeredIds = await db.ChildRegistrations.Where(r => r.MatchId == matchId && r.FamilyId == familyId)
                .Select(r => r.ChildId).ToListAsync(ct);
            children = (await identity.GetChildrenOfFamilyAsync(familyId, ct))
                .Where(c => c.DivisionId is null || c.DivisionId == match.DivisionId)
                .Select(c => new ChildStatusDto(c.Id, c.FirstName, c.LastName, registeredIds.Contains(c.Id))).ToList();
        }

        var visibleKinds = new List<SlotKind>();
        if (user.IsInRole(Policies.Parent)) visibleKinds.Add(SlotKind.Volunteer);
        if (user.IsInRole(Policies.Staff)) visibleKinds.Add(SlotKind.Staff);

        var slots = (await db.Slots.AsNoTracking().Include(s => s.Signups).Where(s => s.MatchId == matchId).ToListAsync(ct))
            .Where(s => visibleKinds.Contains(s.Kind)).OrderBy(s => s.Role)
            .Select(s => s.ToDto(user.UserId)).ToList();

        return Ok(new ParticipationDto(confirmed, children, slots));
    }

    // "Mes engagements": everything I (or my family) committed to, upcoming only.
    [HttpQuery("me/commitments")]
    public async Task<IActionResult> Commitments([FromServices] ICarpoolApi carpool, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var raw = new List<(string Type, Guid MatchId, string Label)>();

        if (user.FamilyId is { } familyId)
        {
            var childNames = (await identity.GetChildrenOfFamilyAsync(familyId, ct))
                .ToDictionary(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim());

            foreach (var r in await db.ChildRegistrations.AsNoTracking().Where(r => r.FamilyId == familyId).ToListAsync(ct))
                raw.Add(("child_registration", r.MatchId, childNames.GetValueOrDefault(r.ChildId, "")));

            foreach (var m in await db.PresenceConfirmations.AsNoTracking().Where(p => p.FamilyId == familyId).Select(p => p.MatchId).ToListAsync(ct))
                raw.Add(("presence", m, ""));
        }

        foreach (var s in await db.Slots.AsNoTracking().Where(s => s.Signups.Any(x => x.UserId == user.UserId)).ToListAsync(ct))
            raw.Add((s.Kind == SlotKind.Staff ? "staff" : "volunteer", s.MatchId, s.Role));

        foreach (var c in await carpool.GetCommitmentsAsync(user.UserId, now, ct))
            raw.Add((c.IsDriver ? "carpool_driver" : "carpool_passenger", c.MatchId, c.MeetingPoint));

        var byId = await matches.GetManyAsync(raw.Select(x => x.MatchId), ct);
        var items = raw
            .Where(x => byId.TryGetValue(x.MatchId, out var m) && m.Status == MatchStatus.Scheduled && m.StartsAtUtc >= now)
            .Select(x => new CommitmentDto(x.Type, x.MatchId, byId[x.MatchId].Title, byId[x.MatchId].StartsAtUtc, x.Label))
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Type);
        return Ok(items);
    }
}

internal sealed record OpenSlotsQuery(DateTime? From, DateTime? To);

[Route(ApiRoutes.V1 + "/staff")]
[Authorize(Policy = Policies.Staff)]
[Tags("Staff")]
internal sealed class StaffController(ParticipationDbContext db, IMatchesApi matches, ICurrentUser user, TimeProvider clock)
    : ApiControllerBase
{
    [HttpQuery("assignments")]
    public async Task<IActionResult> Assignments(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var mine = await db.Slots.AsNoTracking()
            .Where(s => s.Signups.Any(x => x.UserId == user.UserId)).ToListAsync(ct);
        var byId = await matches.GetManyAsync(mine.Select(s => s.MatchId), ct);

        var result = mine.Where(s => s.Kind == SlotKind.Staff && byId.TryGetValue(s.MatchId, out var m) && m.StartsAtUtc >= now)
            .Select(s => new StaffAssignmentDto(s.Id, s.MatchId, byId[s.MatchId].Title, byId[s.MatchId].StartsAtUtc, s.Role))
            .OrderBy(x => x.StartsAtUtc);
        return Ok(result);
    }

    // Matches with staff places still to fill (default: next 30 days).
    [HttpQuery("open-slots")]
    public async Task<IActionResult> OpenSlots([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OpenSlotsQuery? query, CancellationToken ct)
    {
        var (from, to) = (query?.From, query?.To);
        var now = clock.GetUtcNow().UtcDateTime;
        var fromUtc = Dates.ToUtc(from) ?? now;
        var toUtc = Dates.ToUtc(to) ?? fromUtc.AddDays(30);

        var upcoming = await matches.GetStartingBetweenAsync(fromUtc, toUtc, ct);
        var ids = upcoming.Select(m => m.Id).ToList();
        var slots = (await db.Slots.AsNoTracking().Include(s => s.Signups)
                .Where(s => ids.Contains(s.MatchId)).ToListAsync(ct))
            .Where(s => s.Kind == SlotKind.Staff).ToLookup(s => s.MatchId);

        return Ok(upcoming.Where(m => slots.Contains(m.Id))
            .Select(m => new OpenStaffMatchDto(m.Id, m.Title, m.DivisionName, m.StartsAtUtc,
                slots[m.Id].OrderBy(s => s.Role).Select(s => s.ToDto(user.UserId)).ToList())));
    }
}

[Route(ApiRoutes.V1 + "/admin/matches/{matchId:guid}/participation")]
[Authorize(Policy = Policies.Admin)]
[Tags("Participation (admin)")]
internal sealed class ParticipationAdminController(ParticipationDbContext db, IMatchesApi matches, IIdentityApi identity)
    : ApiControllerBase
{
    // Admin roster: who is registered / confirmed / signed up for a match.
    [HttpGet]
    public async Task<IActionResult> Roster(Guid matchId, CancellationToken ct)
    {
        if (await matches.GetAsync(matchId, ct) is null) return ParticipationErrors.MatchNotFound.ToProblem();

        var regs = await db.ChildRegistrations.AsNoTracking().Where(r => r.MatchId == matchId).ToListAsync(ct);
        var confirmedFamilyIds = await db.PresenceConfirmations.AsNoTracking().Where(p => p.MatchId == matchId)
            .Select(p => p.FamilyId).ToListAsync(ct);
        var slots = await db.Slots.AsNoTracking().Include(s => s.Signups).Where(s => s.MatchId == matchId).ToListAsync(ct);

        var familyIds = regs.Select(r => r.FamilyId).Concat(confirmedFamilyIds).Distinct().ToList();
        var familyNames = await identity.GetFamilyNamesAsync(familyIds, ct);
        var userNames = await identity.GetUserNamesAsync(slots.SelectMany(s => s.Signups).Select(x => x.UserId), ct);

        var childNames = new Dictionary<Guid, string>();
        foreach (var fid in regs.Select(r => r.FamilyId).Distinct())
            foreach (var c in await identity.GetChildrenOfFamilyAsync(fid, ct))
                childNames[c.Id] = $"{c.FirstName} {c.LastName}".Trim();

        return Ok(new RosterDto(
            regs.Select(r => new RosterChildDto(r.ChildId, childNames.GetValueOrDefault(r.ChildId, "?"),
                r.FamilyId, familyNames.GetValueOrDefault(r.FamilyId, "?"))).ToList(),
            confirmedFamilyIds.Select(f => familyNames.GetValueOrDefault(f, "?")).ToList(),
            slots.OrderBy(s => s.Role).Select(s => new RosterSlotDto(s.Id, s.Kind.ToString(), s.Role, s.Capacity,
                s.Signups.Select(x => new RosterPersonDto(x.UserId, userNames.GetValueOrDefault(x.UserId, "?"))).ToList())).ToList()));
    }
}
