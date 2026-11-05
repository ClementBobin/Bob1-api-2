using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bob1.Shared;

public static class Guard
{
    public static Error? NotBlank(string? value, string field, int max = 200) =>
        string.IsNullOrWhiteSpace(value) ? Error.Validation($"{field}.required", $"'{field}' is required.")
        : value.Trim().Length > max ? Error.Validation($"{field}.too_long", $"'{field}' must be at most {max} characters.")
        : null;

    public static Error? InRange(int value, string field, int min, int max) =>
        value < min || value > max ? Error.Validation($"{field}.out_of_range", $"'{field}' must be between {min} and {max}.") : null;

    /// <summary>Returns the first non-null error, or null if everything is valid.</summary>
    public static Error? First(params Error?[] errors)
    {
        foreach (var e in errors)
            if (e is not null) return e;
        return null;
    }
}

public static class Dates
{
    /// <summary>Normalize to UTC before persisting. Unspecified is treated as UTC.</summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? ToUtc(DateTime? value) => value is null ? null : ToUtc(value.Value);
}

/// <summary>
/// SQLite round-trips DateTime as Unspecified, which System.Text.Json would serialize without a
/// trailing "Z". Clients would then read UTC instants as local time. Always emit UTC.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Dates.ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Dates.ToUtc(value));
}

/// <summary>Club season runs September to August, e.g. "2026-2027".</summary>
public static class Season
{
    public static string For(DateTime utc)
    {
        var start = utc.Month >= 9 ? utc.Year : utc.Year - 1;
        return $"{start}-{start + 1}";
    }
}
