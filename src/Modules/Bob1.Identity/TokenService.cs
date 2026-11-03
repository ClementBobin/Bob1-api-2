using System.Security.Cryptography;
using System.Text;
using Bob1.Identity.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bob1.Identity;

internal sealed class TokenService(JwtOptions jwt, TimeProvider clock)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    public string CreateAccessToken(User user)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["person_id"] = user.Id.ToString(), // Person ID is the same as User ID
            ["email"] = user.Email,
            ["name"] = user.DisplayName,
            ["role"] = user.Roles.ToNames()
        };
        if (user.FamilyId is { } familyId)
            claims["family_id"] = familyId.ToString();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = clock.GetUtcNow().UtcDateTime.Add(AccessLifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256),
            Claims = claims
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Opaque random token for the client + its SHA-256 hash for the database (never store the raw token).</summary>
    public (string Token, string Hash) NewRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
