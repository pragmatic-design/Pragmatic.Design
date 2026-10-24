namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Who is asking, for the current asynchronous flow: the tenant, the user, the access scopes and the permissions a request carries.
///     <see cref="Use" /> sets it for a scope (a request, a job's unit of work), as <see cref="SqlProtection" /> does a protector. A visibility
///     rule reads it (<c>d =&gt; d.TenantId == SqlContext.Current.TenantId</c>): the value is a parameter of the statement, read again at each
///     execution, so one plan serves every tenant and user. Outside a scope it is <see cref="Nobody" />: no tenant, no user, no scope, so a rule
///     on them matches nothing (fail closed).
/// </summary>
public sealed class SqlContext(string? tenantId = null, string? userId = null, IReadOnlyList<string>? scopes = null, IReadOnlyCollection<string>? permissions = null)
{
    private static readonly AsyncLocal<SqlContext?> Ambient = new();
    private readonly HashSet<string> _permissions = permissions is null ? [] : new HashSet<string>(permissions, StringComparer.Ordinal);

    /// <summary>The context outside any scope: every member empty.</summary>
    public static SqlContext Nobody { get; } = new();

    /// <summary>The context of the current flow; <see cref="Nobody" /> outside a scope.</summary>
    public static SqlContext Current => Ambient.Value ?? Nobody;

    public string? TenantId { get; } = tenantId;

    public string? UserId { get; } = userId;

    /// <summary>The access scopes the user holds (a rule compares a row's scopes with them).</summary>
    public IReadOnlyList<string> Scopes { get; } = scopes ?? [];

    /// <summary>True when the user holds the permission (a rule's bypass: <c>SqlContext.Current.Has("bookings.view-all") || …</c>).</summary>
    public bool Has(string permission) => _permissions.Contains(permission);

    /// <summary>Puts <paramref name="context" /> in effect until the scope is disposed; the one before comes back then (scopes nest).</summary>
    public static IDisposable Use(SqlContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = new Scope(Ambient.Value);
        Ambient.Value = context;
        return scope;
    }

    private sealed class Scope(SqlContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
