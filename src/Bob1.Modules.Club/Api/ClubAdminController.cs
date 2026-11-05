using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Club.Api;

[Route(ApiRoutes.V1 + "/admin")]
[Authorize(Policy = Policies.Admin)]
[Tags("Club (admin)")]
internal sealed class ClubAdminController(ClubDbContext db) : ApiControllerBase
{
    // ---- Divisions ----

    [HttpPost("divisions")]
    public async Task<IActionResult> CreateDivision(DivisionRequest r, CancellationToken ct)
    {
        if (Guard.NotBlank(r.Name, "name", 50) is { } err) return err.ToProblem();
        if (Guard.NotBlank(r.Category, "category", 20) is { } err2) return err2.ToProblem();
        if (Guard.NotBlank(r.Gender, "gender", 20) is { } err3) return err3.ToProblem();
        var name = r.Name.Trim();
        if (await db.Divisions.AnyAsync(d => d.Name == name, ct))
            return Error.Conflict("division.name_taken", "A division with this name already exists.").ToProblem();
        var d = new Division { Name = name, SortOrder = r.SortOrder, Category = r.Category.Trim(), Gender = r.Gender.Trim(), IsActive = r.IsActive };
        db.Divisions.Add(d);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/divisions/{d.Id}", new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive));
    }

    [HttpPut("divisions/{id:guid}")]
    public async Task<IActionResult> UpdateDivision(Guid id, DivisionRequest r, CancellationToken ct)
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
        return Ok(new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive));
    }

    [HttpDelete("divisions/{id:guid}")]
    public async Task<IActionResult> DeleteDivision(Guid id, CancellationToken ct)
    {
        // NOTE: other modules reference divisions by id only (no cross-schema FK).
        // TODO: refuse deletion when children or matches still use it (ask IMatchesApi / IIdentityApi).
        var d = await db.Divisions.FindAsync([id], ct);
        if (d is null) return Error.NotFound("division.not_found", "Division not found.").ToProblem();
        if (await db.Teams.AnyAsync(t => t.DivisionId == id, ct))
            return Error.Conflict("division.has_teams", "Delete or move the teams of this division first.").ToProblem();
        db.Divisions.Remove(d);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Teams ----

    [HttpPost("teams")]
    public async Task<IActionResult> CreateTeam(TeamRequest r, CancellationToken ct)
    {
        if (Guard.NotBlank(r.Name, "name", 100) is { } err) return err.ToProblem();
        if (!await db.Divisions.AnyAsync(d => d.Id == r.DivisionId, ct))
            return Error.NotFound("division.not_found", "Division not found.").ToProblem();
        var t = new Team { Name = r.Name.Trim(), DivisionId = r.DivisionId, CoachName = r.CoachName?.Trim(), CoachUserId = r.CoachUserId, LogoUrl = r.LogoUrl?.Trim(), IsActive = r.IsActive, TrainingVenueId = r.TrainingVenueId };
        db.Teams.Add(t);
        await db.SaveChangesAsync(ct);
        var venueName = t.TrainingVenueId.HasValue ? await db.Venues.Where(v => v.Id == t.TrainingVenueId).Select(v => v.Name).FirstOrDefaultAsync(ct) : null;
        return Created($"/api/v1/teams/{t.Id}", new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.CoachUserId, t.LogoUrl, t.IsActive, t.TrainingVenueId, venueName));
    }

    [HttpPut("teams/{id:guid}")]
    public async Task<IActionResult> UpdateTeam(Guid id, TeamRequest r, CancellationToken ct)
    {
        if (Guard.NotBlank(r.Name, "name", 100) is { } err) return err.ToProblem();
        var t = await db.Teams.FindAsync([id], ct);
        if (t is null) return Error.NotFound("team.not_found", "Team not found.").ToProblem();
        if (!await db.Divisions.AnyAsync(d => d.Id == r.DivisionId, ct))
            return Error.NotFound("division.not_found", "Division not found.").ToProblem();
        t.Name = r.Name.Trim();
        t.DivisionId = r.DivisionId;
        t.CoachName = r.CoachName?.Trim();
        t.CoachUserId = r.CoachUserId;
        t.LogoUrl = r.LogoUrl?.Trim();
        t.IsActive = r.IsActive;
        t.TrainingVenueId = r.TrainingVenueId;
        await db.SaveChangesAsync(ct);
        var venueName = t.TrainingVenueId.HasValue ? await db.Venues.Where(v => v.Id == t.TrainingVenueId).Select(v => v.Name).FirstOrDefaultAsync(ct) : null;
        return Ok(new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.CoachUserId, t.LogoUrl, t.IsActive, t.TrainingVenueId, venueName));
    }

    [HttpDelete("teams/{id:guid}")]
    public async Task<IActionResult> DeleteTeam(Guid id, CancellationToken ct)
    {
        var t = await db.Teams.FindAsync([id], ct);
        if (t is null) return Error.NotFound("team.not_found", "Team not found.").ToProblem();
        db.Teams.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Venues ----

    [HttpPost("venues")]
    public async Task<IActionResult> CreateVenue(VenueRequest r, CancellationToken ct)
    {
        if (Guard.NotBlank(r.Name, "name", 150) is { } err) return err.ToProblem();
        var v = new Venue { Name = r.Name.Trim(), Address = r.Address?.Trim(), City = r.City?.Trim(), PostalCode = r.PostalCode?.Trim() };
        db.Venues.Add(v);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/venues/{v.Id}", new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode));
    }

    [HttpPut("venues/{id:guid}")]
    public async Task<IActionResult> UpdateVenue(Guid id, VenueRequest r, CancellationToken ct)
    {
        if (Guard.NotBlank(r.Name, "name", 150) is { } err) return err.ToProblem();
        var v = await db.Venues.FindAsync([id], ct);
        if (v is null) return Error.NotFound("venue.not_found", "Venue not found.").ToProblem();
        v.Name = r.Name.Trim();
        v.Address = r.Address?.Trim();
        v.City = r.City?.Trim();
        v.PostalCode = r.PostalCode?.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode));
    }

    [HttpDelete("venues/{id:guid}")]
    public async Task<IActionResult> DeleteVenue(Guid id, CancellationToken ct)
    {
        // TODO: refuse deletion when matches still reference it.
        var v = await db.Venues.FindAsync([id], ct);
        if (v is null) return Error.NotFound("venue.not_found", "Venue not found.").ToProblem();
        db.Venues.Remove(v);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
