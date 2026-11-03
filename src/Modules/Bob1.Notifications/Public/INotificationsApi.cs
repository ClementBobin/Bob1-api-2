namespace Bob1.Notifications.Public;

/// <summary>Stores an in-app notification for each user and pushes it to their devices.</summary>
public interface INotificationsApi
{
    Task SendToUsersAsync(IEnumerable<Guid> userIds, string title, string body,
        IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default);

    Task SendToPersonAsync(Guid personId, string title, string body,
        IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default);
}
