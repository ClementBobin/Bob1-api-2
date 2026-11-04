namespace Bob1.Notifications.Domain;

internal sealed class DeviceToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = default!;
    public string Platform { get; private set; } = default!;   // "android" | "ios"
    public DateTime LastSeenAtUtc { get; private set; }

    private DeviceToken() { }

    public static DeviceToken Create(Guid userId, string token, string platform, DateTime nowUtc) =>
        new() { UserId = userId, Token = token, Platform = platform, LastSeenAtUtc = nowUtc };

    public void Reassign(Guid userId, string platform, DateTime nowUtc)
    {
        UserId = userId; Platform = platform; LastSeenAtUtc = nowUtc;
    }
}

/// <summary>Named UserNotification to avoid clashing with FirebaseAdmin's Notification type.</summary>
internal sealed class UserNotification
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string? DataJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public bool Recurring { get; private set; }
    public bool Startup { get; private set; }

    private UserNotification() { }

    public static UserNotification Create(Guid userId, string title, string body, string? dataJson, DateTime nowUtc,
        DateTime? expiresAtUtc = null, bool recurring = false, bool startup = false) =>
        new() { UserId = userId, Title = title, Body = body, DataJson = dataJson, CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc, Recurring = recurring, Startup = startup };

    public void MarkRead(DateTime nowUtc) => ReadAtUtc ??= nowUtc;
}
