using System.Security.Cryptography;
using Bob1.Shared.Contracts.Club;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Identity.Api;

internal sealed record ChildRequest(string FirstName, string LastName, DateOnly BirthDate, Guid? DivisionId);
internal sealed record ChildDto(Guid Id, string FirstName, string LastName, DateOnly BirthDate, Guid? DivisionId, string? DivisionName);
internal sealed record MemberDto(Guid UserId, string FirstName, string LastName, string Email, bool IsMe);
internal sealed record FamilyDto(Guid Id, string Name, IReadOnlyList<MemberDto> Members, IReadOnlyList<ChildDto> Children);
internal sealed record InvitationDto(string Code, DateTime ExpiresAtUtc);

[Route(ApiRoutes.V1 + "/family")]
[Authorize(Policy = Policies.Parent)]
[Tags("Family")]
internal sealed class FamilyController(IdentityDbContext db, IClubApi club, ICurrentUser user, TimeProvider clock)
    : ApiControllerBase
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();

        var f = await db.Families.AsNoTracking().SingleOrDefaultAsync(x => x.Id == familyId, ct);
        if (f is null) return Error.NotFound("family.not_found", "Family not found.").ToProblem();

        var members = await db.Users.AsNoTracking().Where(u => u.FamilyId == familyId).OrderBy(u => u.FirstName)
            .Select(u => new MemberDto(u.Id, u.FirstName, u.LastName, u.Email, u.Id == user.UserId)).ToListAsync(ct);
        var children = await LoadChildrenAsync(familyId, ct);
        return Ok(new FamilyDto(f.Id, f.Name, members, children));
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> CreateInvitation(CancellationToken ct)
    {
        if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();
        var invitation = FamilyInvitation.Create(
            familyId, RandomNumberGenerator.GetString(CodeAlphabet, 8), user.UserId,
            clock.GetUtcNow().UtcDateTime.AddDays(7));
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return Ok(new InvitationDto(invitation.Code, invitation.ExpiresAtUtc));
    }

    // ---- Children ----

    [HttpGet("children")]
    public async Task<IActionResult> ListChildren(CancellationToken ct)
    {
        if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();
        return Ok(await LoadChildrenAsync(familyId, ct));
    }

    [HttpPost("children")]
    public async Task<IActionResult> AddChild(ChildRequest r, CancellationToken ct)
    {
        if (user.FamilyId is not { } familyId) return Error.Forbidden("family.none", "No family.").ToProblem();
        if (await ValidateAsync(r, ct) is { } err) return err.ToProblem();

        var child = Child.Create(familyId, r.FirstName.Trim(), r.LastName.Trim(), r.BirthDate, r.DivisionId);
        db.Children.Add(child);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/family/children/{child.Id}", await ToDtoAsync(child, ct));
    }

    [HttpPut("children/{id:guid}")]
    public async Task<IActionResult> UpdateChild(Guid id, ChildRequest r, CancellationToken ct)
    {
        var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == user.FamilyId, ct);
        if (child is null) return Error.NotFound("child.not_found", "Child not found.").ToProblem();
        if (await ValidateAsync(r, ct) is { } err) return err.ToProblem();

        child.Update(r.FirstName.Trim(), r.LastName.Trim(), r.BirthDate, r.DivisionId);
        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(child, ct));
    }

    [HttpDelete("children/{id:guid}")]
    public async Task<IActionResult> DeleteChild(Guid id, CancellationToken ct)
    {
        var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == user.FamilyId, ct);
        if (child is null) return Error.NotFound("child.not_found", "Child not found.").ToProblem();
        // TODO: also cancel the child's future registrations (publish a ChildRemoved event for Participation).
        db.Children.Remove(child);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Error?> ValidateAsync(ChildRequest r, CancellationToken ct)
    {
        var err = Guard.First(Guard.NotBlank(r.FirstName, "first_name", 100), Guard.NotBlank(r.LastName, "last_name", 100));
        if (err is not null) return err;
        if (r.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
            return Error.Validation("birth_date.invalid", "Birth date cannot be in the future.");
        if (r.DivisionId is { } d && await club.GetDivisionAsync(d, ct) is null)
            return Error.NotFound("division.not_found", "Division not found.");
        return null;
    }

    private async Task<ChildDto> ToDtoAsync(Child c, CancellationToken ct)
    {
        string? divisionName = c.DivisionId is { } d ? (await club.GetDivisionAsync(d, ct))?.Name : null;
        return new ChildDto(c.Id, c.FirstName, c.LastName, c.BirthDate, c.DivisionId, divisionName);
    }

    private async Task<IReadOnlyList<ChildDto>> LoadChildrenAsync(Guid familyId, CancellationToken ct)
    {
        var rows = await db.Children.AsNoTracking().Where(c => c.FamilyId == familyId).OrderBy(c => c.FirstName).ToListAsync(ct);
        var names = await club.GetDivisionNamesAsync(rows.Where(c => c.DivisionId != null).Select(c => c.DivisionId!.Value), ct);
        return rows.Select(c => new ChildDto(c.Id, c.FirstName, c.LastName, c.BirthDate, c.DivisionId,
            c.DivisionId is { } d && names.TryGetValue(d, out var n) ? n : null)).ToList();
    }
}
