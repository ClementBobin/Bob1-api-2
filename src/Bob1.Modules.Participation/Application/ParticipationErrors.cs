using Bob1.Shared.Contracts.Matches;

namespace Bob1.Modules.Participation.Application;

internal static class ParticipationErrors
{
    public static readonly Error NoFamily = Error.Forbidden("participation.no_family", "Your account is not attached to a family.");
    public static readonly Error MatchNotFound = Error.NotFound("match.not_found", "Match not found.");
    public static readonly Error MatchAlreadyStarted = Error.Conflict("match.already_started", "This match has already started.");
    public static readonly Error MatchCancelled = Error.Conflict("match.cancelled", "This match has been cancelled.");
    public static readonly Error SlotNotFound = Error.NotFound("slot.not_found", "Slot not found.");
    public static readonly Error WrongAudience = Error.Forbidden("slot.wrong_audience", "This slot is not open to your role.");
}

internal static class MatchRules
{
    /// <summary>A match accepts registrations/sign-ups only while scheduled and not started.</summary>
    public static Error? EnsureOpen(MatchInfo? match, DateTime nowUtc) =>
        match is null ? ParticipationErrors.MatchNotFound
        : match.Status == MatchStatus.Cancelled ? ParticipationErrors.MatchCancelled
        : match.StartsAtUtc <= nowUtc ? ParticipationErrors.MatchAlreadyStarted
        : null;
}
