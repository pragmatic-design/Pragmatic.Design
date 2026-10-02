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
    ///     Synchronous variant for use inside expression-building paths (e.g., EF Core filter expressions)
    ///     where async is not supported. Implementations that perform I/O should override this to run
    ///     the work on a thread-pool thread via <c>Task.Run</c> to avoid ASP.NET Core deadlocks.
    /// </summary>
    /// <param name="user">The current user context.</param>
    /// <returns>A set of scope identifiers the user has access to.</returns>
    IReadOnlySet<string> ResolveAccessScopes(ICurrentUser user)
        // Default: run on thread-pool to avoid sync-context deadlock, then block
        => Task.Run(() => ResolveAccessScopesAsync(user)).GetAwaiter().GetResult();
}
