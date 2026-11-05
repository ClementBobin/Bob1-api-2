using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Participation.Api;

[Route(ApiRoutes.V1)]
[Authorize(Policy = Policies.Parent)]
[Tags("Participation")]
internal sealed class ConfirmPresenceController(ConfirmPresenceHandler handler) : ApiControllerBase
{
    [HttpPost("matches/{matchId:guid}/presence/confirm", Name = "ConfirmPresence")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(Guid matchId, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new ConfirmPresenceCommand(matchId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToProblem();
    }
}
