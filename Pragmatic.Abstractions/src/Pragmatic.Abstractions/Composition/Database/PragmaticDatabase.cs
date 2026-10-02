namespace Pragmatic.Composition.Database;

/// <summary>
///     Base class for declaring a physical database used in the application.
///     Derive from this class and decorate with <see cref="Attributes.PragmaticDatabaseAttribute" />
///     to register it in the composition graph.
/// </summary>
/// <example>
///     <code>
/// [PragmaticDatabase(Provider = DatabaseProvider.SqlServer, ConfigKey = "ConnectionStrings:App")]
/// public sealed class AppDatabase : PragmaticDatabase
/// {
///     public override string? ConfigKey => "ConnectionStrings:App";
/// }
/// </code>
/// </example>
public abstract class PragmaticDatabase
{
    /// <summary>
    ///     Gets the configuration key for the connection string.
    ///     Override in derived classes to provide the key without reflection.
    /// </summary>
    public virtual string? ConfigKey => null;

    /// <summary>
    ///     Gets the configuration key for the migration connection string.
    ///     Falls back to <see cref="ConfigKey"/> if not overridden.
    /// </summary>
    public virtual string? MigrationConfigKey => ConfigKey;

    /// <summary>
    ///     Returns all configuration keys required by this database.
    ///     Used for startup validation and architectural documentation.
    /// </summary>
    /// <returns>
    ///     The distinct configuration key(s) required by this database — <see cref="ConfigKey"/>
    ///     and, when it resolves to a different value, <see cref="MigrationConfigKey"/>.
    /// </returns>
    public virtual IEnumerable<string> GetRequiredConfigKeys()
    {
        if (ConfigKey is { Length: > 0 } key)
            yield return key;
        if (MigrationConfigKey is { Length: > 0 } migrationKey && migrationKey != ConfigKey)
            yield return migrationKey;
    }
}
