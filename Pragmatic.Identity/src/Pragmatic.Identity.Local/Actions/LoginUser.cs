using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Actions;

/// <summary>Authenticate with local credentials (email + password).</summary>
/// <remarks>
///     Answers who the account is, and issues nothing: <see cref="SignInUser" /> is the same check followed by
///     the access token.
/// </remarks>
[DomainAction]
public partial class LoginUser : DomainAction<LoginResult, InvalidCredentialsError, AccountLockedError, IdentityNotActiveError, EmailNotVerifiedError>
{
    private ILocalIdentityStore _store = null!;
    private IPasswordHasher _hasher = null!;
    private IOptions<LocalIdentityOptions> _options = null!;
    private IClock _clock = null!;
    private IDomainEventDispatcher _events = null!;
    private ILogger<LoginUser> _rehashLogger = null!;

    public required string Email { get; init; }
    public required string Password { get; init; }

    public override async Task<Result<LoginResult, IError>> Execute(CancellationToken ct = default)
    {
        var verified = await LocalCredentialCheck
            .VerifyAsync(Email, Password, _store, _hasher, _options.Value, _clock, _events, _rehashLogger, ct)
            .ConfigureAwait(false);
        if (verified.IsFailure)
            return Result<LoginResult, IError>.Failure(verified.Error);

        return new LoginResult(verified.Value.ExternalIdentityKey, verified.Value.LastLoginAt!.Value);
    }
}
