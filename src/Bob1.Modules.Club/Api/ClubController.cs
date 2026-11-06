using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Club.Api;

internal sealed record DivisionDto(Guid Id, string Name, int SortOrder, string Category, string Gender, bool IsActive);
internal sealed record TeamDto(Guid Id, string Name, Guid DivisionId, string? CoachName, Guid? CoachUserId, string? LogoUrl, bool IsActive, Guid? TrainingVenueId, string? TrainingVenueName);
internal sealed record VenueDto(Guid Id, string Name, string? Address, string? City, string? PostalCode);

internal sealed record DivisionQuery(string? Search, bool? IsActive, int? Skip, int? Take);
internal sealed record TeamQuery(string? Search, Guid? DivisionId, bool? IsActive, int? Skip, int? Take);
internal sealed record VenueQuery(string? Search, int? Skip, int? Take);

internal sealed record DivisionRequest(string Name, int SortOrder, string Category, string Gender, bool IsActive);
internal sealed record TeamRequest(string Name, Guid DivisionId, string? CoachName, Guid? CoachUserId, string? LogoUrl, bool IsActive, Guid? TrainingVenueId);
internal sealed record VenueRequest(string Name, string? Address, string? City, string? PostalCode);

/// <summary>Read access to reference data (any authenticated user).</summary>
[Route(ApiRoutes.V1)]
[Authorize]
[Tags("Club")]
internal sealed class ClubController(ClubDbContext db) : ApiControllerBase
{
    [HttpQuery("divisions")]
    public async Task<IActionResult> Divisions([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DivisionQuery? r, CancellationToken ct)
    {
        r ??= new(null, null, null, null);
        var q = db.Divisions.AsNoTracking();
        if (Paging.Term(r.Search) is { } s)
            q = q.Where(d => d.Name.ToLower().Contains(s) || d.Category.ToLower().Contains(s) || d.Gender.ToLower().Contains(s));
        if (r.IsActive is { } active) q = q.Where(d => d.IsActive == active);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Skip(Paging.Skip(r.Skip)).Take(Paging.Take(r.Take))
            .Select(d => new DivisionDto(d.Id, d.Name, d.SortOrder, d.Category, d.Gender, d.IsActive)).ToListAsync(ct);
        return Ok(new PagedResult<DivisionDto>(total, items));
    }

    [HttpQuery("teams")]
    public async Task<IActionResult> Teams([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TeamQuery? r, CancellationToken ct)
    {
        r ??= new(null, null, null, null, null);
        var q = db.Teams.AsNoTracking();
        if (r.DivisionId is { } d) q = q.Where(t => t.DivisionId == d);
        if (r.IsActive is { } active) q = q.Where(t => t.IsActive == active);
        if (Paging.Term(r.Search) is { } s)
            q = q.Where(t => t.Name.ToLower().Contains(s) || (t.CoachName != null && t.CoachName.ToLower().Contains(s)));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(t => t.Name).Skip(Paging.Skip(r.Skip)).Take(Paging.Take(r.Take)).Select(t => new TeamDto(t.Id, t.Name, t.DivisionId, t.CoachName, t.CoachUserId, t.LogoUrl, t.IsActive, t.TrainingVenueId, t.TrainingVenueId.HasValue ? db.Venues.Where(v => v.Id == t.TrainingVenueId).Select(v => v.Name).FirstOrDefault() : null)).ToListAsync(ct);
        return Ok(new PagedResult<TeamDto>(total, items));
    }

    [HttpQuery("venues")]
    public async Task<IActionResult> Venues([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VenueQuery? r, CancellationToken ct)
    {
        r ??= new(null, null, null);
        var q = db.Venues.AsNoTracking();
        if (Paging.Term(r.Search) is { } s)
            q = q.Where(v => v.Name.ToLower().Contains(s)
                             || (v.City != null && v.City.ToLower().Contains(s))
                             || (v.Address != null && v.Address.ToLower().Contains(s)));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(v => v.Name).Skip(Paging.Skip(r.Skip)).Take(Paging.Take(r.Take))
            .Select(v => new VenueDto(v.Id, v.Name, v.Address, v.City, v.PostalCode)).ToListAsync(ct);
        return Ok(new PagedResult<VenueDto>(total, items));
    }
}
