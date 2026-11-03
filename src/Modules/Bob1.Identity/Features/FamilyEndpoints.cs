using System.Security.Cryptography;
using Bob1.Club.Public;
using Bob1.Identity.Domain;
using Bob1.Identity.Infrastructure;

namespace Bob1.Identity.Features;

internal sealed record ChildRequest(string FirstName, string LastName, DateOnly BirthDate, Guid? DivisionId);
internal sealed record ChildDto(Guid Id, string FirstName, string LastName, DateOnly BirthDate, Guid? DivisionId, string? DivisionName);
internal sealed record MemberDto(Guid UserId, string FirstName, string LastName, string Email, bool IsMe);
internal sealed record FamilyDto(Guid Id, string Name, IReadOnlyList<MemberDto> Members, IReadOnlyList<ChildDto> Children);
internal sealed record InvitationDto(string Code, DateTime ExpiresAtUtc);

internal static class FamilyEndpoints
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I

    public static void Map(IEndpointRouteBuilder api)
    {
        var family = api.MapGroup("/family").RequireAuthorization(Policies.Parent).WithTags("Family");

        family.MapGet("", async (IdentityDbContext db, IClubApi club, ICurrentUser user, CancellationToken ct) =>
        {
            if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();

            var f = await db.Families.AsNoTracking().SingleOrDefaultAsync(x => x.Id == familyId, ct);
            if (f is null) return Error.NotFound("family.not_found", "Family not found.").ToProblem();

            var members = await db.Users.AsNoTracking().Where(u => u.FamilyId == familyId).OrderBy(u => u.FirstName)
                .Select(u => new MemberDto(u.Id, u.FirstName, u.LastName, u.Email, u.Id == user.UserId)).ToListAsync(ct);
            var children = await LoadChildrenAsync(db, club, familyId, ct);
            return Results.Ok(new FamilyDto(f.Id, f.Name, members, children));
        });

        family.MapPost("/invitations", async (IdentityDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        {
            if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();
            var invitation = FamilyInvitation.Create(
                familyId, RandomNumberGenerator.GetString(CodeAlphabet, 8), user.UserId,
                clock.GetUtcNow().UtcDateTime.AddDays(7));
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new InvitationDto(invitation.Code, invitation.ExpiresAtUtc));
        });

        // ---- Children ----
        var children = family.MapGroup("/children");

        children.MapGet("", async (IdentityDbContext db, IClubApi club, ICurrentUser user, CancellationToken ct) =>
            user.FamilyId is { } familyId
                ? Results.Ok(await LoadChildrenAsync(db, club, familyId, ct))
                : Error.Forbidden("family.none", "No family.").ToProblem());

        children.MapPost("", async (ChildRequest r, IdentityDbContext db, IClubApi club, ICurrentUser user, CancellationToken ct) =>
        {
            if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();
            if (await ValidateAsync(r, club, ct) is { } err) return err.ToProblem();

            var child = Child.Create(familyId, r.FirstName.Trim(), r.LastName.Trim(), r.BirthDate, r.DivisionId);
            db.Children.Add(child);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/family/children/{child.Id}", await ToDtoAsync(child, club, ct));
        });

        children.MapPut("/{id:guid}", async (Guid id, ChildRequest r, IdentityDbContext db, IClubApi club, ICurrentUser user, CancellationToken ct) =>
        {
            var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == user.FamilyId, ct);
            if (child is null) return Error.NotFound("child.not_found", "Child not found.").ToProblem();
            if (await ValidateAsync(r, club, ct) is { } err) return err.ToProblem();

            child.Update(r.FirstName.Trim(), r.LastName.Trim(), r.BirthDate, r.DivisionId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ToDtoAsync(child, club, ct));
        });

        children.MapDelete("/{id:guid}", async (Guid id, IdentityDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == user.FamilyId, ct);
            if (child is null) return Error.NotFound("child.not_found", "Child not found.").ToProblem();
            // TODO: also cancel the child's future registrations (publish a ChildRemoved event for Participation).
            db.Children.Remove(child);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<Error?> ValidateAsync(ChildRequest r, IClubApi club, CancellationToken ct)
    {
        var err = Guard.First(Guard.NotBlank(r.FirstName, "first_name", 100), Guard.NotBlank(r.LastName, "last_name", 100));
        if (err is not null) return err;
        if (r.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
            return Error.Validation("birth_date.invalid", "Birth date cannot be in the future.");
        if (r.DivisionId is { } d && await club.GetDivisionAsync(d, ct) is null)
            return Error.NotFound("division.not_found", "Division not found.");
        return null;
    }

    private static async Task<ChildDto> ToDtoAsync(Child c, IClubApi club, CancellationToken ct)
    {
        string? divisionName = c.DivisionId is { } d ? (await club.GetDivisionAsync(d, ct))?.Name : null;
        return new ChildDto(c.Id, c.FirstName, c.LastName, c.BirthDate, c.DivisionId, divisionName);
    }

    private static async Task<IReadOnlyList<ChildDto>> LoadChildrenAsync(IdentityDbContext db, IClubApi club, Guid familyId, CancellationToken ct)
    {
        var rows = await db.Children.AsNoTracking().Where(c => c.FamilyId == familyId).OrderBy(c => c.FirstName).ToListAsync(ct);
        var names = await club.GetDivisionNamesAsync(rows.Where(c => c.DivisionId != null).Select(c => c.DivisionId!.Value), ct);
        return rows.Select(c => new ChildDto(c.Id, c.FirstName, c.LastName, c.BirthDate, c.DivisionId,
            c.DivisionId is { } d && names.TryGetValue(d, out var n) ? n : null)).ToList();
    }
}
