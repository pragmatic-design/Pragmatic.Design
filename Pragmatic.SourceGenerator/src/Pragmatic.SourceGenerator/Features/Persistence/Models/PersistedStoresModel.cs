using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The stores the persistence generator writes into a host: which <c>Add{Boundary}DbContext</c>
///     registrations exist, and which databases get a <c>{Db}Schema</c> and a
///     <c>{Db}MigrationDbContext</c>.
/// </summary>
/// <remarks>
///     <para>
///         Composition writes the calls to all three and cannot see this generator's output, so the facts
///         travel through the pipeline. Assuming them from the <c>[Include&lt;TModule, TDatabase&gt;]</c>
///         alone would be wrong for a module with no entities yet, which is the first state of every
///         application: the host would name types nobody wrote.
///     </para>
///     <para>
///         Each set is the condition its emitter applies, restated: <c>GenerateDbContextRegistration</c>
///         writes one registration per boundary with a valid entity; <c>GenerateMigrationDbContexts</c>
///         and <c>GenerateSchemaMetadata</c> write one per database a valid entity's boundary is
///         assigned to. A change to either condition has to change <see cref="From" /> with it.
///     </para>
/// </remarks>
internal sealed record PersistedStoresModel
{
    /// <summary>A host with no persistence generator behind it: nothing to call.</summary>
    public static PersistedStoresModel None { get; } = new();

    /// <summary>Names of the generated registrations, e.g. <c>AddLeaveDbContext</c>.</summary>
    public EquatableArray<string> DbContextRegistrations { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The class the registrations are declared in, fully qualified —
    ///     <c>global::TimeOff.DbContextRegistrationExtensions</c>. The host calls them through it: its own
    ///     generated code lives in a namespace named after its assembly, which reaches this class only
    ///     when the assembly happens to be named under the modules' root.
    /// </summary>
    public string RegistrationClass { get; init; } = "";

    /// <summary>
    ///     Database types with at least one entity behind them, fully qualified without
    ///     <c>global::</c>.
    /// </summary>
    public EquatableArray<string> Databases { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Databases holding an <c>[Audited]</c> entity: the boundary context maps the audit trail's tables
    ///     there (<c>BoundaryDbContextModel.HasAuditedEntities</c>), so its migration creates them.
    /// </summary>
    public EquatableArray<string> AuditedDatabases { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Databases holding a <c>[DataSubject]</c>: with <c>Pragmatic.Privacy.EFCore</c> referenced the
    ///     boundary context maps the subject registry's tables there (<c>HasPrivacyRegistry</c>).
    /// </summary>
    public EquatableArray<string> SubjectDatabases { get; init; } = EquatableArray<string>.Empty;

    public bool HasDbContextRegistration(string? methodName)
        => methodName is not null && DbContextRegistrations.AsImmutableArray().Contains(methodName);

    public bool HasDatabase(string? databaseTypeName)
        => databaseTypeName is not null
           && Databases.AsImmutableArray().Contains(StripGlobal(databaseTypeName));

    /// <summary>
    ///     Databases with a <c>{Db}Schema</c>, by class name, with the namespace it was written in: the ones the host migrates with
    ///     Pragmatic.Migrations. A superset of <see cref="Databases" /> wherever another engine than EF Core persists a boundary, since its
    ///     database has a schema and no EF Core migration context.
    /// </summary>
    public EquatableArray<SchemaDatabaseModel> SchemaDatabases { get; init; } = EquatableArray<SchemaDatabaseModel>.Empty;

    /// <summary>
    ///     The namespace the EF Core migration contexts (<c>{Db}MigrationDbContext</c>) are declared in: the host names them by it, with or
    ///     without a schema beside them (none without Pragmatic.Migrations).
    /// </summary>
    public string MigrationContextNamespace { get; init; } = "";

    /// <summary>The EF Core migration context of <paramref name="databaseSimpleName" />, fully qualified.</summary>
    public string MigrationDbContextOf(string databaseSimpleName)
        => string.IsNullOrEmpty(MigrationContextNamespace)
            ? $"global::{databaseSimpleName}MigrationDbContext"
            : $"global::{MigrationContextNamespace}.{databaseSimpleName}MigrationDbContext";

    /// <summary>The schema written for <paramref name="databaseTypeName" />, or null when none was.</summary>
    public SchemaDatabaseModel? SchemaOf(string? databaseTypeName)
    {
        if (databaseTypeName is null)
            return null;
        var name = StripGlobal(databaseTypeName);
        var simple = name.Substring(name.LastIndexOf('.') + 1);
        return SchemaDatabases.AsImmutableArray().FirstOrDefault(s => s.DatabaseName == simple);
    }

    public static PersistedStoresModel From(
        ImmutableArray<EntityMetadataModel> entities,
        DatabaseTopologyInfo topology,
        string registrationClass)
    {
        var valid = entities.IsDefaultOrEmpty
            ? ImmutableArray<EntityMetadataModel>.Empty
            : entities.Where(e => e.IsValid).ToImmutableArray();

        var registrations = valid
            .Where(e => !string.IsNullOrEmpty(e.BoundaryName))
            .Select(e => $"Add{e.BoundaryName}DbContext")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToImmutableArray();

        var databaseOfBoundary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assignment in topology.BoundaryToDatabase)
            databaseOfBoundary[StripGlobal(assignment.Key)] = StripGlobal(assignment.Value.DatabaseTypeName);

        ImmutableArray<string> DatabasesOf(IEnumerable<EntityMetadataModel> holders)
            => holders
                .Select(e => databaseOfBoundary.TryGetValue(StripGlobal(e.BoundaryTypeFullName ?? ""), out var db)
                    ? db
                    : null)
                .Where(db => db is not null)
                .Select(db => db!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(db => db, StringComparer.Ordinal)
                .ToImmutableArray();

        return new PersistedStoresModel
        {
            DbContextRegistrations = registrations,
            RegistrationClass = registrationClass,
            Databases = DatabasesOf(valid),
            AuditedDatabases = DatabasesOf(valid.Where(e => e.IsAudited)),
            SubjectDatabases = DatabasesOf(valid.Where(e => e.IsDataSubject))
        };
    }

    public bool IsAuditedDatabase(string? databaseTypeName)
        => databaseTypeName is not null
           && AuditedDatabases.AsImmutableArray().Contains(StripGlobal(databaseTypeName));

    public bool IsSubjectDatabase(string? databaseTypeName)
        => databaseTypeName is not null
           && SubjectDatabases.AsImmutableArray().Contains(StripGlobal(databaseTypeName));

    private static string StripGlobal(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;
}
