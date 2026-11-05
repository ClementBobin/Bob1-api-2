namespace Bob1.Shared;

/// <summary>Who is calling. Implemented in Bob1.Api from the JWT claims; modules only see this interface.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid PersonId { get; }
    Guid? FamilyId { get; }
    bool IsInRole(string role);
}

public sealed record JwtOptions(string Issuer, string Audience, string Key);

public static class Policies
{
    public const string Parent = nameof(Parent);
    public const string Staff = nameof(Staff);
    public const string Admin = nameof(Admin);
}
