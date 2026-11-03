using Bob1.Carpool.Public;
using Bob1.Identity.Public;
using Bob1.Matches.Public;
using Bob1.Participation.Domain;
using Bob1.Participation.Infrastructure;

namespace Bob1.Participation.Features.Queries;

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

internal static class QueryEndpoints
{
    private static SlotDto ToDto(Slot s, Guid userId) =>
        new(s.Id, s.Kind.ToString(), s.Role, s.Capacity, s.Filled, s.StartsAtUtc, s.EndsAtUtc, s.Signups.Any(x => x.UserId == userId));

    public static void Map(IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().WithTags("Participation");

        // Everything the "match detail" screen needs in one call.
        g.MapGet("/matches/{matchId:guid}/participation", async (Guid matchId, ParticipationDbContext db, IMatchesApi matches,
            IIdentityApi identity, ICurrentUser user, CancellationToken ct) =>
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
                .Select(s => ToDto(s, user.UserId)).ToList();

            return Results.Ok(new ParticipationDto(confirmed, children, slots));
        });

        // "Mes engagements": everything I (or my family) committed to, upcoming only.
        g.MapGet("/me/commitments", async (ParticipationDbContext db, IMatchesApi matches, IIdentityApi identity,
            ICarpoolApi carpool, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
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
            return Results.Ok(items);
        });

        // ---- Staff ----
        var staff = api.MapGroup("/staff").RequireAuthorization(Policies.Staff).WithTags("Staff");

        staff.MapGet("/assignments", async (ParticipationDbContext db, IMatchesApi matches, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var mine = await db.Slots.AsNoTracking()
                .Where(s => s.Signups.Any(x => x.UserId == user.UserId)).ToListAsync(ct);
            var byId = await matches.GetManyAsync(mine.Select(s => s.MatchId), ct);

            var result = mine.Where(s => s.Kind == SlotKind.Staff && byId.TryGetValue(s.MatchId, out var m) && m.StartsAtUtc >= now)
                .Select(s => new StaffAssignmentDto(s.Id, s.MatchId, byId[s.MatchId].Title, byId[s.MatchId].StartsAtUtc, s.Role))
                .OrderBy(x => x.StartsAtUtc);
            return Results.Ok(result);
        });

        // Matches with staff places still to fill (default: next 30 days).
        staff.MapGet("/open-slots", async (DateTime? from, DateTime? to, ParticipationDbContext db, IMatchesApi matches,
            ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var fromUtc = Dates.ToUtc(from) ?? now;
            var toUtc = Dates.ToUtc(to) ?? fromUtc.AddDays(30);

            var upcoming = await matches.GetStartingBetweenAsync(fromUtc, toUtc, ct);
            var ids = upcoming.Select(m => m.Id).ToList();
            var slots = (await db.Slots.AsNoTracking().Include(s => s.Signups)
                    .Where(s => ids.Contains(s.MatchId)).ToListAsync(ct))
                .Where(s => s.Kind == SlotKind.Staff).ToLookup(s => s.MatchId);

            return Results.Ok(upcoming.Where(m => slots.Contains(m.Id))
                .Select(m => new OpenStaffMatchDto(m.Id, m.Title, m.DivisionName, m.StartsAtUtc,
                    slots[m.Id].OrderBy(s => s.Role).Select(s => ToDto(s, user.UserId)).ToList())));
        });

        // ---- Admin roster: who is registered / confirmed / signed up for a match ----
        api.MapGet("/admin/matches/{matchId:guid}/participation", async (Guid matchId, ParticipationDbContext db, IMatchesApi matches,
            IIdentityApi identity, CancellationToken ct) =>
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

            return Results.Ok(new RosterDto(
                regs.Select(r => new RosterChildDto(r.ChildId, childNames.GetValueOrDefault(r.ChildId, "?"),
                    r.FamilyId, familyNames.GetValueOrDefault(r.FamilyId, "?"))).ToList(),
                confirmedFamilyIds.Select(f => familyNames.GetValueOrDefault(f, "?")).ToList(),
                slots.OrderBy(s => s.Role).Select(s => new RosterSlotDto(s.Id, s.Kind.ToString(), s.Role, s.Capacity,
                    s.Signups.Select(x => new RosterPersonDto(x.UserId, userNames.GetValueOrDefault(x.UserId, "?"))).ToList())).ToList()));
        }).RequireAuthorization(Policies.Admin).WithTags("Participation (admin)");
    }
}
