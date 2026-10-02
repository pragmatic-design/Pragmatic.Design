namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Delivers a freshly-issued password reset token to the identity owner out-of-band
///     (typically email or SMS). The plaintext token must NEVER be returned through the API
///     response — exposing it would let anyone who knows a victim's email reset their password.
///     Replace the default <see cref="LogOnlyPasswordResetNotifier" /> with an email/SMS-backed
///     implementation in production.
/// </summary>
public interface IPasswordResetNotifier
{
    /// <summary>Delivers the plaintext reset token to the identity owner.</summary>
    /// <param name="email">The destination email of the requesting identity.</param>
    /// <param name="token">The plaintext, short-lived reset token.</param>
    /// <param name="expiresAt">When the token stops being valid.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default);
}
