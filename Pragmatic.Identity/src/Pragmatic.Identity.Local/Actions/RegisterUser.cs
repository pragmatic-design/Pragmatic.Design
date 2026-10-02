using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Actions;

/// <summary>Register a new user with local credentials.</summary>
[DomainAction]
public partial class RegisterUser : DomainAction<string, EmailAlreadyExistsError, PasswordPolicyError>
{
    private ILocalIdentityStore _store = null!;
    private IPasswordHasher _hasher = null!;
    private IPasswordPolicy _passwordPolicy = null!;
    private IDomainEventDispatcher _events = null!;
    private IClock _clock = null!;
    private IOptions<LocalIdentityOptions> _options = null!;

    public required string Email { get; init; }
    public required string Password { get; init; }

    public override async Task<Result<string, IError>> Execute(CancellationToken ct = default)
    {
        // Validate password policy — this is about the *submitted password*, not account existence,
        // so it is surfaced in both modes.
        var policyResult = _passwordPolicy.Validate(Password);
        if (!policyResult.IsSuccess)
            return Result<string, IError>.Failure(policyResult.Error);

        // Hash password before checking email existence so that response timing does not
        // differ between "email taken" and "email free" paths (hashing is the slow step).
        var normalizedEmail = LocalIdentity.NormalizeEmail(Email);
        // The external key is a deterministic function of the email, so returning it never reveals
        // whether the account already existed — a fresh registration would return the same value.
        // Through the shared composer, with "local" as the issuer: the local store IS the identity
        // provider here, whatever issuer later signs the token.
        var externalKey = ExternalIdentityKey.Compose(LocalIdentity.Provider, normalizedEmail)!;
        var passwordHash = _hasher.Hash(Password);

        if (await _store.EmailExistsAsync(normalizedEmail, ct).ConfigureAwait(false))
        {
            if (_options.Value.RevealAccountState)
                return new EmailAlreadyExistsError(normalizedEmail);

            // Uniform (default): do not confirm existence. Return the same success shape as a fresh
            // registration without creating a duplicate or dispatching UserRegistered. The response
            // body is identical to a genuine new registration, so it is not an existence oracle.
            return externalKey;
        }

        var identity = new LocalIdentity
        {
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            ExternalIdentityKey = externalKey,
            ProvisionSource = ProvisionSource.Manual,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var created = await _store.CreateAsync(identity, ct).ConfigureAwait(false);

        await _events.DispatchAsync(
            new UserRegistered(created.ExternalIdentityKey, Email, _clock.UtcNow), ct).ConfigureAwait(false);

        return created.ExternalIdentityKey;
    }
}
