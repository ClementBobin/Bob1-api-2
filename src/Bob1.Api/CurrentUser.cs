using System.Security.Claims;
using Bob1.SharedKernel;

namespace Bob1.Api;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid UserId => Guid.TryParse(Principal?.FindFirstValue("sub"), out var id)
        ? id
        : throw new InvalidOperationException("No authenticated user.");

    public Guid PersonId => Guid.TryParse(Principal?.FindFirstValue("person_id"), out var id)
        ? id
        : UserId; // Fallback to UserId if person_id claim not present

    public Guid? FamilyId => Guid.TryParse(Principal?.FindFirstValue("family_id"), out var id) ? id : null;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
