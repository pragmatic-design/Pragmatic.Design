using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Actions;

/// <summary>
///     Request a password reset. Always succeeds (does not reveal whether the email exists).
///     When the email matches an active identity, a reset token is generated, hashed for storage,
///     and delivered out-of-band via <see cref="IPasswordResetNotifier" /> — the plaintext token
///     is NEVER returned in the response, so it cannot be harvested by an attacker who only knows
///     the victim's email.
/// </summary>
[DomainAction]
public partial class RequestPasswordReset : VoidDomainAction
{
    private ILocalIdentityStore _store = null!;
    private ISecurityTokenService _tokenService = null!;
    private IPasswordResetNotifier _notifier = null!;
    private IOptions<LocalIdentityOptions> _options = null!;
    private IClock _clock = null!;

    public required string Email { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var identity = await _store.FindByEmailAsync(LocalIdentity.NormalizeEmail(Email), ct).ConfigureAwait(false);
        if (identity is null || !identity.IsActive || identity.Email is null)
        {
            // Do not reveal whether the email exists — succeed without issuing a token.
            // Perform comparable throwaway token work (and discard it) so this miss/inactive branch is
            // not trivially separable from the found branch by response latency. No token is persisted
            // and no notification is sent — those would create a side effect / leak existence.
            _ = _tokenService.HashToken(_tokenService.GenerateToken());
            return Success;
        }

        var token = _tokenService.GenerateToken();
        identity.ResetToken = _tokenService.HashToken(token);
        var expiresAt = _clock.UtcNow.Add(_options.Value.ResetTokenExpiry);
        identity.ResetTokenExpiresAt = expiresAt;

        await _store.UpdateAsync(identity, ct).ConfigureAwait(false);

        // Deliver the plaintext token out-of-band; it must never appear in the HTTP response.
        await _notifier.NotifyAsync(identity.Email, token, expiresAt, ct).ConfigureAwait(false);

        return Success;
    }
}
