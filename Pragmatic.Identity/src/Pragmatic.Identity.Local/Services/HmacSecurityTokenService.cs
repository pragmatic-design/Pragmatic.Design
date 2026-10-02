using System.Security.Cryptography;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     HMAC-SHA256 based token service for password reset and email verification.
/// </summary>
[Service(Lifetime = Lifetime.Singleton)]
public sealed class HmacSecurityTokenService : ISecurityTokenService
{
    private const int TokenSizeBytes = 32;

    /// <inheritdoc />
    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc />
    public string HashToken(string token)
    {
        try
        {
            var bytes = Convert.FromBase64String(token);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }
        catch (FormatException)
        {
            // Malformed token — return empty string so callers get a predictable non-match.
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public bool VerifyToken(string token, string storedHash)
    {
        try
        {
            var computedHash = HashToken(token);
            if (computedHash.Length == 0 || storedHash.Length == 0)
                return false;
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(computedHash),
                Convert.FromBase64String(storedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
