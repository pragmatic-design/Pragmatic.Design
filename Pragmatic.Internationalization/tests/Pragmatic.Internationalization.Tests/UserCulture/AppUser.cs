using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Internationalization.Tests.UserCulture;

/// <summary>
///     The application user of the wiring suite, in the shape the Showcase already has: a
///     <c>[PragmaticUser]</c> with a <c>[ProfileProperty] PreferredCulture</c>.
/// </summary>
/// <remarks>
///     <c>MatchProperty</c> is named explicitly so the resolver matches the claim directly. The default
///     composes <c>issuer|subject</c> against <c>ExternalIdentityKey</c>, which would make this suite
///     about claim composition rather than about the culture.
/// </remarks>
/// <remarks>
///     <c>IEntity</c> because the generated resolver reads through
///     <c>IReadRepository&lt;TEntity&gt;</c>, which is constrained to it. The fixture is not a plain
///     POCO reached through a <c>DbContext</c> the suite registers by hand: registering that context by
///     hand would make a resolver that depends on it look workable, when no Pragmatic application
///     registers one.
/// </remarks>
[PragmaticUser(MatchClaim = "sub", MatchProperty = nameof(UserKey))]
public partial class AppUser : IEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid PersistenceId => Id;

    /// <summary>What the "sub" claim is matched against.</summary>
    public string UserKey { get; set; } = string.Empty;

    /// <summary>The preference the generated provider resolves the UI culture from.</summary>
    [ProfileProperty]
    public string? PreferredCulture { get; set; }
}
