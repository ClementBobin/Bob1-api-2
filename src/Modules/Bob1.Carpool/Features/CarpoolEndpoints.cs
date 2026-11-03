using Bob1.Carpool.Domain;
using Bob1.Carpool.Infrastructure;
using Bob1.Identity.Public;
using Bob1.Matches.Public;
using Bob1.Notifications.Public;
using Microsoft.Data.Sqlite;

namespace Bob1.Carpool.Features;

internal sealed record OfferRideRequest(int Seats, DateTime DepartureUtc, string MeetingPoint, string? Notes);
internal sealed record BookRideRequest(int? Seats);
internal sealed record RideDto(
    Guid Id, string DriverName, int Seats, int SeatsLeft, DateTime DepartureUtc, string MeetingPoint, string? Notes,
    bool IsMine, bool BookedByMe);

internal static class CarpoolEndpoints
{
    private const int MaxRetries = 3;

    public static void Map(IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization().WithTags("Carpool");

        g.MapGet("/matches/{matchId:guid}/rides", async (Guid matchId, CarpoolDbContext db, IIdentityApi identity, ICurrentUser user, CancellationToken ct) =>
        {
            var rides = await db.Rides.AsNoTracking().Include(r => r.Bookings)
                .Where(r => r.MatchId == matchId && !r.IsCancelled).OrderBy(r => r.DepartureUtc).ToListAsync(ct);
            var names = await identity.GetUserNamesAsync(rides.Select(r => r.DriverUserId), ct);

            return Results.Ok(rides.Select(r => new RideDto(
                r.Id, names.GetValueOrDefault(r.DriverUserId, "?"), r.Seats, r.SeatsLeft, r.DepartureUtc, r.MeetingPoint, r.Notes,
                r.DriverUserId == user.UserId, r.Bookings.Any(b => b.UserId == user.UserId))));
        });

        g.MapPost("/matches/{matchId:guid}/rides", async (Guid matchId, OfferRideRequest r, CarpoolDbContext db, IMatchesApi matches,
            ICurrentUser user, IDomainEventDispatcher events, TimeProvider clock, CancellationToken ct) =>
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
            return Results.Created($"/api/v1/matches/{matchId}/rides", new { id = ride.Id });
        });

        g.MapPut("/rides/{id:guid}", async (Guid id, OfferRideRequest r, CarpoolDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            if (Guard.First(Guard.InRange(r.Seats, "seats", 1, 8), Guard.NotBlank(r.MeetingPoint, "meeting_point")) is { } err)
                return err.ToProblem();

            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                var ride = await db.Rides.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
                if (ride is null || ride.DriverUserId != user.UserId) return Error.NotFound("ride.not_found", "Ride not found.").ToProblem();

                var updated = ride.Update(r.Seats, Dates.ToUtc(r.DepartureUtc), r.MeetingPoint.Trim(), r.Notes?.Trim());
                if (!updated.IsSuccess) return updated.Error.ToProblem();

                try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
                catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            }
            return Error.Conflict("ride.contention", "The ride was modified concurrently, please retry.").ToProblem();
        });

        g.MapDelete("/rides/{id:guid}", async (Guid id, CarpoolDbContext db, ICurrentUser user, IDomainEventDispatcher events,
            INotificationsApi notifications, CancellationToken ct) =>
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
            return Results.NoContent();
        });

        g.MapPost("/rides/{id:guid}/booking", async (Guid id, BookRideRequest? r, CarpoolDbContext db, ICurrentUser user,
            TimeProvider clock, CancellationToken ct) =>
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

                try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
                catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }   // someone else booked first: re-check seats
                catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }) // UNIQUE / PRIMARY KEY
                {
                    return Error.Conflict("ride.already_booked", "You already booked this ride.").ToProblem();
                }
            }
            return Error.Conflict("ride.contention", "Too many simultaneous bookings, please retry.").ToProblem();
        });

        g.MapDelete("/rides/{id:guid}/booking", async (Guid id, CarpoolDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                var ride = await db.Rides.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
                if (ride is null) return Error.NotFound("ride.not_found", "Ride not found.").ToProblem();

                var booking = ride.Bookings.SingleOrDefault(b => b.UserId == user.UserId);
                var cancelled = ride.CancelBooking(user.UserId);
                if (!cancelled.IsSuccess) return cancelled.Error.ToProblem();
                if (booking is not null) db.Bookings.Remove(booking);

                try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
                catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            }
            return Error.Conflict("ride.contention", "Please retry.").ToProblem();
        });
    }
}
