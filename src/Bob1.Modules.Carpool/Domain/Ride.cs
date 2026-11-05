using Bob1.Shared.Contracts.Carpool;

namespace Bob1.Modules.Carpool.Domain;

internal sealed class Ride : Entity
{
    private readonly List<RideBooking> _bookings = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public Guid DriverUserId { get; private set; }
    public Guid? DriverFamilyId { get; private set; }
    public int Seats { get; private set; }
    public int SeatsTaken { get; private set; }
    public DateTime DepartureUtc { get; private set; }
    public string MeetingPoint { get; private set; } = default!;
    public string? Notes { get; private set; }
    public bool IsCancelled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<RideBooking> Bookings => _bookings;
    public int SeatsLeft => Seats - SeatsTaken;

    private Ride() { }

    public static Ride Offer(Guid matchId, Guid driverUserId, Guid? driverFamilyId, int seats,
        DateTime departureUtc, string meetingPoint, string? notes, DateTime nowUtc)
    {
        var ride = new Ride
        {
            MatchId = matchId, DriverUserId = driverUserId, DriverFamilyId = driverFamilyId, Seats = seats,
            DepartureUtc = departureUtc, MeetingPoint = meetingPoint, Notes = notes, CreatedAtUtc = nowUtc
        };
        ride.Raise(new RideOffered(ride.Id, matchId, driverUserId, driverFamilyId));
        return ride;
    }

    public Result Update(int seats, DateTime departureUtc, string meetingPoint, string? notes)
    {
        if (IsCancelled) return Error.Conflict("ride.cancelled", "This ride is cancelled.");
        if (seats < SeatsTaken)
            return Error.Conflict("ride.seats_below_booked", $"{SeatsTaken} seat(s) are already booked.");
        Seats = seats; DepartureUtc = departureUtc; MeetingPoint = meetingPoint; Notes = notes;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (IsCancelled) return Result.Success();
        IsCancelled = true;
        Raise(new RideCancelled(Id, MatchId, DriverUserId, DriverFamilyId));
        return Result.Success();
    }

    public Result Book(Guid userId, Guid? familyId, int seats, DateTime nowUtc)
    {
        if (IsCancelled) return Error.Conflict("ride.cancelled", "This ride is cancelled.");
        if (userId == DriverUserId) return Error.Conflict("ride.own_ride", "You cannot book your own ride.");
        if (_bookings.Any(b => b.UserId == userId)) return Error.Conflict("ride.already_booked", "You already booked this ride.");
        if (seats > SeatsLeft) return Error.Conflict("ride.full", "Not enough seats left.");

        _bookings.Add(RideBooking.Create(Id, userId, familyId, seats, nowUtc));
        SeatsTaken += seats;
        return Result.Success();
    }

    public Result CancelBooking(Guid userId)
    {
        var booking = _bookings.SingleOrDefault(b => b.UserId == userId);
        if (booking is null) return Error.NotFound("booking.not_found", "You have no booking on this ride.");
        _bookings.Remove(booking);
        SeatsTaken -= booking.Seats;
        return Result.Success();
    }
}

internal sealed class RideBooking
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RideId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? FamilyId { get; private set; }
    public int Seats { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private RideBooking() { }

    public static RideBooking Create(Guid rideId, Guid userId, Guid? familyId, int seats, DateTime nowUtc) =>
        new() { RideId = rideId, UserId = userId, FamilyId = familyId, Seats = seats, CreatedAtUtc = nowUtc };
}
