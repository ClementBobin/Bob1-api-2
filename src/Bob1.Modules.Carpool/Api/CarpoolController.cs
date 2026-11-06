using Bob1.Shared.Contracts.Identity;
using Bob1.Shared.Contracts.Matches;
using Bob1.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.Sqlite;

namespace Bob1.Modules.Carpool.Api;

internal sealed record OfferRideRequest(int Seats, DateTime DepartureUtc, string MeetingPoint, string? Notes);
internal sealed record BookRideRequest(int? Seats);
internal sealed record RideDto(
    Guid Id, string DriverName, int Seats, int SeatsLeft, DateTime DepartureUtc, string MeetingPoint, string? Notes,
    bool IsMine, bool BookedByMe);

[Route(ApiRoutes.V1)]
[Authorize]
[Tags("Carpool")]
internal sealed class CarpoolController(CarpoolDbContext db, ICurrentUser user, TimeProvider clock) : ApiControllerBase
{
    private const int MaxRetries = 3;

    [HttpQuery("matches/{matchId:guid}/rides")]
    public async Task<IActionResult> ListRides(Guid matchId, [FromServices] IIdentityApi identity, CancellationToken ct)
    {
        var rides = await db.Rides.AsNoTracking().Include(r => r.Bookings)
            .Where(r => r.MatchId == matchId && !r.IsCancelled).OrderBy(r => r.DepartureUtc).ToListAsync(ct);
        var names = await identity.GetUserNamesAsync(rides.Select(r => r.DriverUserId), ct);

        return Ok(rides.Select(r => new RideDto(
            r.Id, names.GetValueOrDefault(r.DriverUserId, "?"), r.Seats, r.SeatsLeft, r.DepartureUtc, r.MeetingPoint, r.Notes,
            r.DriverUserId == user.UserId, r.Bookings.Any(b => b.UserId == user.UserId))));
    }

    [HttpPost("matches/{matchId:guid}/rides")]
    public async Task<IActionResult> OfferRide(Guid matchId, OfferRideRequest r,
        [FromServices] IMatchesApi matches, [FromServices] IDomainEventDispatcher events, CancellationToken ct)
    {
        if (Guard.First(Guard.InRange(r.Seats, "seats", 1, 8), Guard.NotBlank(r.MeetingPoint, "meeting_point")) is { } err)
            return err.ToProblem();

        var now = clock.GetUtcNow().UtcDateTime;
        var match = await matches.GetAsync(matchId, ct);
        if (match is null) return Error.NotFound("match.not_found", "Match not found.").ToProblem();
        if (match.Status != MatchStatus.Scheduled || match.StartsAtUtc <= now)
            return Error.Conflict("match.closed", "This match is no longer open.").ToProblem();

        var ride = Ride.Offer(matchId, user.UserId, user.FamilyId, r.Seats, Dates.ToUtc(r.DepartureUtc),
            r.MeetingPoint.Trim(), r.Notes?.Trim(), now);
        db.Rides.Add(ride);
        await db.SaveChangesAsync(ct);

        await events.DispatchAsync(ride.DomainEvents, ct);
        ride.ClearDomainEvents();
        return Created($"/api/v1/matches/{matchId}/rides", new { id = ride.Id });
    }

    [HttpPut("rides/{id:guid}")]
    public async Task<IActionResult> UpdateRide(Guid id, OfferRideRequest r, CancellationToken ct)
    {
        if (Guard.First(Guard.InRange(r.Seats, "seats", 1, 8), Guard.NotBlank(r.MeetingPoint, "meeting_point")) is { } err)
            return err.ToProblem();

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var ride = await db.Rides.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (ride is null || ride.DriverUserId != user.UserId) return Error.NotFound("ride.not_found", "Ride not found.").ToProblem();

            var updated = ride.Update(r.Seats, Dates.ToUtc(r.DepartureUtc), r.MeetingPoint.Trim(), r.Notes?.Trim());
            if (!updated.IsSuccess) return updated.Error.ToProblem();

            try { await db.SaveChangesAsync(ct); return NoContent(); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        }
        return Error.Conflict("ride.contention", "The ride was modified concurrently, please retry.").ToProblem();
    }

    [HttpDelete("rides/{id:guid}")]
    public async Task<IActionResult> CancelRide(Guid id,
        [FromServices] IDomainEventDispatcher events, [FromServices] INotificationsApi notifications, CancellationToken ct)
    {
        var ride = await db.Rides.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ride is null || ride.DriverUserId != user.UserId) return Error.NotFound("ride.not_found", "Ride not found.").ToProblem();

        var passengers = ride.Bookings.Select(b => b.UserId).ToList();
        ride.Cancel();
        await db.SaveChangesAsync(ct);

        await events.DispatchAsync(ride.DomainEvents, ct);
        ride.ClearDomainEvents();
        await notifications.SendToUsersAsync(passengers, "Covoiturage annulé",
            "Le conducteur a annulé le covoiturage que vous aviez réservé.",
            new Dictionary<string, string> { ["type"] = "ride_cancelled", ["matchId"] = ride.MatchId.ToString() }, ct);
        return NoContent();
    }

    [HttpPost("rides/{id:guid}/booking")]
    public async Task<IActionResult> Book(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookRideRequest? r, CancellationToken ct)
    {
        var seats = r?.Seats ?? 1;
        if (Guard.InRange(seats, "seats", 1, 8) is { } err) return err.ToProblem();

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var ride = await db.Rides.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (ride is null) return Error.NotFound("ride.not_found", "Ride not found.").ToProblem();
            if (ride.DepartureUtc <= clock.GetUtcNow().UtcDateTime)
                return Error.Conflict("ride.departed", "This ride has already departed.").ToProblem();

            var booked = ride.Book(user.UserId, user.FamilyId, seats, clock.GetUtcNow().UtcDateTime);
            if (!booked.IsSuccess) return booked.Error.ToProblem();

            try { await db.SaveChangesAsync(ct); return NoContent(); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }   // someone else booked first: re-check seats
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }) // UNIQUE / PRIMARY KEY
            {
                return Error.Conflict("ride.already_booked", "You already booked this ride.").ToProblem();
            }
        }
        return Error.Conflict("ride.contention", "Too many simultaneous bookings, please retry.").ToProblem();
    }

    [HttpDelete("rides/{id:guid}/booking")]
    public async Task<IActionResult> CancelBooking(Guid id, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var ride = await db.Rides.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (ride is null) return Error.NotFound("ride.not_found", "Ride not found.").ToProblem();

            var booking = ride.Bookings.SingleOrDefault(b => b.UserId == user.UserId);
            var cancelled = ride.CancelBooking(user.UserId);
            if (!cancelled.IsSuccess) return cancelled.Error.ToProblem();
            if (booking is not null) db.Bookings.Remove(booking);

            try { await db.SaveChangesAsync(ct); return NoContent(); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        }
        return Error.Conflict("ride.contention", "Please retry.").ToProblem();
    }
}
