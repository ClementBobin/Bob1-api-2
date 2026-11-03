namespace Bob1.Identity.Features;

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/register", async (RegisterRequest r, AuthService svc, CancellationToken ct) =>
        {
            var result = await svc.RegisterAsync(r, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        }).AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest r, AuthService svc, CancellationToken ct) =>
        {
            var result = await svc.LoginAsync(r, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        }).AllowAnonymous();

        auth.MapPost("/refresh", async (RefreshRequest r, AuthService svc, CancellationToken ct) =>
        {
            var result = await svc.RefreshAsync(r, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        }).AllowAnonymous();

        auth.MapPost("/logout", async (RefreshRequest r, AuthService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(r.RefreshToken, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
