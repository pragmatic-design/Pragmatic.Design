using Pragmatic.Composition.Enums;

namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a class derived from <see cref="Database.PragmaticDatabase" /> with its provider
///     and configuration key.
/// </summary>
/// <remarks>
///     <b>Where this is consumed.</b> Two readers, for two different jobs.
///     <c>ModuleTransform</c> uses it to place a module on a database while building the topology,
///     and <c>DatabaseTopologyReader</c> reads <c>ConfigKey</c> off the database class to resolve the
///     connection string without reflection. The topology they produce is what decides how many
///     <c>DbContext</c> types the Persistence feature emits.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PragmaticDatabaseAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the database provider.
    /// </summary>
    public DatabaseProvider Provider { get; set; }

    /// <summary>
    ///     Gets or sets the configuration key for the connection string.
    ///     Used for queries and general database access.
    ///     Example: "ConnectionStrings:App"
    ///     <para>
    ///         <b>Required.</b> Leaving this at the default empty string will cause a runtime
    ///         failure when the host resolves the connection string — the host will look up an
    ///         empty key and receive <c>null</c>, resulting in a misleading startup exception.
    ///         Always supply a non-empty key via the attribute or the <c>GetRequiredConfigKeys()</c>
    ///         override on <c>PragmaticDatabase</c>.
    ///     </para>
    /// </summary>
    public string ConfigKey { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the configuration key for the migration connection string.
    ///     If not set, falls back to <see cref="ConfigKey"/>.
    ///     Use this to separate DDL (migration) access from DML (query) access.
    ///     Example: "ConnectionStrings:App:Migration"
    /// </summary>
    public string? MigrationConfigKey { get; set; }
}
