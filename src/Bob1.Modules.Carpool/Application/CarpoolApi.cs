using Bob1.Shared.Contracts.Carpool;

namespace Bob1.Modules.Carpool.Application;

internal sealed class CarpoolApi(CarpoolDbContext db) : ICarpoolApi
{
    public async Task<IReadOnlyList<CarpoolCommitment>> GetCommitmentsAsync(Guid userId, DateTime fromUtc, CancellationToken ct)
    {
        var driving = await db.Rides.AsNoTracking()
            .Where(r => r.DriverUserId == userId && !r.IsCancelled && r.DepartureUtc >= fromUtc)
            .Select(r => new CarpoolCommitment(r.Id, r.MatchId, true, r.Seats, r.DepartureUtc, r.MeetingPoint))
            .ToListAsync(ct);

        var riding = await db.Rides.AsNoTracking()
            .Where(r => !r.IsCancelled && r.DepartureUtc >= fromUtc && r.Bookings.Any(b => b.UserId == userId))
            .Select(r => new CarpoolCommitment(r.Id, r.MatchId, false,
                r.Bookings.Where(b => b.UserId == userId).Sum(b => b.Seats), r.DepartureUtc, r.MeetingPoint))
            .ToListAsync(ct);

        return driving.Concat(riding).OrderBy(c => c.DepartureUtc).ToList();
    }
}
