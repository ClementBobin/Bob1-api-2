namespace Bob1.Participation.Features.Registration;

internal static class RegistrationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/matches/{matchId:guid}/children/{childId:guid}/registration")
            .RequireAuthorization(Policies.Parent).WithTags("Participation");

        g.MapPost("", async (Guid matchId, Guid childId, RegisterChildHandler handler, CancellationToken ct) =>
        {
            var r = await handler.RegisterAsync(matchId, childId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        g.MapDelete("", async (Guid matchId, Guid childId, RegisterChildHandler handler, CancellationToken ct) =>
        {
            var r = await handler.UnregisterAsync(matchId, childId, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });
    }
}
