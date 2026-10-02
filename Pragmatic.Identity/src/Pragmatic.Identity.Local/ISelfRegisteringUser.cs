namespace Pragmatic.Identity.Local;

/// <summary>
///     A <c>[PragmaticUser]</c> entity that <c>RegisterUser</c> may create from a local identity.
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>{User}.LocalIdentityStore</c> finds, updates and checks users by their local
///         credentials without being told how. Creating one it cannot guess: what else a new user needs — a
///         name, a role, a team — is the application's to say. An entity that implements this says it; one
///         that does not creates its users itself (an administrator provisions them), and the store refuses
///         a self-registration.
///     </para>
///     <para>
///         The returned user carries the identity: <see cref="Register" /> sets it, because
///         the property that holds it may not be settable from outside the entity.
///     </para>
/// </remarks>
/// <typeparam name="TSelf">The user entity itself.</typeparam>
public interface ISelfRegisteringUser<TSelf>
    where TSelf : class, ISelfRegisteringUser<TSelf>
{
    /// <summary>A new user whose local credentials are <paramref name="identity" />.</summary>
    static abstract TSelf Register(LocalIdentity identity);
}
