using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Actions;

/// <summary>Confirm a password reset using a token.</summary>
[DomainAction]
public partial class ConfirmPasswordReset : VoidDomainAction<InvalidResetTokenError, PasswordPolicyError>
{
    private ILocalIdentityStore _store = null!;
    private IPasswordHasher _hasher = null!;
    private ISecurityTokenService _tokenService = null!;
    private IPasswordPolicy _passwordPolicy = null!;
    private IDomainEventDispatcher _events = null!;
    private IClock _clock = null!;

    public required string Email { get; init; }
    public required string Token { get; init; }
    public required string NewPassword { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        // Validate new password policy
        var policyResult = _passwordPolicy.Validate(NewPassword);
        if (!policyResult.IsSuccess)
            return VoidResult<IError>.Failure(policyResult.Error);

        var identity = await _store.FindByEmailAsync(LocalIdentity.NormalizeEmail(Email), ct).ConfigureAwait(false);

        // Use a constant-time comparison path even when the identity is not found,
        // so response timing does not leak whether the email exists.
        var storedToken = identity?.ResetToken ?? string.Empty;
        var tokenValid = _tokenService.VerifyToken(Token, storedToken);

        if (identity is null || identity.ResetToken is null || identity.ResetTokenExpiresAt is null)
            return new InvalidResetTokenError();

        if (_clock.UtcNow > identity.ResetTokenExpiresAt)
            return new InvalidResetTokenError();

        if (!tokenValid)
            return new InvalidResetTokenError();

        // A reset that clears an active lockout / failed-attempt counter is an audit-worthy
        // side effect distinct from the reset itself — capture whether it actually unlocked.
        var wasLockedOrFailed = identity.LockoutEnd is not null || identity.FailedLoginAttempts > 0;

        // Apply new password and clear reset state
        identity.PasswordHash = _hasher.Hash(NewPassword);
        identity.ResetToken = null;
        identity.ResetTokenExpiresAt = null;
        identity.FailedLoginAttempts = 0;
        identity.LockoutEnd = null;
        // Rotate the security stamp so tokens issued before the reset fail the per-request stamp check.
        identity.SecurityStamp = Guid.NewGuid().ToString("N");

        await _store.UpdateAsync(identity, ct).ConfigureAwait(false);

        await _events.DispatchAsync(
            new PasswordResetCompleted(identity.ExternalIdentityKey, _clock.UtcNow), ct).ConfigureAwait(false);

        if (wasLockedOrFailed)
            await _events.DispatchAsync(
                new AccountUnlocked(identity.ExternalIdentityKey, _clock.UtcNow), ct).ConfigureAwait(false);

        return Success;
    }
}
