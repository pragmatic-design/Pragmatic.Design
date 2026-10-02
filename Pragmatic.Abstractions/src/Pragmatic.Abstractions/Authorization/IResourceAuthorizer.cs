using Pragmatic.Identity;

namespace Pragmatic.Authorization;

/// <summary>
///     Authorizes access to a specific resource instance (ABAC / resource-level authorization).
///     One authorizer per concrete resource type: an authorizer registered for a base type does
///     <b>not</b> apply to its derived types.
/// </summary>
/// <remarks>
///     <para>
///         The interface is deliberately not contravariant: <c>in TResource</c> would promise the
///         opposite, and it is a promise the framework cannot keep. Authorizers are resolved from the DI
///         container, and a container keys registrations by the closed generic type without applying
///         variance — <c>GetService(IResourceAuthorizer&lt;Derived&gt;)</c> returns <c>null</c> even when
///         <c>IResourceAuthorizer&lt;Base&gt;</c> is registered and the cast would succeed. The variance
///         annotation would let a derived action compile as protected and run unprotected.
///     </para>
///     <para>
///         Register an authorizer for every concrete type that needs one. The action pipeline denies,
///         rather than passing silently, when a type's base is covered but the type itself is not —
///         see <see cref="IResourceAuthorizerCatalog" />.
///     </para>
/// </remarks>
/// <typeparam name="TResource">The resource type to authorize access for.</typeparam>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IResourceAuthorizer<TResource>
{
    /// <summary>
    ///     Determines whether the user can perform the specified action on the resource.
    /// </summary>
    /// <param name="user">The current user requesting access.</param>
    /// <param name="resource">The resource instance being accessed.</param>
    /// <param name="action">The action being performed (e.g., the action type name).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if access is granted; otherwise <c>false</c>.</returns>
    ValueTask<bool> CanAccessAsync(
        ICurrentUser user, TResource resource, string action,
        CancellationToken ct = default);
}
