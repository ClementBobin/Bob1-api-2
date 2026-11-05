using FirebaseAdmin.Messaging;

namespace Bob1.Modules.Notifications.Infrastructure;

internal interface IPushSender
{
    /// <summary>Returns the tokens that are no longer valid (app uninstalled...) so they can be deleted.</summary>
    Task<IReadOnlyList<string>> SendAsync(IReadOnlyCollection<string> tokens, string title, string body,
        IReadOnlyDictionary<string, string>? data, CancellationToken ct);
}

/// <summary>Used when Firebase:CredentialsPath is not configured (local dev): just logs.</summary>
internal sealed class LoggingPushSender(ILogger<LoggingPushSender> log) : IPushSender
{
    public Task<IReadOnlyList<string>> SendAsync(IReadOnlyCollection<string> tokens, string title, string body,
        IReadOnlyDictionary<string, string>? data, CancellationToken ct)
    {
        log.LogInformation("[push:dev] to {Count} device(s): {Title} - {Body}", tokens.Count, title, body);
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}

internal sealed class FcmPushSender : IPushSender
{
    public async Task<IReadOnlyList<string>> SendAsync(IReadOnlyCollection<string> tokens, string title, string body,
        IReadOnlyDictionary<string, string>? data, CancellationToken ct)
    {
        var invalid = new List<string>();

        foreach (var chunk in tokens.Chunk(500)) // FCM multicast limit
        {
            var message = new MulticastMessage
            {
                Tokens = chunk.ToList(),
                Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
                Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value)
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, ct);
            for (var i = 0; i < response.Responses.Count; i++)
            {
                var r = response.Responses[i];
                if (!r.IsSuccess && r.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
                    invalid.Add(chunk[i]);
            }
        }
        return invalid;
    }
}
