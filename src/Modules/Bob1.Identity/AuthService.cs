using Bob1.Identity.Domain;
using Bob1.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace Bob1.Identity;

internal sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName, string? FamilyName, string? InvitationCode);
internal sealed record LoginRequest(string Email, string Password);
internal sealed record RefreshRequest(string RefreshToken);

internal sealed record UserDto(Guid Id, string Email, string FirstName, string LastName, string[] Roles, Guid? FamilyId);
internal sealed record AuthResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, UserDto User);

internal static class UserMapping
{
    public static UserDto ToDto(this User u) => new(u.Id, u.Email, u.FirstName, u.LastName, u.Roles.ToNames(), u.FamilyId);
}

internal sealed class AuthService(IdentityDbContext db, IPasswordHasher<User> hasher, TokenService tokens, TimeProvider clock)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest r, CancellationToken ct)
    {
        var invalid = Guard.First(
            Guard.NotBlank(r.Email, "email"),
            Guard.NotBlank(r.FirstName, "first_name", 100),
            Guard.NotBlank(r.LastName, "last_name", 100));
        if (invalid is not null) return invalid;
        if (!r.Email.Contains('@')) return Error.Validation("email.invalid", "Invalid email address.");
        if (r.Password is null || r.Password.Length < 8)
            return Error.Validation("password.too_short", "Password must be at least 8 characters.");

        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Error.Conflict("email.taken", "An account with this email already exists.");

        Guid familyId;
        if (!string.IsNullOrWhiteSpace(r.InvitationCode))
        {
            var code = r.InvitationCode.Trim().ToUpperInvariant();
            var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Code == code, ct);
            if (invitation is null || !invitation.IsUsable(Now))
                return Error.Validation("invitation.invalid", "This invitation code is invalid or expired.");
            invitation.MarkUsed(Now);
            familyId = invitation.FamilyId;
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(r.FamilyName) ? r.LastName.Trim() : r.FamilyName.Trim();
            var family = Family.Create(name, Now);
            db.Families.Add(family);
            familyId = family.Id;
        }

        var user = User.Create(email, r.FirstName.Trim(), r.LastName.Trim(), UserRoles.Parent, familyId, Now);
        user.SetPasswordHash(hasher.HashPassword(user, r.Password));
        db.Users.Add(user);

        try
        {
            return await IssueAsync(user, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }) // UNIQUE / PRIMARY KEY
        {
            return Error.Conflict("email.taken", "An account with this email already exists.");
        }
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest r, CancellationToken ct)
    {
        var email = (r.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.IsActive || string.IsNullOrEmpty(r.Password)) return InvalidCredentials;

        var check = hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password);
        if (check == PasswordVerificationResult.Failed) return InvalidCredentials;
        if (check == PasswordVerificationResult.SuccessRehashNeeded)
            user.SetPasswordHash(hasher.HashPassword(user, r.Password));

        return await IssueAsync(user, ct);
    }

    /// <summary>Refresh-token rotation: the presented token is revoked and a new pair is issued.</summary>
    public async Task<Result<AuthResponse>> RefreshAsync(RefreshRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.RefreshToken)) return InvalidToken();
        var hash = TokenService.Hash(r.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive(Now)) return InvalidToken();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || !user.IsActive) return InvalidToken();

        stored.Revoke(Now);
        return await IssueAsync(user, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return;
        stored.Revoke(Now);
        await db.SaveChangesAsync(ct);
    }

    private static Error InvalidToken() => Error.Unauthorized("auth.invalid_refresh_token", "Invalid or expired refresh token.");

    private async Task<Result<AuthResponse>> IssueAsync(User user, CancellationToken ct)
    {
        var (token, hash) = tokens.NewRefreshToken();
        db.RefreshTokens.Add(RefreshToken.Create(user.Id, hash, Now.Add(TokenService.RefreshLifetime), Now));
        await db.SaveChangesAsync(ct);

        return new AuthResponse(tokens.CreateAccessToken(user), token, (int)TokenService.AccessLifetime.TotalSeconds, user.ToDto());
    }
}
