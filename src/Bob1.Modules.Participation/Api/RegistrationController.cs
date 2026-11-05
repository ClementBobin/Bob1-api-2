using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Participation.Api;

[Route(ApiRoutes.V1 + "/matches/{matchId:guid}/children/{childId:guid}/registration")]
[Authorize(Policy = Policies.Parent)]
[Tags("Participation")]
internal sealed class RegistrationController(RegisterChildHandler handler) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Register(Guid matchId, Guid childId, CancellationToken ct)
    {
        var r = await handler.RegisterAsync(matchId, childId, ct);
        return r.IsSuccess ? NoContent() : r.Error.ToProblem();
    }

    [HttpDelete]
    public async Task<IActionResult> Unregister(Guid matchId, Guid childId, CancellationToken ct)
    {
        var r = await handler.UnregisterAsync(matchId, childId, ct);
        return r.IsSuccess ? NoContent() : r.Error.ToProblem();
    }
}
