using Bob1.Shared.Contracts.Participation;

namespace Bob1.Modules.Participation.Domain;

internal sealed class PresenceConfirmation : Entity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public Guid FamilyId { get; private set; }
    public Guid ConfirmedByUserId { get; private set; }
    public DateTime ConfirmedAtUtc { get; private set; }

    private PresenceConfirmation() { }

    public static PresenceConfirmation Create(Guid matchId, Guid familyId, Guid userId, DateTime nowUtc)
    {
        var c = new PresenceConfirmation { MatchId = matchId, FamilyId = familyId, ConfirmedByUserId = userId, ConfirmedAtUtc = nowUtc };
        c.Raise(new PresenceConfirmed(matchId, userId, familyId));
        return c;
    }
}

internal sealed class ChildRegistration
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public Guid ChildId { get; private set; }
    public Guid FamilyId { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }

    private ChildRegistration() { }

    public static ChildRegistration Create(Guid matchId, Guid childId, Guid familyId, DateTime nowUtc) =>
        new() { MatchId = matchId, ChildId = childId, FamilyId = familyId, RegisteredAtUtc = nowUtc };
}

internal enum SlotKind { Volunteer, Staff }

/// <summary>A job to fill on match day: "Buvette" (parents) or "MAR" / "Arbitre 1" / "Chrono" (staff).</summary>
internal sealed class Slot : Entity
{
    private readonly List<SlotSignup> _signups = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public SlotKind Kind { get; private set; }
    public string Role { get; private set; } = default!;
    public int Capacity { get; private set; }
    public int Filled { get; private set; }
    public DateTime? StartsAtUtc { get; private set; }
    public DateTime? EndsAtUtc { get; private set; }

    public IReadOnlyCollection<SlotSignup> Signups => _signups;

    private Slot() { }

    public static Slot Create(Guid matchId, SlotKind kind, string role, int capacity, DateTime? startsAtUtc, DateTime? endsAtUtc) =>
        new() { MatchId = matchId, Kind = kind, Role = role, Capacity = capacity, StartsAtUtc = startsAtUtc, EndsAtUtc = endsAtUtc };

    public Result Update(string role, int capacity, DateTime? startsAtUtc, DateTime? endsAtUtc)
    {
        if (capacity < Filled)
            return Error.Conflict("slot.capacity_below_filled", $"{Filled} person(s) are already signed up.");
        Role = role; Capacity = capacity; StartsAtUtc = startsAtUtc; EndsAtUtc = endsAtUtc;
        return Result.Success();
    }

    public Result Join(Guid userId, Guid? familyId, DateTime nowUtc)
    {
        if (_signups.Any(s => s.UserId == userId)) return Error.Conflict("slot.already_joined", "You already joined this slot.");
        if (Filled >= Capacity) return Error.Conflict("slot.full", "This slot is full.");

        var signup = SlotSignup.Create(Id, userId, familyId, nowUtc);
        _signups.Add(signup);
        Filled++;
        Raise(new SlotJoined(Id, signup.Id, MatchId, Role, Kind == SlotKind.Staff, userId, familyId));
        return Result.Success();
    }

    public Result Leave(Guid userId)
    {
        var signup = _signups.SingleOrDefault(s => s.UserId == userId);
        if (signup is null) return Error.NotFound("slot.not_joined", "You are not signed up for this slot.");

        _signups.Remove(signup);
        Filled--;
        Raise(new SlotLeft(Id, signup.Id, MatchId, Role, Kind == SlotKind.Staff, userId, signup.FamilyId));
        return Result.Success();
    }
}

internal sealed class SlotSignup
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SlotId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? FamilyId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private SlotSignup() { }

    public static SlotSignup Create(Guid slotId, Guid userId, Guid? familyId, DateTime nowUtc) =>
        new() { SlotId = slotId, UserId = userId, FamilyId = familyId, CreatedAtUtc = nowUtc };
}

/// <summary>Makes reminders idempotent: one row per (reminder kind, match, family).</summary>
internal sealed class ReminderLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Key { get; private set; } = default!;
    public DateTime SentAtUtc { get; private set; }

    private ReminderLog() { }

    public static ReminderLog Create(string key, DateTime nowUtc) => new() { Key = key, SentAtUtc = nowUtc };
}
