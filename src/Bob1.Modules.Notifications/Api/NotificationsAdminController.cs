using Bob1.Shared.Contracts.Identity;
using Bob1.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Notifications.Api;

[Route(ApiRoutes.V1 + "/admin/notifications")]
[Authorize(Policy = Policies.Admin)]
[Tags("Notifications (admin)")]
internal sealed class NotificationsAdminController(NotificationsDbContext db) : ApiControllerBase
{
    // ---- Broadcast ----
    [HttpPost]
    public async Task<IActionResult> Broadcast(BroadcastRequest r,
        [FromServices] IIdentityApi identity, [FromServices] INotificationsApi notifications, CancellationToken ct)
    {
        if (Guard.First(Guard.NotBlank(r.Title, "title", 200), Guard.NotBlank(r.Body, "body", 1000)) is { } err)
            return err.ToProblem();

        var recipients = await identity.GetUserIdsByDivisionAsync(r.DivisionId, ct);
        await notifications.SendToUsersAsync(recipients, r.Title.Trim(), r.Body.Trim(),
            new Dictionary<string, string> { ["type"] = "broadcast" }, ct, r.ExpiresAtUtc, r.Recurring, r.Startup);
        return Ok(new { recipients = recipients.Count });
    }

    // ---- List all notifications ----
    [HttpGet]
    public async Task<IActionResult> List(string? search, int? skip, int? take, CancellationToken ct)
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
        return Ok(new { total, items = rows });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, BroadcastRequest r, CancellationToken ct)
    {
        if (Guard.First(Guard.NotBlank(r.Title, "title", 200), Guard.NotBlank(r.Body, "body", 1000)) is { } err)
            return err.ToProblem();

        var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return Error.NotFound("notification.not_found", "Notification not found.").ToProblem();

        n.Update(r.Title.Trim(), r.Body.Trim(), r.ExpiresAtUtc, r.Recurring, r.Startup);
        await db.SaveChangesAsync(ct);
        return Ok(new NotificationDto(n.Id, n.Title, n.Body, n.DataJson, n.CreatedAtUtc, n.ReadAtUtc != null, n.ExpiresAtUtc, n.Recurring, n.Startup));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return Error.NotFound("notification.not_found", "Notification not found.").ToProblem();
        db.Notifications.Remove(n);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
