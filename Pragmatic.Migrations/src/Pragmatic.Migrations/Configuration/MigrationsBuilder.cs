using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Configuration;

/// <summary>
///     Fluent builder for configuring Pragmatic Migrations services.
///     Providers are auto-detected — no need to call UsePostgreSql() etc.
/// </summary>
public sealed class MigrationsBuilder
{
    private readonly HashSet<string> _databaseFilter = new(StringComparer.OrdinalIgnoreCase);
    private string _auditTableName = MigrationConstants.AuditTableName;
    private bool _force;
    private bool _dryRun;
    private bool _concurrentIndexes;
    private bool _built;
    private bool _manageDeclaredTablesOnly;
    private readonly HashSet<string> _excludedTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, DbConnection>> _connectionFactories = new(StringComparer.OrdinalIgnoreCase);

    internal IServiceCollection Services { get; }

    public MigrationsBuilder(IServiceCollection services)
    {
        Services = services;

        // Core services (provider-agnostic)
        services.AddSingleton<ISchemaDiffEngine, SchemaDiffEngine>();
        services.AddSingleton<IMigrationRunner, MigrationRunner>();
        services.AddSingleton<MigrationProviderFactory>();
    }

    /// <summary>
    ///     Only migrate the specified database. Can be called multiple times.
    ///     The database type must match the type used in <c>[Include&lt;TModule, TDatabase&gt;]</c>.
    /// </summary>
    public MigrationsBuilder OnlyDatabase<TDatabase>()
    {
        _databaseFilter.Add(typeof(TDatabase).Name);
        return this;
    }

    /// <summary>
    ///     Sets a custom name for the migration audit table. Default: "__PragmaticSchema".
    /// </summary>
    public MigrationsBuilder UseAuditTable(string tableName)
    {
        _auditTableName = tableName;
        return this;
    }

    /// <summary>
    ///     Allows breaking changes (DROP TABLE, DROP COLUMN, SET NOT NULL, narrowing type
    ///     changes) to be applied. By default breaking changes are blocked: the migration
    ///     fails and — at host startup — the host aborts rather than applying data loss.
    /// </summary>
    public MigrationsBuilder Force()
    {
        _force = true;
        return this;
    }

    /// <summary>
    ///     Computes and reports the migration plan without executing any DDL.
    ///     Use to inspect the generated SQL before applying. Default: false.
    /// </summary>
    public MigrationsBuilder DryRun()
    {
        _dryRun = true;
        return this;
    }

    /// <summary>
    ///     Builds indexes outside the migration transaction so the table is not locked while
    ///     the index is built — PostgreSQL <c>CREATE INDEX CONCURRENTLY</c>, SQL Server
    ///     <c>WITH (ONLINE = ON)</c>, SQLite no-op. A concurrent build that fails leaves the
    ///     index INVALID on the server (the schema change is already committed); the failure
    ///     is reported on the <see cref="MigrationResult" />.
    /// </summary>
    public MigrationsBuilder UseConcurrentIndexes()
    {
        _concurrentIndexes = true;
        return this;
    }

    /// <summary>
    ///     Registers an <see cref="IDataMigration" />. Data migrations run once per database —
    ///     tracked by name in __PragmaticDataMigrations — after the schema changes are applied.
    /// </summary>
    public MigrationsBuilder AddDataMigration<TDataMigration>()
        where TDataMigration : class, IDataMigration
    {
        Services.AddSingleton<IDataMigration, TDataMigration>();
        return this;
    }

    /// <summary>
    ///     Excludes one or more tables from migration management. Excluded tables are skipped
    ///     by introspection, so the diff engine never proposes to alter or drop them. Use for
    ///     tables owned by another system, a DBA, or a database extension.
    /// </summary>
    public MigrationsBuilder ExcludeTable(params string[] tableNames)
    {
        foreach (var name in tableNames)
            _excludedTables.Add(name);
        return this;
    }

    /// <summary>
    ///     Stops the diff engine from ever proposing to DROP a table it does not know about.
    ///     <para>
    ///     By default every table found in the database but absent from the desired schema is a
    ///     <c>DROP TABLE</c> — a breaking change that blocks startup. On a database shared with
    ///     another application (or carrying legacy tables) that means naming every foreign table
    ///     with <see cref="ExcludeTable" /> one by one, and a table added later by the other system
    ///     silently starts blocking deploys. Turn this on and unknown tables are simply left alone:
    ///     this host manages the tables it declares, and nothing else.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     Renaming an entity then looks like "new table added, old table unknown", so the old table
    ///     survives and has to be dropped by hand. That is the trade-off for sharing a database.
    /// </remarks>
    public MigrationsBuilder ManageDeclaredTablesOnly()
    {
        _manageDeclaredTablesOnly = true;
        return this;
    }

    /// <summary>
    ///     Registers a connection factory for the given provider, removing the need for
    ///     runtime reflection. Call this from the host project that references the DB driver.
    ///     Example: <c>UseProvider(MigrationConstants.ProviderPostgreSql, cs => new NpgsqlConnection(cs))</c>
    /// </summary>
    public MigrationsBuilder UseProvider(string providerName, Func<string, DbConnection> factory)
    {
        _connectionFactories[providerName] = factory;
        return this;
    }

    /// <summary>
    ///     Registers the migration options and providers into DI. Called for you by
    ///     <c>UsePragmaticMigrations</c>; call it directly only when composing migrations without an
    ///     <c>IPragmaticBuilder</c> (the CLI and the test harness do this). Calling it twice is a
    ///     no-op — the second call would otherwise register a duplicate, conflicting options object.
    /// </summary>
    public void Build()
    {
        if (_built) return;
        _built = true;

        var options = new MigrationOptions
        {
            Force = _force,
            DryRun = _dryRun,
            ConcurrentIndexes = _concurrentIndexes,
            AuditTableName = _auditTableName,
            ManageDeclaredTablesOnly = _manageDeclaredTablesOnly,
            DatabaseFilter = _databaseFilter.Count > 0 ? _databaseFilter : null
        };
        Services.AddSingleton(options);

        // Build excluded tables: framework tables + user-declared exclusions (ExcludeTable).
        // LockTableName is the leader-election table; without it the runner sees its own lock
        // table on re-introspection and computes a spurious DROP TABLE breaking change.
        // Both the configured audit name AND the default are excluded: a host that switched to a
        // custom name still has the original __PragmaticSchema on disk, and introspecting it would
        // produce exactly that spurious DROP TABLE.
        var excluded = new List<string>
        {
            _auditTableName,
            MigrationConstants.AuditTableName,
            MigrationConstants.EfMigrationsTableName,
            MigrationConstants.DataMigrationTableName,
            MigrationConstants.LockTableName
        };
        excluded.AddRange(_excludedTables);

        // Register a provider only when its driver is actually usable: either the host supplied an
        // explicit connection factory (UseProvider), or the driver's connection type resolves at
        // runtime. This avoids wiring up a provider whose NuGet package is absent: with all three
        // registered unconditionally, the default (last-wins) IConnectionFactory could resolve to a
        // provider whose driver is not loadable, surfacing only as a confusing failure deep inside
        // the runner.
        RegisterProviderIfAvailable(MigrationConstants.ProviderPostgreSql,
            new PostgreSqlMigrationGenerator(),
            new PostgreSqlSchemaIntrospector { ExcludedTables = excluded });

        RegisterProviderIfAvailable(MigrationConstants.ProviderSqlServer,
            new SqlServerMigrationGenerator(),
            new SqlServerSchemaIntrospector { ExcludedTables = excluded });

        RegisterProviderIfAvailable(MigrationConstants.ProviderSqlite,
            new SqliteMigrationGenerator(),
            new SqliteSchemaIntrospector { ExcludedTables = excluded });

        // Say it here rather than let DI fail later on a missing IConnectionFactory. The probe this
        // replaced could not tell "no driver referenced" from "generator never ran"; naming both is
        // the difference between a clear message at startup and a confusing one deep in the runner.
        if (_registeredProviders.Count == 0)
            throw new InvalidOperationException(
                "Migrations found no usable database provider. Reference a driver package (Npgsql, "
                + "Microsoft.Data.SqlClient or Microsoft.Data.Sqlite) together with the Pragmatic source "
                + "generator, which registers its connection factory — or call UseProvider(...) to supply "
                + "one explicitly.");
    }

    private readonly HashSet<string> _registeredProviders = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterProviderIfAvailable(
        string providerName,
        ISqlMigrationGenerator generator,
        ISchemaIntrospector introspector)
    {
        // Explicit factory always wins.
        if (_connectionFactories.TryGetValue(providerName, out var explicitFactory))
        {
            RegisterProvider(providerName, generator, introspector, explicitFactory);
            return;
        }

        // Otherwise only register when the host actually references the driver, so a multi-target host
        // that pulls in just one gets a clean single-provider default.
        //
        // The generator answers that: it sees the driver on the compilation and emits a module
        // initializer with a typed `new NpgsqlConnection(cs)`. Asked at run time by this assembly,
        // which references no driver, the question would need Type.GetType on a name string plus
        // Activator.CreateInstance; asked at compile time by the host, which does, it needs neither.
        var factory = MigrationDriverRegistry.Find(providerName);
        if (factory is null)
            return;

        RegisterProvider(providerName, generator, introspector, factory);
    }

    private void RegisterProvider(
        string providerName,
        ISqlMigrationGenerator generator,
        ISchemaIntrospector introspector,
        Func<string, DbConnection> connectionFactory)
    {
        _registeredProviders.Add(providerName);

        Services.AddKeyedSingleton<ISqlMigrationGenerator>(providerName, generator);
        Services.AddKeyedSingleton<ISchemaIntrospector>(providerName, introspector);
        Services.AddKeyedSingleton<IConnectionFactory>(providerName,
            new GenericConnectionFactory(providerName, connectionFactory));

        // Default fallback (last-wins for single-provider hosts)
        Services.AddSingleton(generator);
        Services.AddSingleton(introspector);
        Services.AddSingleton<IConnectionFactory>(new GenericConnectionFactory(providerName, connectionFactory));
    }
}
