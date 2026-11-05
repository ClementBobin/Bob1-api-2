using Bob1.Shared.Contracts.Identity;

namespace Bob1.Modules.Identity.Application;

internal sealed class IdentityApi(IdentityDbContext db) : IIdentityApi
{
    public Task<ChildInfo?> GetChildAsync(Guid childId, CancellationToken ct) =>
        db.Children.AsNoTracking().Where(c => c.Id == childId)
            .Select(c => new ChildInfo(c.Id, c.FamilyId, c.FirstName, c.LastName, c.DivisionId))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ChildInfo>> GetChildrenOfFamilyAsync(Guid familyId, CancellationToken ct) =>
        await db.Children.AsNoTracking().Where(c => c.FamilyId == familyId).OrderBy(c => c.FirstName)
            .Select(c => new ChildInfo(c.Id, c.FamilyId, c.FirstName, c.LastName, c.DivisionId))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetFamilyNamesAsync(IEnumerable<Guid> familyIds, CancellationToken ct)
    {
        var ids = familyIds.Distinct().ToList();
        return await db.Families.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsOfFamilyAsync(Guid familyId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.FamilyId == familyId && u.IsActive).Select(u => u.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsByDivisionAsync(Guid? divisionId, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (divisionId is { } d)
        {
            var familyIds = db.Children.Where(c => c.DivisionId == d).Select(c => c.FamilyId);
            users = users.Where(u => u.FamilyId != null && familyIds.Contains(u.FamilyId.Value));
        }
        return await users.Select(u => u.Id).ToListAsync(ct);
    }

    // New methods for person-level rankings
    public async Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(IEnumerable<Guid> personIds, CancellationToken ct)
    {
        var ids = personIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
    }

    public async Task<IReadOnlyDictionary<Guid, Guid?>> GetPersonFamilyMapAsync(CancellationToken ct)
    {
        return await db.Users.AsNoTracking().Where(u => u.FamilyId != null)
            .ToDictionaryAsync(u => u.Id, u => u.FamilyId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetFamilyNamesForPersonsAsync(IEnumerable<Guid> personIds, CancellationToken ct)
    {
        var ids = personIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.FamilyId != null)
            .Select(u => new { u.Id, u.FamilyId }).ToListAsync(ct);
        
        var familyIds = users.Select(u => u.FamilyId!.Value).Distinct().ToList();
        var familyNames = await db.Families.AsNoTracking().Where(f => familyIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        
        var result = new Dictionary<Guid, string>();
        foreach (var user in users)
        {
            if (user.FamilyId.HasValue && familyNames.TryGetValue(user.FamilyId.Value, out var name))
                result[user.Id] = name;
        }
        return result;
    }
}
