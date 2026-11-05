using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Club.Api;

internal sealed record DivisionDto(Guid Id, string Name, int SortOrder, string Category, string Gender, bool IsActive);
internal sealed record TeamDto(Guid Id, string Name, Guid DivisionId, string? CoachName, Guid? CoachUserId, string? LogoUrl, bool IsActive, Guid? TrainingVenueId, string? TrainingVenueName);
internal sealed record VenueDto(Guid Id, string Name, string? Address, string? City, string? PostalCode);

internal sealed record DivisionRequest(string Name, int SortOrder, string Category, string Gender, bool IsActive);
internal sealed record TeamRequest(string Name, Guid DivisionId, string? CoachName, Guid? CoachUserId, string? LogoUrl, bool IsActive, Guid? TrainingVenueId);
internal sealed record VenueRequest(string Name, string? Address, string? City, string? PostalCode);

/// <summary>Read access to reference data (any authenticated user).</summary>
[Route(ApiRoutes.V1)]
[Authorize]
[Tags("Club")]
internal sealed class ClubController(ClubDbContext db) : ApiControllerBase
{
    [HttpGet("divisions")]
    public async Task<IActionResult> Divisions(CancellationToken ct) =>
        Ok(await db.Divisions.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive)).ToListAsync(ct));

    [HttpGet("teams")]
    public async Task<IActionResult> Teams(Guid? divisionId, CancellationToken ct)
    {
        var q = db.Teams.AsNoTracking();
        if (divisionId is { } d) q = q.Where(t => t.DivisionId == d);
        return Ok(await q.OrderBy(t => t.Name).Select(t => new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.CoachUserId, t.LogoUrl, t.IsActive, t.TrainingVenueId, t.TrainingVenueId.HasValue ? db.Venues.Where(v => v.Id == t.TrainingVenueId).Select(v => v.Name).FirstOrDefault() : null)).ToListAsync(ct));
    }

    [HttpGet("venues")]
    public async Task<IActionResult> Venues(CancellationToken ct) =>
        Ok(await db.Venues.AsNoTracking().OrderBy(v => v.Name)
            .Select(v => new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode)).ToListAsync(ct));
}
