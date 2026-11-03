using Bob1.Identity.Domain;
using Bob1.Identity.Infrastructure;

namespace Bob1.Identity.Features;

internal sealed record AdminFamilyMemberDto(Guid Id, string FirstName, string LastName, string Email, string[] Roles, bool IsActive);
internal sealed record AdminFamilyChildDto(Guid Id, string FirstName, string LastName, DateOnly BirthDate, Guid? DivisionId);
internal sealed record AdminFamilyDto(Guid Id, string Name, IReadOnlyList<AdminFamilyMemberDto> Members,
    IReadOnlyList<AdminFamilyChildDto> Children, DateTime CreatedAtUtc);
internal sealed record AdminFamilyRequest(string Name);

internal static class AdminFamilyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/families").RequireAuthorization(Policies.Admin).WithTags("Families (admin)");

        admin.MapGet("", async (string? search, int? skip, int? take, IdentityDbContext db, CancellationToken ct) =>
        {
            var q = db.Families.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                q = q.Where(f => f.Name.ToLower().Contains(s));
            }

            var total = await q.CountAsync(ct);
            var families = await q.OrderBy(f => f.Name)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 100, 1, 300)).ToListAsync(ct);

            var ids = families.Select(f => f.Id).ToList();
            var users = await db.Users.AsNoTracking()
                .Where(u => u.FamilyId != null && ids.Contains(u.FamilyId.Value)).ToListAsync(ct);
            var children = await db.Children.AsNoTracking()
                .Where(c => ids.Contains(c.FamilyId)).ToListAsync(ct);

            var usersByFamily = users.ToLookup(u => u.FamilyId!.Value);
            var childrenByFamily = children.ToLookup(c => c.FamilyId);

            var items = families.Select(f => new AdminFamilyDto(
                f.Id, f.Name,
                usersByFamily[f.Id].OrderBy(u => u.FirstName)
                    .Select(u => new AdminFamilyMemberDto(u.Id, u.FirstName, u.LastName, u.Email,
                        u.Roles.ToNames(), u.IsActive)).ToList(),
                childrenByFamily[f.Id].OrderBy(c => c.FirstName)
                    .Select(c => new AdminFamilyChildDto(c.Id, c.FirstName, c.LastName, c.BirthDate, c.DivisionId)).ToList(),
                f.CreatedAtUtc)).ToList();

            return Results.Ok(new { total, items });
        });

        admin.MapPost("", async (AdminFamilyRequest r, IdentityDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 150) is { } err) return err.ToProblem();
            var name = r.Name.Trim();
            if (await db.Families.AnyAsync(f => f.Name == name, ct))
                return Error.Conflict("family.name_taken", "A family with this name already exists.").ToProblem();

            var family = Family.Create(name, clock.GetUtcNow().UtcDateTime);
            db.Families.Add(family);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/families/{family.Id}",
                new AdminFamilyDto(family.Id, family.Name, [], [], family.CreatedAtUtc));
        });

        admin.MapPut("/{id:guid}", async (Guid id, AdminFamilyRequest r, IdentityDbContext db, CancellationToken ct) =>
        {
            if (Guard.NotBlank(r.Name, "name", 150) is { } err) return err.ToProblem();
            var family = await db.Families.SingleOrDefaultAsync(f => f.Id == id, ct);
            if (family is null) return Error.NotFound("family.not_found", "Family not found.").ToProblem();

            var name = r.Name.Trim();
            if (await db.Families.AnyAsync(f => f.Id != id && f.Name == name, ct))
                return Error.Conflict("family.name_taken", "A family with this name already exists.").ToProblem();

            family.Rename(name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new AdminFamilyDto(family.Id, family.Name, [], [], family.CreatedAtUtc));
        });

        admin.MapDelete("/{id:guid}", async (Guid id, IdentityDbContext db, CancellationToken ct) =>
        {
            var family = await db.Families.SingleOrDefaultAsync(f => f.Id == id, ct);
            if (family is null) return Error.NotFound("family.not_found", "Family not found.").ToProblem();
            if (await db.Users.AnyAsync(u => u.FamilyId == id, ct))
                return Error.Conflict("family.has_members", "Move or remove the members of this family first.").ToProblem();
            if (await db.Children.AnyAsync(c => c.FamilyId == id, ct))
                return Error.Conflict("family.has_children", "Remove the children of this family first.").ToProblem();

            db.Families.Remove(family);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
