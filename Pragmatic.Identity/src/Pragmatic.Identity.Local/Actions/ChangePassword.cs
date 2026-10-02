using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Permissions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Actions;

/// <summary>Change password for the currently authenticated user.</summary>
[DomainAction]
[RequirePermission(LocalIdentityPermissions.ChangePassword)]
public partial class ChangePassword : VoidDomainAction<InvalidCredentialsError, IdentityNotActiveError, PasswordPolicyError>
{
    private ILocalIdentityStore _store = null!;
    private IPasswordHasher _hasher = null!;
    private ICurrentUser _currentUser = null!;
    private IPasswordPolicy _passwordPolicy = null!;
    private IDomainEventDispatcher _events = null!;
    private IClock _clock = null!;

    public required string CurrentPassword { get; init; }
    public required string NewPassword { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        // Validate new password policy
        var policyResult = _passwordPolicy.Validate(NewPassword);
        if (!policyResult.IsSuccess)
            return VoidResult<IError>.Failure(policyResult.Error);

        var externalKey = _currentUser.Authentication.ExternalIdentityKey;
        if (string.IsNullOrEmpty(externalKey))
            return new IdentityNotActiveError();

        var identity = await _store.FindByExternalKeyAsync(externalKey, ct).ConfigureAwait(false);

        if (identity is null || !identity.IsActive)
            return new IdentityNotActiveError();

        if (!_hasher.Verify(CurrentPassword, identity.PasswordHash))
            return new InvalidCredentialsError();

        identity.PasswordHash = _hasher.Hash(NewPassword);
        // Rotate the security stamp so tokens issued before the change fail the per-request stamp check.
        identity.SecurityStamp = Guid.NewGuid().ToString("N");
        await _store.UpdateAsync(identity, ct).ConfigureAwait(false);

        await _events.DispatchAsync(
            new PasswordChanged(identity.ExternalIdentityKey, _clock.UtcNow), ct).ConfigureAwait(false);

        return Success;
    }
}
