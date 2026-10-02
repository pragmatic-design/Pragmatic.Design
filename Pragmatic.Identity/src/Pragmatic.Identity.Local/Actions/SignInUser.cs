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

/// <summary>Signs in with local credentials (email + password) and issues the access token.</summary>
/// <remarks>
///     <para>
///         The credentials are checked exactly as <see cref="LoginUser" /> checks them — lockout, timing
///         equalisation, the password rehash, the events. This adds the token: the account's key and
///         security stamp always, and whatever the application contributes
///         (<see cref="IUserClaimsContributor" />), signed by the <see cref="IAccessTokenIssuer" /> the host
///         registered.
///     </para>
///     <para>
///         Without it every application writes the same sign-in and the same issuer outside the package,
///         and a copy that leaves out the security stamp issues tokens refused on first use.
///     </para>
/// </remarks>
[DomainAction]
public partial class SignInUser
    : DomainAction<AccessToken, InvalidCredentialsError, AccountLockedError, IdentityNotActiveError, EmailNotVerifiedError>
{
    private ILocalIdentityStore _store = null!;
    private IPasswordHasher _hasher = null!;
    private IOptions<LocalIdentityOptions> _options = null!;
    private IClock _clock = null!;
    private IDomainEventDispatcher _events = null!;
    private ILogger<SignInUser> _rehashLogger = null!;
    private IAccessTokenIssuer _tokens = null!;
    private IEnumerable<IUserClaimsContributor> _contributors = null!;

    public required string Email { get; init; }

    public required string Password { get; init; }

    public override async Task<Result<AccessToken, IError>> Execute(CancellationToken ct = default)
    {
        var verified = await LocalCredentialCheck
            .VerifyAsync(Email, Password, _store, _hasher, _options.Value, _clock, _events, _rehashLogger, ct)
            .ConfigureAwait(false);
        if (verified.IsFailure)
            return Result<AccessToken, IError>.Failure(verified.Error);

        var identity = verified.Value;
        var claims = new SignInClaims(identity);
        foreach (var contributor in _contributors)
            await contributor.ContributeAsync(identity, claims, ct).ConfigureAwait(false);

        return _tokens.Issue(claims);
    }
}
