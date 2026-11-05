using Bob1.Shared.Contracts.Matches;

namespace Bob1.Modules.Matches.Domain;

internal sealed class Match : Entity
{
    public const string ClubName = "BOB1";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid DivisionId { get; private set; }
    public string DivisionName { get; private set; } = default!;   // snapshot, e.g. "U13 M"
    public string Opponent { get; private set; } = default!;
    public bool IsHome { get; private set; }
    public Guid VenueId { get; private set; }
    public string VenueName { get; private set; } = default!;       // snapshot
    public DateTime StartsAtUtc { get; private set; }
    public MatchStatus Status { get; private set; } = MatchStatus.Scheduled;
    public int? HomeScore { get; private set; }
    public int? AwayScore { get; private set; }

    public string Title => IsHome ? $"{ClubName} vs {Opponent}" : $"{Opponent} vs {ClubName}";

    private Match() { }

    public static Match Create(Guid divisionId, string divisionName, string opponent, bool isHome,
        Guid venueId, string venueName, DateTime startsAtUtc, Guid? id = null)
    {
        var m = new Match { Id = id ?? Guid.NewGuid() };
        m.Apply(divisionId, divisionName, opponent, isHome, venueId, venueName, startsAtUtc);
        return m;
    }

    public Result Update(Guid divisionId, string divisionName, string opponent, bool isHome,
        Guid venueId, string venueName, DateTime startsAtUtc)
    {
        if (Status != MatchStatus.Scheduled)
            return Error.Conflict("match.not_editable", "Only scheduled matches can be edited.");
        Apply(divisionId, divisionName, opponent, isHome, venueId, venueName, startsAtUtc);
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status != MatchStatus.Scheduled)
            return Error.Conflict("match.not_cancellable", "Only scheduled matches can be cancelled.");
        Status = MatchStatus.Cancelled;
        Raise(new MatchCancelled(Id, Title, StartsAtUtc));
        return Result.Success();
    }

    public Result SetResult(int homeScore, int awayScore)
    {
        if (Status == MatchStatus.Cancelled)
            return Error.Conflict("match.cancelled", "A cancelled match has no result.");
        HomeScore = homeScore;
        AwayScore = awayScore;
        Status = MatchStatus.Played;
        return Result.Success();
    }

    private void Apply(Guid divisionId, string divisionName, string opponent, bool isHome,
        Guid venueId, string venueName, DateTime startsAtUtc)
    {
        DivisionId = divisionId; DivisionName = divisionName; Opponent = opponent; IsHome = isHome;
        VenueId = venueId; VenueName = venueName; StartsAtUtc = startsAtUtc;
    }
}
