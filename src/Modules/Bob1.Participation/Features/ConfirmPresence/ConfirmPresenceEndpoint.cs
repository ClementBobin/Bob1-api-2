using Bob1.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bob1.Participation.Features.ConfirmPresence;

internal static class ConfirmPresenceEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/matches/{matchId:guid}/presence/confirm",
                async (Guid matchId, ConfirmPresenceHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(new ConfirmPresenceCommand(matchId), ct);
                    return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
                })
            .RequireAuthorization(Policies.Parent)
            .WithTags("Participation")
            .WithName("ConfirmPresence")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
