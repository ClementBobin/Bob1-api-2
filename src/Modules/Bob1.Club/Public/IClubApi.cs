namespace Bob1.Club.Public;

public interface IClubApi
{
    Task<DivisionInfo?> GetDivisionAsync(Guid id, CancellationToken ct);
    Task<VenueInfo?> GetVenueAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetDivisionNamesAsync(IEnumerable<Guid> ids, CancellationToken ct);
}

public sealed record DivisionInfo(Guid Id, string Name);
public sealed record VenueInfo(Guid Id, string Name);

/// <summary>Fixed ids used by the Development seed, so other modules' seeds can reference them.</summary>
public static class ClubSeed
{
    public static readonly Guid DivisionU13M = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid DivisionU17F = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid VenueCoubertin = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
}
