using Bob1.Matches.Public;
using Bob1.Participation.Domain;
using Bob1.Participation.Infrastructure;

namespace Bob1.Participation.Features.Slots;

internal sealed record SlotRequest(string Kind, string Role, int Capacity, DateTime? StartsAtUtc, DateTime? EndsAtUtc);
internal sealed record UrgentSlotDto(Guid SlotId, Guid MatchId, string MatchTitle, DateTime MatchStartsAtUtc, string Kind, string Role, int Missing);

internal static class SlotsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // ---- Join / leave (parents for Volunteer slots, staff for Staff slots; checked in the handler) ----
        var g = api.MapGroup("/slots/{slotId:guid}/join").RequireAuthorization().WithTags("Participation");

        g.MapPost("", async (Guid slotId, SlotMembershipHandler h, CancellationToken ct) =>
        {
            var r = await h.JoinAsync(slotId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        g.MapDelete("", async (Guid slotId, SlotMembershipHandler h, CancellationToken ct) =>
        {
            var r = await h.LeaveAsync(slotId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        // ---- Admin ----
        var admin = api.MapGroup("/admin").RequireAuthorization(Policies.Admin).WithTags("Participation (admin)");

        admin.MapPost("/matches/{matchId:guid}/slots", async (Guid matchId, SlotRequest r, ParticipationDbContext db, IMatchesApi matches, CancellationToken ct) =>
        {
            if (Validate(r, out var kind) is { } err) return err.ToProblem();
            if (await matches.GetAsync(matchId, ct) is null) return ParticipationErrors.MatchNotFound.ToProblem();

            var slot = Slot.Create(matchId, kind, r.Role.Trim(), r.Capacity, Dates.ToUtc(r.StartsAtUtc), Dates.ToUtc(r.EndsAtUtc));
            db.Slots.Add(slot);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/matches/{matchId}/participation", new { id = slot.Id });
        });

        admin.MapPut("/slots/{id:guid}", async (Guid id, SlotRequest r, ParticipationDbContext db, CancellationToken ct) =>
        {
            if (Validate(r, out _) is { } err) return err.ToProblem();
            var slot = await db.Slots.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (slot is null) return ParticipationErrors.SlotNotFound.ToProblem();

            var updated = slot.Update(r.Role.Trim(), r.Capacity, Dates.ToUtc(r.StartsAtUtc), Dates.ToUtc(r.EndsAtUtc));
            if (!updated.IsSuccess) return updated.Error.ToProblem();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapDelete("/slots/{id:guid}", async (Guid id, ParticipationDbContext db, CancellationToken ct) =>
        {
            var slot = await db.Slots.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (slot is null) return ParticipationErrors.SlotNotFound.ToProblem();
            if (slot.Filled > 0)
                return Error.Conflict("slot.has_signups", "Remove the people signed up first (or lower the capacity).").ToProblem();
            db.Slots.Remove(slot);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // The back-office "urgent" list: slots still open on matches starting soon.
        admin.MapGet("/slots/urgent", async (int? days, ParticipationDbContext db, IMatchesApi matches, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var upcoming = (await matches.GetStartingBetweenAsync(now, now.AddDays(Math.Clamp(days ?? 7, 1, 60)), ct))
                .ToDictionary(m => m.Id);
            var ids = upcoming.Keys.ToList();

            var open = await db.Slots.AsNoTracking().Where(s => ids.Contains(s.MatchId) && s.Filled < s.Capacity).ToListAsync(ct);
            return Results.Ok(open
                .Select(s => new UrgentSlotDto(s.Id, s.MatchId, upcoming[s.MatchId].Title, upcoming[s.MatchId].StartsAtUtc,
                    s.Kind.ToString(), s.Role, s.Capacity - s.Filled))
                .OrderBy(x => x.MatchStartsAtUtc).ThenBy(x => x.Role));
        });
    }

    private static Error? Validate(SlotRequest r, out SlotKind kind)
    {
        kind = SlotKind.Volunteer;
        if (!Enum.TryParse(r.Kind, ignoreCase: true, out kind))
            return Error.Validation("kind.invalid", "Kind must be 'Volunteer' or 'Staff'.");
        return Guard.First(Guard.NotBlank(r.Role, "role", 50), Guard.InRange(r.Capacity, "capacity", 1, 50));
    }
}
