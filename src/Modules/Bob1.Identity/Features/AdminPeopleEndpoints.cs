using System.Security.Cryptography;
using Bob1.Identity.Domain;
using Bob1.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace Bob1.Identity.Features;

internal sealed record PersonDto(Guid Id, string Email, string FirstName, string LastName, string[] Roles, bool IsActive, bool SuspendedFromParticipation, Guid? FamilyId);
internal sealed record UpdateRolesRequest(string[] Roles);
internal sealed record SetActiveRequest(bool IsActive);
internal sealed record CreatePersonRequest(string Email, string FirstName, string LastName, string[] Roles, Guid? FamilyId, string? Password);
internal sealed record UpdatePersonRequest(string Email, string FirstName, string LastName, Guid? FamilyId);
internal sealed record CreatePersonResponse(PersonDto Person, string? TemporaryPassword);

internal static class AdminPeopleEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/people").RequireAuthorization(Policies.Admin).WithTags("People (admin)");

        admin.MapGet("", async (string? search, string? role, int? skip, int? take, IdentityDbContext db, CancellationToken ct) =>
        {
            var q = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                q = q.Where(u => u.Email.Contains(s) || u.FirstName.ToLower().Contains(s) || u.LastName.ToLower().Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRoles>(role, true, out var r) && r != UserRoles.None)
                q = q.Where(u => (u.Roles & r) == r);

            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 50, 1, 200)).ToListAsync(ct);

            return Results.Ok(new
            {
                total,
                items = rows.Select(ToDto)
            });
        });

        admin.MapPost("", async (CreatePersonRequest r, IdentityDbContext db, IPasswordHasher<User> hasher,
            TimeProvider clock, CancellationToken ct) =>
        {
            var invalid = Guard.First(
                Guard.NotBlank(r.Email, "email"),
                Guard.NotBlank(r.FirstName, "first_name", 100),
                Guard.NotBlank(r.LastName, "last_name", 100));
            if (invalid is not null) return invalid.ToProblem();
            if (!r.Email.Contains('@')) return Error.Validation("email.invalid", "Invalid email address.").ToProblem();
            if (r.Roles is null || !UserRolesExtensions.TryParse(r.Roles, out var roles) || roles == UserRoles.None)
                return Error.Validation("roles.invalid", "Roles must be a non-empty list of Parent, Staff, Admin.").ToProblem();

            var email = r.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
                return Error.Conflict("email.taken", "An account with this email already exists.").ToProblem();
            if (r.FamilyId is { } familyId && !await db.Families.AnyAsync(f => f.Id == familyId, ct))
                return Error.NotFound("family.not_found", "Family not found.").ToProblem();

            var temporaryPassword = string.IsNullOrWhiteSpace(r.Password) ? NewTemporaryPassword() : null;
            var password = temporaryPassword ?? r.Password!;
            if (password.Length < 8)
                return Error.Validation("password.too_short", "Password must be at least 8 characters.").ToProblem();

            var user = User.Create(email, r.FirstName.Trim(), r.LastName.Trim(), roles, r.FamilyId,
                clock.GetUtcNow().UtcDateTime);
            user.SetPasswordHash(hasher.HashPassword(user, password));
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
            {
                return Error.Conflict("email.taken", "An account with this email already exists.").ToProblem();
            }

            return Results.Created($"/api/v1/admin/people/{user.Id}",
                new CreatePersonResponse(ToDto(user), temporaryPassword));
        });

        admin.MapPut("/{id:guid}", async (Guid id, UpdatePersonRequest r, IdentityDbContext db, CancellationToken ct) =>
        {
            var invalid = Guard.First(
                Guard.NotBlank(r.Email, "email"),
                Guard.NotBlank(r.FirstName, "first_name", 100),
                Guard.NotBlank(r.LastName, "last_name", 100));
            if (invalid is not null) return invalid.ToProblem();
            if (!r.Email.Contains('@')) return Error.Validation("email.invalid", "Invalid email address.").ToProblem();

            var u = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();

            var email = r.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Id != id && x.Email == email, ct))
                return Error.Conflict("email.taken", "An account with this email already exists.").ToProblem();
            if (r.FamilyId is { } familyId && !await db.Families.AnyAsync(f => f.Id == familyId, ct))
                return Error.NotFound("family.not_found", "Family not found.").ToProblem();

            u.SetName(r.FirstName.Trim(), r.LastName.Trim());
            u.SetEmail(email);
            u.SetFamilyId(r.FamilyId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(u));
        });

        admin.MapPut("/{id:guid}/roles", async (Guid id, UpdateRolesRequest r, IdentityDbContext db, ICurrentUser me, CancellationToken ct) =>
        {
            if (r.Roles is null || !UserRolesExtensions.TryParse(r.Roles, out var roles) || roles == UserRoles.None)
                return Error.Validation("roles.invalid", "Roles must be a non-empty list of Parent, Staff, Admin.").ToProblem();
            if (id == me.UserId && !roles.HasFlag(UserRoles.Admin))
                return Error.Conflict("roles.cannot_remove_own_admin", "You cannot remove your own Admin role.").ToProblem();

            var u = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
            u.SetRoles(roles);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(u));
        });

        admin.MapPut("/{id:guid}/active", async (Guid id, SetActiveRequest r, IdentityDbContext db, ICurrentUser me, TimeProvider clock, CancellationToken ct) =>
        {
            if (id == me.UserId && !r.IsActive)
                return Error.Conflict("user.cannot_deactivate_self", "You cannot deactivate yourself.").ToProblem();

            var u = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
            u.SetActive(r.IsActive);

            if (!r.IsActive)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                foreach (var t in await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAtUtc == null).ToListAsync(ct))
                    t.Revoke(now);
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapPut("/{id:guid}/suspended-from-participation", async (Guid id, SetActiveRequest r, IdentityDbContext db, ICurrentUser me, CancellationToken ct) =>
        {
            var u = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
            u.SetSuspendedFromParticipation(r.IsActive);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static PersonDto ToDto(User u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.Roles.ToNames(), u.IsActive, u.SuspendedFromParticipation, u.FamilyId);

    private static string NewTemporaryPassword() =>
        $"Bob1-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}!aA";
}
