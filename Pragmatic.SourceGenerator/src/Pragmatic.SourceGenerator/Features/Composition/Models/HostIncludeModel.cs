// Pragmatic.SourceGenerator - Composition - Host Include Model

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Represents a single [Include&lt;T&gt;] declaration on the host's [Module] class.
///     Carries wiring information: which module, which database, which DbContext.
/// </summary>
internal sealed record HostIncludeModel
{
    /// <summary>Fully qualified module type name (TModule).</summary>
    public required string ModuleTypeName { get; init; }

    /// <summary>
    ///     The assembly that declares TModule, which is how this include is matched to the boundaries
    ///     it brings.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>The assembly, not the name.</b> Matching <c>"SplitModule"</c> → <c>"Split"</c> against a
    ///     discovered module whose name comes from its <b>boundary</b> works only while every module
    ///     has exactly one boundary named after it: a module with two boundaries has no name that could
    ///     match both, so it would be PRAG1608 and its DbContexts would never be registered — and a
    ///     single boundary named differently from its module would fail the same way. The assembly is
    ///     the fact; the names are a coincidence.
    /// </remarks>
    public string? ModuleAssemblyName { get; init; }

    /// <summary>
    ///     Fully qualified database type name (TDatabase).
    ///     Null when using 1-arity [Include&lt;TModule&gt;] without database.
    /// </summary>
    public string? DatabaseTypeName { get; init; }

    /// <summary>
    ///     Database provider enum value name (e.g., "SqlServer", "PostgreSql").
    ///     Read from [PragmaticDatabase(Provider = DatabaseProvider.SqlServer)] on TDatabase.
    /// </summary>
    public string? DatabaseProvider { get; init; }

    /// <summary>
    ///     Connection string configuration key for queries (e.g., "ConnectionStrings:App").
    ///     Read from [PragmaticDatabase(ConfigKey = "...")] on TDatabase.
    /// </summary>
    public string? DatabaseConfigKey { get; init; }

    /// <summary>
    ///     Connection string configuration key for migrations (DDL).
    ///     Falls back to <see cref="DatabaseConfigKey"/> when null.
    ///     Read from [PragmaticDatabase(MigrationConfigKey = "...")] on TDatabase.
    /// </summary>
    public string? MigrationConfigKey { get; init; }

    /// <summary>
    ///     Fully qualified DbContext type name (TDbContext).
    ///     Null when using 1- or 2-arity Include (DbContext is auto-named by Persistence SG).
    /// </summary>
    public string? DbContextTypeName { get; init; }

    /// <summary>
    ///     Simple class name of the DbContext (e.g., "CatalogDbContext").
    ///     Derived from TDbContext when provided via 3-arity [Include&lt;T,D,C&gt;].
    /// </summary>
    public string? DbContextClassName { get; init; }

    /// <summary>Source location for diagnostic reporting.</summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>Whether this include has an explicit database assignment.</summary>
    public bool HasDatabase => DatabaseTypeName is not null;

    /// <summary>Whether this include has an explicit DbContext type.</summary>
    public bool HasExplicitDbContext => DbContextTypeName is not null;

    /// <summary>
    ///     Fully qualified name of the MigrationDbContext for this database group.
    ///     Derived by convention in HostModeGenerator (e.g., <c>global::Showcase.ShowcaseAppDatabaseMigrationDbContext</c>).
    ///     Null when no entity is behind the database: the persistence generator then writes neither this
    ///     context nor the database's schema, and every consumer skips the database on it.
    /// </summary>
    public string? MigrationDbContextFqn { get; init; }
}
