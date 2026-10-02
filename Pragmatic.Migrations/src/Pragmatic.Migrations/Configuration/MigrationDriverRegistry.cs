using System.Collections.Concurrent;
using System.Data.Common;

namespace Pragmatic.Migrations.Configuration;

/// <summary>
///     The database drivers this application can actually open a connection with.
/// </summary>
/// <remarks>
///     <para>
///         Filled by a module initializer the generator emits: it sees which driver packages the host
///         references and writes <c>new NpgsqlConnection(cs)</c> — typed, compiled, no lookup.
///     </para>
///     <para>
///         <c>MigrationsBuilder</c> cannot answer the same question without reflection
///         (<c>Type.GetType</c> on a name string plus <c>Activator.CreateInstance</c>):
///         <c>Pragmatic.Migrations</c> references no driver, so it cannot name one. The generator runs
///         in the host, which does — a case of the rule that generated code may only name what its
///         consumer already references.
///     </para>
/// </remarks>
public static class MigrationDriverRegistry
{
    private static readonly ConcurrentDictionary<string, Func<string, DbConnection>> Factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Registers the connection factory for a provider. Called by generated code.
    /// </summary>
    /// <param name="providerName">The provider key, e.g. "postgresql".</param>
    /// <param name="factory">Opens a connection from a connection string.</param>
    public static void Register(string providerName, Func<string, DbConnection> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerName);
        ArgumentNullException.ThrowIfNull(factory);
        Factories[providerName] = factory;
    }

    /// <summary>
    ///     Gets the factory for a provider, or null when the host does not reference its driver.
    /// </summary>
    /// <param name="providerName">The provider key.</param>
    /// <returns>The factory, or null.</returns>
    public static Func<string, DbConnection>? Find(string providerName)
        => providerName is not null && Factories.TryGetValue(providerName, out var factory) ? factory : null;

    /// <summary>The providers registered so far.</summary>
    public static IReadOnlyCollection<string> Providers => Factories.Keys.ToArray();
}
