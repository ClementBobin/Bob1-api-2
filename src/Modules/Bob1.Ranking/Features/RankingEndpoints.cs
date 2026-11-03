using Bob1.Identity.Public;
using Bob1.Notifications.Public;
using Bob1.Ranking.Domain;
using Bob1.Ranking.Infrastructure;

namespace Bob1.Ranking.Features;

internal sealed record PersonRankingRow(int Rank, Guid PersonId, string PersonName, string? FamilyName, int Points, bool IsMine);
internal sealed record FamilyRankingRow(int Rank, Guid FamilyId, string FamilyName, int Points, bool IsMine);
internal sealed record RankingResponse(string Season, IReadOnlyList<PersonRankingRow> Top, PersonRankingRow? Mine);
internal sealed record FamilyRankingResponse(string Season, IReadOnlyList<FamilyRankingRow> Top, FamilyRankingRow? Mine);
internal sealed record EntryDto(Guid Id, string Category, int Points, string Reason, Guid? MatchId, DateTime CreatedAtUtc, Guid PersonId, string PersonName);
internal sealed record RuleDto(string Key, string Category, string Label, int Points, int? PointsJ15, int? PointsJ4, bool HasJ15J4);
internal sealed record UpdateRuleRequest(int Points, string? Label, int? PointsJ15, int? PointsJ4);
internal sealed record PenaltyDto(Guid Id, Guid PersonId, string PersonName, int Points, string Reason, Guid? MatchId, string Season, DateTime CreatedAtUtc);
internal sealed record PenaltyRequest(Guid PersonId, string? RuleKey, int? Points, string? Reason, Guid? MatchId);
internal sealed record QuickPenaltyRequest(Guid PersonId, string RuleKey, Guid? MatchId);

internal static class RankingEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().WithTags("Ranking");

        // Person rankings by category
        g.MapGet("/rankings", (string? category, string? season, string? view, RankingDbContext db, IIdentityApi identity,
            ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            if (view == "family")
                return BuildFamilyRankingAsync(category, season, 50, db, identity, user, clock, ct);
            return BuildPersonRankingAsync(category, season, 50, db, identity, user, clock, ct);
        });

        // "Why do we have these points?": my person's recent ledger entries.
        g.MapGet("/rankings/me/entries", async (string? season, int? take, RankingDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            var personId = user.PersonId;
            var s = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
            var rows = await db.PointEntries.AsNoTracking().Where(p => p.PersonId == personId && p.Season == s)
                .OrderByDescending(p => p.CreatedAtUtc).Take(Math.Clamp(take ?? 50, 1, 200))
                .Select(p => new EntryDto(p.Id, p.Category, p.Points, p.Reason, p.MatchId, p.CreatedAtUtc, p.PersonId, "")).ToListAsync(ct);
            return Results.Ok(rows);
        });

        // The rules screen (read-only for everyone).
        g.MapGet("/ranking-rules", async (RankingDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Rules.AsNoTracking().OrderBy(r => r.Category).ThenBy(r => r.Key)
                .Select(r => new RuleDto(r.Key, r.Category, r.Label, r.Points, r.PointsJ15, r.PointsJ4, r.HasJ15J4)).ToListAsync(ct)));

        // ---- Admin ----
        var admin = api.MapGroup("/admin").RequireAuthorization(Policies.Admin).WithTags("Ranking (admin)");

        // Person rankings (admin view)
        admin.MapGet("/rankings", (string? category, string? season, string? view, RankingDbContext db, IIdentityApi identity,
            ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            if (view == "family")
                return BuildFamilyRankingAsync(category, season, 500, db, identity, user, clock, ct);
            return BuildPersonRankingAsync(category, season, 500, db, identity, user, clock, ct);
        });

        admin.MapPut("/ranking-rules/{key}", async (string key, UpdateRuleRequest r, RankingDbContext db, CancellationToken ct) =>
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
            return Results.Ok(new RuleDto(rule.Key, rule.Category, rule.Label, rule.Points, rule.PointsJ15, rule.PointsJ4, rule.HasJ15J4));
        });

        admin.MapGet("/penalties", async (Guid? personId, string? season, RankingDbContext db, IIdentityApi identity, TimeProvider clock, CancellationToken ct) =>
        {
            var s = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
            var q = db.PointEntries.AsNoTracking().Where(p => p.Category == "penalty" && p.Season == s);
            if (personId is { } f) q = q.Where(p => p.PersonId == f);

            var rows = await q.OrderByDescending(p => p.CreatedAtUtc).Take(200).ToListAsync(ct);
            var personNames = await identity.GetPersonNamesAsync(rows.Select(p => p.PersonId), ct);
            return Results.Ok(rows.Select(p => new PenaltyDto(p.Id, p.PersonId, personNames.GetValueOrDefault(p.PersonId, "?"),
                p.Points, p.Reason, p.MatchId, p.Season, p.CreatedAtUtc)));
        });

        // Free-form penalty (or bonus): either reference a rule, or give points + reason.
        admin.MapPost("/penalties", (PenaltyRequest r, PointsService points, RankingDbContext db, IIdentityApi identity,
            INotificationsApi notifications, ICurrentUser user, CancellationToken ct) =>
            ApplyPenaltyAsync(r, points, db, identity, notifications, user, ct));

        // One-tap penalty from a rule (no-show, late cancellation...).
        admin.MapPost("/penalties/quick", (QuickPenaltyRequest r, PointsService points, RankingDbContext db, IIdentityApi identity,
            INotificationsApi notifications, ICurrentUser user, CancellationToken ct) =>
            ApplyPenaltyAsync(new PenaltyRequest(r.PersonId, r.RuleKey, null, null, r.MatchId), points, db, identity, notifications, user, ct));

        admin.MapPost("/penalties/{id:guid}/revoke", async (Guid id, RankingDbContext db, PointsService points, CancellationToken ct) =>
        {
            var entry = await db.PointEntries.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Category == "penalty", ct);
            if (entry is null) return Error.NotFound("penalty.not_found", "Penalty not found.").ToProblem();
            await points.RevokeAsync(entry.SourceKey, $"revoke:{id}", ct);
            return Results.NoContent();
        });

        // Points history - all entries for a person
        admin.MapGet("/points-history", async (Guid? personId, string? category, string? season, int? skip, int? take, RankingDbContext db, IIdentityApi identity, TimeProvider clock, CancellationToken ct) =>
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
            return Results.Ok(new { total, items });
        });
    }

    private static async Task<IResult> BuildPersonRankingAsync(string? category, string? season, int take, RankingDbContext db,
        IIdentityApi identity, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var seasonKey = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var q = db.PointEntries.AsNoTracking().Where(p => p.Season == seasonKey);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(p => p.Category == category);

        var totals = await q.GroupBy(p => p.PersonId)
            .Select(g => new { PersonId = g.Key, Points = g.Sum(p => p.Points) })
            .OrderByDescending(x => x.Points).ThenBy(x => x.PersonId)
            .ToListAsync(ct);

        var wanted = totals.Take(take).Select(t => t.PersonId).ToList();
        if (user.PersonId is { } mine) wanted.Add(mine);
        var names = await identity.GetPersonNamesAsync(wanted, ct);
        var familyNames = await identity.GetFamilyNamesForPersonsAsync(wanted, ct);

        var rows = totals.Select((t, i) => new PersonRankingRow(i + 1, t.PersonId, names.GetValueOrDefault(t.PersonId, "?"),
            familyNames.GetValueOrDefault(t.PersonId), t.Points, t.PersonId == user.PersonId)).ToList();

        return Results.Ok(new RankingResponse(seasonKey, rows.Take(take).ToList(), rows.FirstOrDefault(r => r.IsMine)));
    }

    private static async Task<IResult> BuildFamilyRankingAsync(string? category, string? season, int take, RankingDbContext db,
        IIdentityApi identity, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var seasonKey = season ?? Season.For(clock.GetUtcNow().UtcDateTime);
        var q = db.PointEntries.AsNoTracking().Where(p => p.Season == seasonKey);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(p => p.Category == category);

        // Get person IDs and their family IDs
        var personFamilyMap = await identity.GetPersonFamilyMapAsync(ct);
        
        var totals = await q.GroupBy(p => p.PersonId)
            .Select(g => new { PersonId = g.Key, Points = g.Sum(p => p.Points) })
            .ToListAsync(ct);

        // Aggregate by family (filter out null family IDs)
        var familyTotals = totals
            .Where(t => personFamilyMap.TryGetValue(t.PersonId, out var fid) && fid.HasValue)
            .GroupBy(t => personFamilyMap[t.PersonId]!.Value)
            .Select(g => new { FamilyId = g.Key, Points = g.Sum(x => x.Points) })
            .OrderByDescending(x => x.Points).ThenBy(x => x.FamilyId)
            .ToList();

        var wanted = familyTotals.Take(take).Select(t => t.FamilyId).ToList();
        if (user.FamilyId is { } mine) wanted.Add(mine);
        var names = await identity.GetFamilyNamesAsync(wanted, ct);

        var rows = familyTotals.Select((t, i) => new FamilyRankingRow(i + 1, t.FamilyId, names.GetValueOrDefault(t.FamilyId, "?"),
            t.Points, t.FamilyId == user.FamilyId)).ToList();

        return Results.Ok(new FamilyRankingResponse(seasonKey, rows.Take(take).ToList(), rows.FirstOrDefault(r => r.IsMine)));
    }

    private static async Task<IResult> ApplyPenaltyAsync(PenaltyRequest r, PointsService points, RankingDbContext db,
        IIdentityApi identity, INotificationsApi notifications, ICurrentUser admin, CancellationToken ct)
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

        var entry = await points.AddManualAsync(r.PersonId, "penalty", pts, reason, r.MatchId, admin.UserId, ct);

        await notifications.SendToPersonAsync(r.PersonId, pts < 0 ? "Pénalité" : "Bonus",
            $"{(pts > 0 ? "+" : "")}{pts} pts : {reason}",
            new Dictionary<string, string> { ["type"] = "ranking_entry" }, ct);

        return Results.Created($"/api/v1/admin/penalties?personId={r.PersonId}",
            new EntryDto(entry.Id, entry.Category, entry.Points, entry.Reason, entry.MatchId, entry.CreatedAtUtc, entry.PersonId, ""));
    }
}