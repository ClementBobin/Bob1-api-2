using Bob1.Shared.Contracts.Matches;
using Bob1.Shared.Contracts.Notifications;
using Bob1.Shared.Contracts.Identity;

namespace Bob1.Modules.Participation.Application;

/// <summary>
/// Every 15 minutes: users that registered a child for a match starting within 4 days but have not
/// confirmed presence get ONE reminder (deduplicated through reminder_logs).
/// </summary>
internal sealed class PresenceReminderService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PresenceReminderService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Presence reminder run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParticipationDbContext>();
        var matches = scope.ServiceProvider.GetRequiredService<IMatchesApi>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationsApi>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityApi>();

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var match in await matches.GetStartingBetweenAsync(now, now.AddDays(4), ct))
        {
            var confirmed = db.PresenceConfirmations.Where(p => p.MatchId == match.Id).Select(p => p.FamilyId);
            var pending = await db.ChildRegistrations
                .Where(r => r.MatchId == match.Id && !confirmed.Contains(r.FamilyId))
                .Select(r => r.FamilyId).Distinct().ToListAsync(ct);

            foreach (var familyId in pending)
            {
                var key = $"presence-j4:{match.Id}:{familyId}";
                if (await db.ReminderLogs.AnyAsync(l => l.Key == key, ct)) continue;

                // Get users from this family
                var userIds = await identity.GetUserIdsOfFamilyAsync(familyId, ct);
                if (userIds.Count > 0)
                {
                    await notifications.SendToUsersAsync(userIds, "Confirmez votre présence",
                        $"{match.Title} : merci de confirmer votre présence.",
                        new Dictionary<string, string> { ["type"] = "presence_reminder", ["matchId"] = match.Id.ToString() }, ct);
                }

                db.ReminderLogs.Add(ReminderLog.Create(key, now));
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
