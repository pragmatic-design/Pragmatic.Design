using Pragmatic.Identity;

namespace Pragmatic.Authorization;

/// <summary>
///     Resolves the set of data access scopes for the current user.
///     Scopes are expanded from user identity, roles, groups, and explicit grants.
/// </summary>
public interface IUserScopeResolver
{
    /// <summary>
    ///     Resolves the expanded set of scope identifiers for the given user.
    ///     Typically includes <c>"user:{id}"</c>, <c>"role:{roleName}"</c>,
    ///     and <c>"scope:{customScope}"</c> entries.
    /// </summary>
    /// <param name="user">The current user context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A set of scope identifiers the user has access to.</returns>
    Task<IReadOnlySet<string>> ResolveAccessScopesAsync(ICurrentUser user, CancellationToken ct = default);

    /// <summary>
    ///     Synchronous variant, called by every generated data filter while it builds the query's expression,
    ///     where async is not supported: once per query over an entity with scopes.
    /// </summary>
    /// <remarks>
    ///     No default, on purpose. The one this interface used to carry ran the asynchronous method with
    ///     <c>Task.Run</c> and blocked on it: two thread-pool threads per query, for implementations whose scopes
    ///     come from the principal already in memory. An implementation decides how it answers synchronously, and
    ///     one that must reach I/O should compute the scopes once per request rather than per query.
    /// </remarks>
    /// <param name="user">The current user context.</param>
    /// <returns>A set of scope identifiers the user has access to.</returns>
    IReadOnlySet<string> ResolveAccessScopes(ICurrentUser user);
}
