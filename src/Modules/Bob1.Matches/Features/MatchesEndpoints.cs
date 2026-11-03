using Bob1.Club.Public;
using Bob1.Identity.Public;
using Bob1.Matches.Domain;
using Bob1.Matches.Infrastructure;
using Bob1.Matches.Public;

namespace Bob1.Matches.Features;

internal sealed record MatchDto(
    Guid Id, string Title, Guid DivisionId, string DivisionName, string Opponent, bool IsHome,
    string VenueName, DateTime StartsAtUtc, MatchStatus Status, int? HomeScore, int? AwayScore);

internal sealed record MatchRequest(Guid DivisionId, string Opponent, bool IsHome, Guid VenueId, DateTime StartsAtUtc);
internal sealed record ResultRequest(int HomeScore, int AwayScore);
internal sealed record Resolved(DivisionInfo Division, VenueInfo Venue);

internal static class MatchesEndpoints
{
    private static MatchDto ToDto(Match m) => new(m.Id, m.Title, m.DivisionId, m.DivisionName, m.Opponent, m.IsHome,
        m.VenueName, m.StartsAtUtc, m.Status, m.HomeScore, m.AwayScore);

    public static void Map(IEndpointRouteBuilder api)
    {
        var read = api.MapGroup("/matches").RequireAuthorization().WithTags("Matches");

        // Calendar. Filters: division, child (uses the child's division), date range. Defaults to the next 120 days.
        read.MapGet("", async (Guid? divisionId, Guid? childId, DateTime? from, DateTime? to,
            MatchesDbContext db, IIdentityApi identity, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
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
            return Results.Ok(rows.Select(ToDto));
        });

        read.MapGet("/{matchId:guid}", async (Guid matchId, MatchesDbContext db, CancellationToken ct) =>
        {
            var m = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, ct);
            return m is null ? Error.NotFound("match.not_found", "Match not found.").ToProblem() : Results.Ok(ToDto(m));
        });

        // ---- Admin ----
        var admin = api.MapGroup("/admin/matches").RequireAuthorization(Policies.Admin).WithTags("Matches (admin)");

        admin.MapPost("", async (MatchRequest r, MatchesDbContext db, IClubApi club, TimeProvider clock, CancellationToken ct) =>
        {
            var resolved = await ResolveAsync(r, club, clock, ct);
            if (!resolved.IsSuccess) return resolved.Error.ToProblem();
            var (division, venue) = (resolved.Value.Division, resolved.Value.Venue);

            var m = Match.Create(division.Id, division.Name, r.Opponent.Trim(), r.IsHome, venue.Id, venue.Name, Dates.ToUtc(r.StartsAtUtc));
            db.Matches.Add(m);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/matches/{m.Id}", ToDto(m));
        });

        admin.MapPut("/{matchId:guid}", async (Guid matchId, MatchRequest r, MatchesDbContext db, IClubApi club, TimeProvider clock, CancellationToken ct) =>
        {
            var m = await db.Matches.SingleOrDefaultAsync(x => x.Id == matchId, ct);
            if (m is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();

            var resolved = await ResolveAsync(r, club, clock, ct);
            if (!resolved.IsSuccess) return resolved.Error.ToProblem();
            var (division, venue) = (resolved.Value.Division, resolved.Value.Venue);

            var updated = m.Update(division.Id, division.Name, r.Opponent.Trim(), r.IsHome, venue.Id, venue.Name, Dates.ToUtc(r.StartsAtUtc));
            if (!updated.IsSuccess) return updated.Error.ToProblem();
            // TODO: publish MatchRescheduled so Participation can notify registered families.
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(m));
        });

        admin.MapPost("/{matchId:guid}/cancel", async (Guid matchId, MatchesDbContext db, IDomainEventDispatcher events, CancellationToken ct) =>
        {
            var m = await db.Matches.SingleOrDefaultAsync(x => x.Id == matchId, ct);
            if (m is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();

            var cancelled = m.Cancel();
            if (!cancelled.IsSuccess) return cancelled.Error.ToProblem();
            await db.SaveChangesAsync(ct);

            await events.DispatchAsync(m.DomainEvents, ct);   // Participation notifies the families
            m.ClearDomainEvents();
            return Results.Ok(ToDto(m));
        });

        admin.MapPut("/{matchId:guid}/result", async (Guid matchId, ResultRequest r, MatchesDbContext db, CancellationToken ct) =>
        {
            if (r.HomeScore < 0 || r.AwayScore < 0)
                return Error.Validation("score.invalid", "Scores cannot be negative.").ToProblem();
            var m = await db.Matches.SingleOrDefaultAsync(x => x.Id == matchId, ct);
            if (m is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();

            var set = m.SetResult(r.HomeScore, r.AwayScore);
            if (!set.IsSuccess) return set.Error.ToProblem();
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(m));
        });
    }

    private static async Task<Result<Resolved>> ResolveAsync(
        MatchRequest r, IClubApi club, TimeProvider clock, CancellationToken ct)
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
