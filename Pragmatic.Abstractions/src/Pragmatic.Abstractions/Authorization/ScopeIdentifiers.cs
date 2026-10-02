namespace Pragmatic.Authorization;

/// <summary>
///     The spelling of a scope identifier, in one place.
/// </summary>
/// <remarks>
///     <para>
///         A scoped row is visible when one of its <c>AccessScopes</c> is one of the caller's, and the
///         comparison is a plain string match: <c>entity.AccessScopes.Any(s =&gt; userScopes.Contains(s))</c>.
///         So the side that <b>writes</b> a scope onto a row and the side that <b>expands</b> a principal
///         into scopes have to agree on the exact string, and nothing in the type system says they do —
///         a mismatch is not an error anywhere, it is a row nobody can see.
///     </para>
///     <para>
///         It lives in Abstractions because its two callers are in different packages:
///         <c>DefaultUserScopeResolver</c> in <c>Pragmatic.Authorization</c> expands the principal, and
///         <c>ScopeInterceptor</c> in <c>Pragmatic.Persistence.EFCore</c> stamps the row.
///     </para>
/// </remarks>
public static class ScopeIdentifiers
{
    /// <summary>The scope that names one user: <c>user:{id}</c>.</summary>
    /// <param name="userId">The principal's identifier.</param>
    public static string ForUser(string userId) => $"user:{userId}";

    /// <summary>The scope that names one role: <c>role:{name}</c>.</summary>
    /// <param name="roleName">The role's name.</param>
    public static string ForRole(string roleName) => $"role:{roleName}";

    /// <summary>The scope that names an explicit data scope: <c>scope:{name}</c>.</summary>
    /// <param name="scopeName">The data scope's name, as a <c>data-scope</c> claim carries it.</param>
    public static string ForScope(string scopeName) => $"scope:{scopeName}";
}
