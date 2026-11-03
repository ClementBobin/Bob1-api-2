using Bob1.Identity.Domain;
using Bob1.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Bob1.Identity.Features;

internal sealed record UpdateProfileRequest(string FirstName, string LastName);
internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal static class MeEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization().WithTags("Me");

        me.MapGet("", async (IdentityDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == user.UserId, ct);
            return u is null ? Error.NotFound("user.not_found", "User not found.").ToProblem() : Results.Ok(u.ToDto());
        });

        me.MapPut("", async (UpdateProfileRequest r, IdentityDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            if (Guard.First(Guard.NotBlank(r.FirstName, "first_name", 100), Guard.NotBlank(r.LastName, "last_name", 100)) is { } err)
                return err.ToProblem();
            var u = await db.Users.SingleOrDefaultAsync(x => x.Id == user.UserId, ct);
            if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
            u.SetName(r.FirstName.Trim(), r.LastName.Trim());
            await db.SaveChangesAsync(ct);
            return Results.Ok(u.ToDto());
        });

        me.MapPut("/password", async (ChangePasswordRequest r, IdentityDbContext db, IPasswordHasher<User> hasher,
            ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            if (r.NewPassword is null || r.NewPassword.Length < 8)
                return Error.Validation("password.too_short", "Password must be at least 8 characters.").ToProblem();

            var u = await db.Users.SingleOrDefaultAsync(x => x.Id == user.UserId, ct);
            if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
            if (hasher.VerifyHashedPassword(u, u.PasswordHash, r.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Error.Validation("password.incorrect", "Current password is incorrect.").ToProblem();

            u.SetPasswordHash(hasher.HashPassword(u, r.NewPassword));

            // Sign out everywhere: revoke all active refresh tokens.
            var now = clock.GetUtcNow().UtcDateTime;
            var tokens = await db.RefreshTokens.Where(t => t.UserId == u.Id && t.RevokedAtUtc == null).ToListAsync(ct);
            foreach (var t in tokens) t.Revoke(now);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
