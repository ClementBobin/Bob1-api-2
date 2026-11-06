namespace Bob1.Shared;

/// <summary>Envelope of every paginated QUERY endpoint: the page of items and the total before paging.</summary>
public sealed record PagedResult<T>(int Total, IReadOnlyList<T> Items);

public static class Paging
{
    public static int Skip(int? skip) => Math.Max(0, skip ?? 0);

    public static int Take(int? take, int defaultTake = 50, int maxTake = 500) =>
        Math.Clamp(take ?? defaultTake, 1, maxTake);

    /// <summary>Lower-cased, trimmed search term, or null when blank.</summary>
    public static string? Term(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
}
