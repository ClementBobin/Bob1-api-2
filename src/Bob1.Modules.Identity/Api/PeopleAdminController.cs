using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace Bob1.Modules.Identity.Api;

internal sealed record PersonDto(Guid Id, string Email, string FirstName, string LastName, string[] Roles, bool IsActive, bool SuspendedFromParticipation, Guid? FamilyId);
internal sealed record UpdateRolesRequest(string[] Roles);
internal sealed record SetActiveRequest(bool IsActive);
internal sealed record CreatePersonRequest(string Email, string FirstName, string LastName, string[] Roles, Guid? FamilyId, string? Password);
internal sealed record UpdatePersonRequest(string Email, string FirstName, string LastName, Guid? FamilyId);
internal sealed record CreatePersonResponse(PersonDto Person, string? TemporaryPassword);

internal sealed record PeopleQuery(string? Search, string? Role, bool? IsActive, int? Skip, int? Take);

[Route(ApiRoutes.V1 + "/admin/people")]
[Authorize(Policy = Policies.Admin)]
[Tags("People (admin)")]
internal sealed class PeopleAdminController(IdentityDbContext db, IPasswordHasher<Domain.User> hasher, ICurrentUser me, TimeProvider clock)
    : ApiControllerBase
{
    [HttpQuery]
    public async Task<IActionResult> List([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PeopleQuery? query, CancellationToken ct)
    {
        query ??= new(null, null, null, null, null);
        var q = db.Users.AsNoTracking();
        if (Paging.Term(query.Search) is { } s)
            q = q.Where(u => u.Email.Contains(s) || u.FirstName.ToLower().Contains(s) || u.LastName.ToLower().Contains(s));
        if (!string.IsNullOrWhiteSpace(query.Role) && Enum.TryParse<UserRoles>(query.Role, true, out var r) && r != UserRoles.None)
            q = q.Where(u => (u.Roles & r) == r);
        if (query.IsActive is { } active) q = q.Where(u => u.IsActive == active);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip(Paging.Skip(query.Skip)).Take(Paging.Take(query.Take, 50, 200)).ToListAsync(ct);

        return Ok(new
        {
            total,
            items = rows.Select(ToDto)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePersonRequest r, CancellationToken ct)
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

        // Fully qualified: inside a controller, "User" alone resolves to ControllerBase.User (the ClaimsPrincipal).
        var user = Domain.User.Create(email, r.FirstName.Trim(), r.LastName.Trim(), roles, r.FamilyId,
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

        return Created($"/api/v1/admin/people/{user.Id}",
            new CreatePersonResponse(ToDto(user), temporaryPassword));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdatePersonRequest r, CancellationToken ct)
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
        return Ok(ToDto(u));
    }

    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> UpdateRoles(Guid id, UpdateRolesRequest r, CancellationToken ct)
    {
        if (r.Roles is null || !UserRolesExtensions.TryParse(r.Roles, out var roles) || roles == UserRoles.None)
            return Error.Validation("roles.invalid", "Roles must be a non-empty list of Parent, Staff, Admin.").ToProblem();
        if (id == me.UserId && !roles.HasFlag(UserRoles.Admin))
            return Error.Conflict("roles.cannot_remove_own_admin", "You cannot remove your own Admin role.").ToProblem();

        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
        u.SetRoles(roles);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(u));
    }

    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, SetActiveRequest r, CancellationToken ct)
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
        return NoContent();
    }

    // Note: reuses SetActiveRequest, so the body field is still called "isActive" (unchanged from the minimal API).
    [HttpPut("{id:guid}/suspended-from-participation")]
    public async Task<IActionResult> SetSuspendedFromParticipation(Guid id, SetActiveRequest r, CancellationToken ct)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return Error.NotFound("user.not_found", "User not found.").ToProblem();
        u.SetSuspendedFromParticipation(r.IsActive);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static PersonDto ToDto(Domain.User u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.Roles.ToNames(), u.IsActive, u.SuspendedFromParticipation, u.FamilyId);

    private static string NewTemporaryPassword() =>
        $"Bob1-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}!aA";
}
