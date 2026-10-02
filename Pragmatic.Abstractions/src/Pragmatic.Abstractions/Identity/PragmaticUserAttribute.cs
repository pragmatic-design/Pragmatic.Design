namespace Pragmatic.Identity;

/// <summary>
///     Marks a class as the application's user entity for Pragmatic Identity integration.
///     The source generator uses this to produce:
///     <list type="bullet">
///         <item>A profile adapter implementing <see cref="IUserProfile"/> from <c>[ProfileProperty]</c> fields.</item>
///         <item>A resolver service that loads the user entity from <see cref="ICurrentUser"/> claims.</item>
///     </list>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PragmaticUserAttribute : Attribute
{
    /// <summary>
    ///     The claim type used to match the current user to the entity.
    ///     Defaults to <c>"sub"</c> (the OIDC subject claim).
    /// </summary>
    public string MatchClaim { get; set; } = "sub";

    /// <summary>
    ///     The entity property to match against the claim value.
    ///     If not specified, defaults to <c>ExternalIdentityKey</c>.
    /// </summary>
    public string? MatchProperty { get; set; }
}
