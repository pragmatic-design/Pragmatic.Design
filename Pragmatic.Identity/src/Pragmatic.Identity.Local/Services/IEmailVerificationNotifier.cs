namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Delivers a freshly-issued email-verification token to the identity owner out-of-band (typically
///     email). Like the reset token, the plaintext verification token must NEVER be returned through the
///     API response. Replace the default <see cref="LogOnlyEmailVerificationNotifier" /> with an
///     email-backed implementation in production.
/// </summary>
public interface IEmailVerificationNotifier
{
    /// <summary>Delivers the plaintext verification token to the identity owner.</summary>
    /// <param name="email">The destination email of the identity to verify.</param>
    /// <param name="token">The plaintext, short-lived verification token.</param>
    /// <param name="expiresAt">When the token stops being valid.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default);
}
