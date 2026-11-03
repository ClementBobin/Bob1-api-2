using Bob1.Club.Domain;
using Bob1.Club.Infrastructure;

namespace Bob1.Club.Features;

internal sealed record DivisionDto(Guid Id, string Name, int SortOrder, string Category, string Gender, bool IsActive);
internal sealed record TeamDto(Guid Id, string Name, Guid DivisionId, string? CoachName, string? LogoUrl, bool IsActive, string? TrainingLocation);
internal sealed record VenueDto(Guid Id, string Name, string? Address, string? City, string? PostalCode);

internal sealed record DivisionRequest(string Name, int SortOrder, string Category, string Gender, bool IsActive);
internal sealed record TeamRequest(string Name, Guid DivisionId, string? CoachName, string? LogoUrl, bool IsActive, string? TrainingLocation);
internal sealed record VenueRequest(string Name, string? Address, string? City, string? PostalCode);

internal static class ClubEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // ---- Read (any authenticated user) ----
        var read = api.MapGroup("").RequireAuthorization().WithTags("Club");

        read.MapGet("/divisions", async (ClubDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Divisions.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
                .Select(d => new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive)).ToListAsync(ct)));

        read.MapGet("/teams", async (Guid? divisionId, ClubDbContext db, CancellationToken ct) =>
        {
            var q = db.Teams.AsNoTracking();
            if (divisionId is { } d) q = q.Where(t => t.DivisionId == d);
            return Results.Ok(await q.OrderBy(t => t.Name).Select(t => new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.LogoUrl, t.IsActive, t.TrainingLocation)).ToListAsync(ct));
        });

        read.MapGet("/venues", async (ClubDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Venues.AsNoTracking().OrderBy(v => v.Name)
                .Select(v => new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode)).ToListAsync(ct)));

        // ---- Admin ----
        var admin = api.MapGroup("/admin").RequireAuthorization(Policies.Admin).WithTags("Club (admin)");

        // Divisions
        admin.MapPost("/divisions", async (DivisionRequest r, ClubDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 50) is { } err) return err.ToProblem();
            if (Guard.NotBlank(r.Category, "category", 20) is { } err2) return err2.ToProblem();
            if (Guard.NotBlank(r.Gender, "gender", 20) is { } err3) return err3.ToProblem();
            if (await db.Divisions.AnyAsync(d => d.Name == r.Name.Trim(), ct))
                return Error.Conflict("division.name_taken", "A division with this name already exists.").ToProblem();
            var d = new Division { Name = r.Name.Trim(), SortOrder = r.SortOrder, Category = r.Category.Trim(), Gender = r.Gender.Trim(), IsActive = r.IsActive };
            db.Divisions.Add(d);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/divisions/{d.Id}", new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive));
        });

        admin.MapPut("/divisions/{id:guid}", async (Guid id, DivisionRequest r, ClubDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 50) is { } err) return err.ToProblem();
            if (Guard.NotBlank(r.Category, "category", 20) is { } err2) return err2.ToProblem();
            if (Guard.NotBlank(r.Gender, "gender", 20) is { } err3) return err3.ToProblem();
            var d = await db.Divisions.FindAsync([id], ct);
            if (d is null) return Error.NotFound("division.not_found", "Division not found.").ToProblem();
            d.Name = r.Name.Trim();
            d.SortOrder = r.SortOrder;
            d.Category = r.Category.Trim();
            d.Gender = r.Gender.Trim();
            d.IsActive = r.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive));
        });

        admin.MapDelete("/divisions/{id:guid}", async (Guid id, ClubDbContext db, CancellationToken ct) =>
        {
            // NOTE: other modules reference divisions by id only (no cross-schema FK).
            // TODO: refuse deletion when children or matches still use it (ask IMatchesApi / IIdentityApi).
            var d = await db.Divisions.FindAsync([id], ct);
            if (d is null) return Error.NotFound("division.not_found", "Division not found.").ToProblem();
            if (await db.Teams.AnyAsync(t => t.DivisionId == id, ct))
                return Error.Conflict("division.has_teams", "Delete or move the teams of this division first.").ToProblem();
            db.Divisions.Remove(d);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Teams
        admin.MapPost("/teams", async (TeamRequest r, ClubDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 100) is { } err) return err.ToProblem();
            if (!await db.Divisions.AnyAsync(d => d.Id == r.DivisionId, ct))
                return Error.NotFound("division.not_found", "Division not found.").ToProblem();
            var t = new Team { Name = r.Name.Trim(), DivisionId = r.DivisionId, CoachName = r.CoachName?.Trim(), LogoUrl = r.LogoUrl?.Trim(), IsActive = r.IsActive, TrainingLocation = r.TrainingLocation?.Trim() };
            db.Teams.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/teams/{t.Id}", new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.LogoUrl, t.IsActive, t.TrainingLocation));
        });

        admin.MapPut("/teams/{id:guid}", async (Guid id, TeamRequest r, ClubDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 100) is { } err) return err.ToProblem();
            var t = await db.Teams.FindAsync([id], ct);
            if (t is null) return Error.NotFound("team.not_found", "Team not found.").ToProblem();
            if (!await db.Divisions.AnyAsync(d => d.Id == r.DivisionId, ct))
                return Error.NotFound("division.not_found", "Division not found.").ToProblem();
            t.Name = r.Name.Trim();
            t.DivisionId = r.DivisionId;
            t.CoachName = r.CoachName?.Trim();
            t.LogoUrl = r.LogoUrl?.Trim();
            t.IsActive = r.IsActive;
            t.TrainingLocation = r.TrainingLocation?.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.LogoUrl, t.IsActive, t.TrainingLocation));
        });

        admin.MapDelete("/teams/{id:guid}", async (Guid id, ClubDbContext db, CancellationToken ct) =>
        {
            var t = await db.Teams.FindAsync([id], ct);
            if (t is null) return Error.NotFound("team.not_found", "Team not found.").ToProblem();
            db.Teams.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Venues
        admin.MapPost("/venues", async (VenueRequest r, ClubDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 150) is { } err) return err.ToProblem();
            var v = new Venue { Name = r.Name.Trim(), Address = r.Address?.Trim(), City = r.City?.Trim(), PostalCode = r.PostalCode?.Trim() };
            db.Venues.Add(v);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/venues/{v.Id}", new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode));
        });

        admin.MapPut("/venues/{id:guid}", async (Guid id, VenueRequest r, ClubDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 150) is { } err) return err.ToProblem();
            var v = await db.Venues.FindAsync([id], ct);
            if (v is null) return Error.NotFound("venue.not_found", "Venue not found.").ToProblem();
            v.Name = r.Name.Trim();
            v.Address = r.Address?.Trim();
            v.City = r.City?.Trim();
            v.PostalCode = r.PostalCode?.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode));
        });

        admin.MapDelete("/venues/{id:guid}", async (Guid id, ClubDbContext db, CancellationToken ct) =>
        {
            // TODO: refuse deletion when matches still reference it.
            var v = await db.Venues.FindAsync([id], ct);
            if (v is null) return Error.NotFound("venue.not_found", "Venue not found.").ToProblem();
            db.Venues.Remove(v);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
