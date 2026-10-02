using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Actions;

/// <summary>Confirm an email address using a verification token.</summary>
[DomainAction]
public partial class ConfirmEmail : VoidDomainAction<InvalidEmailVerificationTokenError>
{
    private ILocalIdentityStore _store = null!;
    private ISecurityTokenService _tokenService = null!;
    private IClock _clock = null!;

    public required string Email { get; init; }
    public required string Token { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var identity = await _store.FindByEmailAsync(LocalIdentity.NormalizeEmail(Email), ct).ConfigureAwait(false);

        // Constant-time verification path even when the identity/token is absent, so response timing
        // does not leak whether the email exists.
        var storedToken = identity?.EmailVerificationToken ?? string.Empty;
        var tokenValid = _tokenService.VerifyToken(Token, storedToken);

        if (identity is null || identity.EmailVerificationToken is null || identity.EmailVerificationTokenExpiresAt is null)
            return new InvalidEmailVerificationTokenError();

        if (_clock.UtcNow > identity.EmailVerificationTokenExpiresAt)
            return new InvalidEmailVerificationTokenError();

        if (!tokenValid)
            return new InvalidEmailVerificationTokenError();

        identity.EmailVerified = true;
        identity.EmailVerificationToken = null;
        identity.EmailVerificationTokenExpiresAt = null;

        await _store.UpdateAsync(identity, ct).ConfigureAwait(false);

        return Success;
    }
}
