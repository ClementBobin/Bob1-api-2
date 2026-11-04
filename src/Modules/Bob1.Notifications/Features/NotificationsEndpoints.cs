using Bob1.Identity.Public;
using Bob1.Notifications.Domain;
using Bob1.Notifications.Infrastructure;
using Bob1.Notifications.Public;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Notifications.Features;

internal sealed record PushTokenRequest(string Token, string Platform);
internal sealed record BroadcastRequest(string Title, string Body, Guid? DivisionId, DateTime? ExpiresAtUtc = null, bool Recurring = false, bool Startup = false);
internal sealed record NotificationDto(Guid Id, string Title, string Body, string? DataJson, DateTime CreatedAtUtc, bool IsRead, DateTime? ExpiresAtUtc, bool Recurring, bool Startup);

internal static class NotificationsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("").RequireAuthorization().WithTags("Notifications");

        // ---- Device tokens (FCM) ----
        auth.MapPost("/me/push-tokens", async (PushTokenRequest r, NotificationsDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            if (Guard.First(Guard.NotBlank(r.Token, "token", 512), Guard.NotBlank(r.Platform, "platform", 20)) is { } err)
                return err.ToProblem();

            var now = clock.GetUtcNow().UtcDateTime;
            var token = r.Token.Trim();
            var existing = await db.DeviceTokens.SingleOrDefaultAsync(t => t.Token == token, ct);
            if (existing is null) db.DeviceTokens.Add(DeviceToken.Create(user.UserId, token, r.Platform.Trim().ToLowerInvariant(), now));
            else existing.Reassign(user.UserId, r.Platform.Trim().ToLowerInvariant(), now);   // device changed account

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        auth.MapDelete("/me/push-tokens", async ([FromBody] PushTokenRequest r, NotificationsDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            await db.DeviceTokens.Where(t => t.Token == r.Token && t.UserId == user.UserId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        // ---- In-app notification list ----
        auth.MapGet("/notifications", async (bool? unreadOnly, int? skip, int? take, NotificationsDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            var q = db.Notifications.AsNoTracking().Where(n => n.UserId == user.UserId);
            if (unreadOnly == true) q = q.Where(n => n.ReadAtUtc == null);

            var rows = await q.OrderByDescending(n => n.CreatedAtUtc)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 30, 1, 100))
                .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.DataJson, n.CreatedAtUtc, n.ReadAtUtc != null, n.ExpiresAtUtc, n.Recurring, n.Startup))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        auth.MapGet("/notifications/unread-count", async (NotificationsDbContext db, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(new { count = await db.Notifications.CountAsync(n => n.UserId == user.UserId && n.ReadAtUtc == null, ct) }));

        auth.MapPost("/notifications/{id:guid}/read", async (Guid id, NotificationsDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId, ct);
            if (n is null) return Error.NotFound("notification.not_found", "Notification not found.").ToProblem();
            n.MarkRead(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        auth.MapPost("/notifications/read-all", async (NotificationsDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Notifications.Where(n => n.UserId == user.UserId && n.ReadAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, (DateTime?)now), ct);
            return Results.NoContent();
        });

        // ---- Admin broadcast ----
        api.MapPost("/admin/notifications", async (BroadcastRequest r, IIdentityApi identity, INotificationsApi notifications, CancellationToken ct) =>
        {
            if (Guard.First(Guard.NotBlank(r.Title, "title", 200), Guard.NotBlank(r.Body, "body", 1000)) is { } err)
                return err.ToProblem();

            var recipients = await identity.GetUserIdsByDivisionAsync(r.DivisionId, ct);
            await notifications.SendToUsersAsync(recipients, r.Title.Trim(), r.Body.Trim(),
                new Dictionary<string, string> { ["type"] = "broadcast" }, ct, r.ExpiresAtUtc, r.Recurring, r.Startup);
            return Results.Ok(new { recipients = recipients.Count });
        }).RequireAuthorization(Policies.Admin).WithTags("Notifications (admin)");

        // ---- Admin: list all notifications ----
        api.MapGet("/admin/notifications", async (string? search, int? skip, int? take, NotificationsDbContext db, CancellationToken ct) =>
        {
            var q = db.Notifications.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                q = q.Where(n => n.Title.ToLower().Contains(s) || n.Body.ToLower().Contains(s));
            }
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(n => n.CreatedAtUtc)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.DataJson, n.CreatedAtUtc, n.ReadAtUtc != null, n.ExpiresAtUtc, n.Recurring, n.Startup))
                .ToListAsync(ct);
            return Results.Ok(new { total, items = rows });
        }).RequireAuthorization(Policies.Admin).WithTags("Notifications (admin)");

        // ---- Admin: update notification ----
        api.MapPut("/admin/notifications/{id:guid}", async (Guid id, BroadcastRequest r, NotificationsDbContext db, CancellationToken ct) =>
        {
            if (Guard.First(Guard.NotBlank(r.Title, "title", 200), Guard.NotBlank(r.Body, "body", 1000)) is { } err)
                return err.ToProblem();

            var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (n is null) return Error.NotFound("notification.not_found", "Notification not found.").ToProblem();

            n.Update(r.Title.Trim(), r.Body.Trim(), r.ExpiresAtUtc, r.Recurring, r.Startup);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new NotificationDto(n.Id, n.Title, n.Body, n.DataJson, n.CreatedAtUtc, n.ReadAtUtc != null, n.ExpiresAtUtc, n.Recurring, n.Startup));
        }).RequireAuthorization(Policies.Admin).WithTags("Notifications (admin)");

        // ---- Admin: delete notification ----
        api.MapDelete("/admin/notifications/{id:guid}", async (Guid id, NotificationsDbContext db, CancellationToken ct) =>
        {
            var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (n is null) return Error.NotFound("notification.not_found", "Notification not found.").ToProblem();
            db.Notifications.Remove(n);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin).WithTags("Notifications (admin)");
    }
}
