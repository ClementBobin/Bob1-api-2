using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Identity.Api;

[Route(ApiRoutes.V1 + "/auth")]
[Tags("Auth")]
internal sealed class AuthController(AuthService svc) : ApiControllerBase
{
    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest r, CancellationToken ct)
    {
        var result = await svc.RegisterAsync(r, ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToProblem();
    }

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest r, CancellationToken ct)
    {
        var result = await svc.LoginAsync(r, ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToProblem();
    }

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest r, CancellationToken ct)
    {
        var result = await svc.RefreshAsync(r, ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToProblem();
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(RefreshRequest r, CancellationToken ct)
    {
        await svc.LogoutAsync(r.RefreshToken, ct);
        return NoContent();
    }
}
