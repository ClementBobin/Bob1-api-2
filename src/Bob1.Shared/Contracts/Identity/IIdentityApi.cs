namespace Bob1.Shared.Contracts.Identity;

/// <summary>What other modules may ask Identity. Everything else about users/families stays private.</summary>
public interface IIdentityApi
{
    Task<ChildInfo?> GetChildAsync(Guid childId, CancellationToken ct);
    Task<IReadOnlyList<ChildInfo>> GetChildrenOfFamilyAsync(Guid familyId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetFamilyNamesAsync(IEnumerable<Guid> familyIds, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetUserIdsOfFamilyAsync(Guid familyId, CancellationToken ct);
    /// <summary>Users whose family has a child in this division. Null division = every active user.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsByDivisionAsync(Guid? divisionId, CancellationToken ct);
    
    // New methods for person-level rankings
    Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(IEnumerable<Guid> personIds, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, Guid?>> GetPersonFamilyMapAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetFamilyNamesForPersonsAsync(IEnumerable<Guid> personIds, CancellationToken ct);
}

public sealed record ChildInfo(Guid Id, Guid FamilyId, string FirstName, string LastName, Guid? DivisionId);
