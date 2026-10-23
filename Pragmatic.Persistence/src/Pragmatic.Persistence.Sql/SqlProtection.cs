namespace Pragmatic.Persistence.Sql;

/// <summary>Turns a value into what the column stores and back: an encryption, with whatever key the application chose (per tenant, rotated).</summary>
public interface ISqlProtector
{
    string Protect(string value);

    string Unprotect(string stored);
}

/// <summary>
///     The protector in effect for the current asynchronous flow: <see cref="Use" /> sets it for a scope (a request, a tenant's unit of work),
///     and every read and write inside the scope, awaited or not, protects and unprotects through it. The converters are static functions
///     (no session reaches them), so the state they need is ambient, as a culture is.
/// </summary>
public static class SqlProtection
{
    private static readonly AsyncLocal<ISqlProtector?> Ambient = new();

    /// <summary>The protector of the current flow; <see cref="InvalidOperationException" /> outside a scope, so a value is never stored in clear by mistake.</summary>
    public static ISqlProtector Current => Ambient.Value ?? throw new InvalidOperationException("No SqlProtector is in effect: wrap the reads and writes of protected columns in SqlProtection.Use(protector).");

    /// <summary>Puts <paramref name="protector" /> in effect until the scope is disposed; the one before comes back then (scopes nest).</summary>
    public static IDisposable Use(ISqlProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        var scope = new Scope(Ambient.Value);
        Ambient.Value = protector;
        return scope;
    }

    private sealed class Scope(ISqlProtector? previous) : IDisposable
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

/// <summary>
///     A text column stored protected (<c>[Convert&lt;SqlProtected&gt;]</c>): written through <see cref="SqlProtection.Current" />'s Protect, read
///     through its Unprotect; a predicate's value is protected the same way, so an equality matches only with a deterministic protector (the same
///     value, the same stored text). Ordering, LIKE and ranges read the stored text, not the value.
/// </summary>
public sealed class SqlProtected : ISqlConverter<string, string>
{
    public static string ToStore(string value) => SqlProtection.Current.Protect(value);

    public static string FromStore(string value) => SqlProtection.Current.Unprotect(value);
}
