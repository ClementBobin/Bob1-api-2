using Bob1.Shared.Contracts.Club;

namespace Bob1.Modules.Club.Application;

internal sealed class ClubApi(ClubDbContext db) : IClubApi
{
    public Task<DivisionInfo?> GetDivisionAsync(Guid id, CancellationToken ct) =>
        db.Divisions.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new DivisionInfo(d.Id, d.Name)).SingleOrDefaultAsync(ct);

    public Task<VenueInfo?> GetVenueAsync(Guid id, CancellationToken ct) =>
        db.Venues.AsNoTracking().Where(v => v.Id == id)
            .Select(v => new VenueInfo(v.Id, v.Name)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDivisionNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Divisions.AsNoTracking().Where(d => list.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);
    }
}
