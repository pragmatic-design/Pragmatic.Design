namespace Pragmatic.Authorization;

/// <summary>
///     The set of resource types an <see cref="IResourceAuthorizer{TResource}" /> was registered for.
/// </summary>
/// <remarks>
///     <para>
///         A DI container keys registrations by the closed generic type, so it can answer
///         "is there an authorizer for exactly <c>T</c>?" and nothing else. It cannot answer
///         "did anyone register an authorizer for a base of <c>T</c>?" — and that question is the
///         difference between an action deliberately left unprotected and one whose protection will
///         never be applied.
///     </para>
///     <para>
///         Registration is a runtime call, not an attribute, so the source generator cannot see it
///         either. This catalog is how the fact reaches the action pipeline: the resource authorization
///         filter walks the base chain of the action type and, if a base is covered here while the
///         concrete type resolves nothing, denies instead of passing silently.
///     </para>
///     <para>
///         Populated during service registration and read afterwards; implementations must tolerate
///         concurrent reads.
///     </para>
/// </remarks>
public interface IResourceAuthorizerCatalog
{
    /// <summary>
    ///     Whether an <see cref="IResourceAuthorizer{TResource}" /> was registered for exactly
    ///     <paramref name="resourceType" />. Base types are not considered — the caller decides what a
    ///     match on a base means.
    /// </summary>
    /// <param name="resourceType">The resource (or action) type to look up.</param>
    bool Covers(Type resourceType);
}
