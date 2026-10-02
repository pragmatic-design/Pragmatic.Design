namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Token generation for password reset and email verification.
/// </summary>
public interface ISecurityTokenService
{
    /// <summary>Generates a cryptographically secure reset token.</summary>
    string GenerateToken();

    /// <summary>Hashes a token for secure storage.</summary>
    string HashToken(string token);

    /// <summary>Verifies a plaintext token against a stored hash.</summary>
    bool VerifyToken(string token, string storedHash);
}
