using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using EngineSchemaModel = Pragmatic.SourceGenerator.Features.Persistence.Models.EngineSchemaModel;
using MigratedTableModel = Pragmatic.SourceGenerator.Features.Persistence.Models.TableSchemaModel;
using ShapeSourceModel = Pragmatic.SourceGenerator.Features.Persistence.Models.ShapeSourceModel;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The tables of Pragmatic's <c>[Entity]</c> types the Sql engine serves, for Pragmatic.Migrations: per database of the host's topology
///     (<c>[Include&lt;TModule, TDatabase&gt;]</c>, <c>Default</c> without one), typed for that database's provider, and handed to
///     <c>SchemaFeature</c>, which writes them in the same <c>{Database}Schema</c> as the EF Core entities of the database. In the shape
///     Pragmatic's EF branch gives a table (<c>SchemaMetadataTransform</c>); where they differ, each difference is listed in
///     <c>TheSqlEnginesSchemaIsPragmaticsTests</c>.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private const string SchemaVersionType = "Pragmatic.Migrations.Schema.SchemaVersion";

    /// <summary>
    ///     The schema of each database the host's Sql-served entities live in; empty outside a host, without Pragmatic.Migrations, or without a
    ///     provider to type the tables for. An owned type's members are columns of its owner's table, <c>{Property}_{Member}</c>, as on EF Core.
    /// </summary>
    internal static EquatableArray<EngineSchemaModel> PragmaticSchemas(Compilation compilation, ImmutableArray<INamedTypeSymbol> entityTypes,
        EquatableArray<ShapeSourceModel> shapeSources, DetectedFeatures features, CancellationToken ct)
    {
        if (!UsesSql(compilation) || !features.IsHostMode || compilation.GetTypeByMetadataName(SchemaVersionType) is null)
            return EquatableArray<EngineSchemaModel>.Empty;
        if (SchemaDialect(compilation, features) is not { } dialect)
            return EquatableArray<EngineSchemaModel>.Empty;

        // The members the shape gives this compilation's entities (the key, the stamps): the columns read them. A referenced assembly's
        // entities have theirs compiled in.
        if (!shapeSources.IsDefaultOrEmpty)
        {
            var options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
            compilation = compilation.AddSyntaxTrees(shapeSources.Select(s => CSharpSyntaxTree.ParseText(s.Text, options, s.HintName, cancellationToken: ct)));
            entityTypes = Rebound(entityTypes, compilation);
        }

        var topology = DatabaseTopologyReader.Read(compilation, ct);
        var databases = new Dictionary<string, string>(StringComparer.Ordinal); // boundary, without global:: → database
        var configKeys = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var assignment in topology.BoundaryToDatabase)
        {
            var boundary = assignment.Key.StartsWith("global::", StringComparison.Ordinal) ? assignment.Key.Substring("global::".Length) : assignment.Key;
            databases[boundary] = assignment.Value.DatabaseClassName;
            configKeys[assignment.Value.DatabaseClassName] = assignment.Value.ConfigKey;
        }

        string? DatabaseOf(INamedTypeSymbol entity)
        {
            if (!topology.HasTopology)
                return "Default";
            return PragmaticPermissions.BoundaryOf(entity) is { } boundary && databases.TryGetValue(boundary.ToDisplayString(), out var database) ? database : null;
        }

        var engines = PragmaticEngines.Of(compilation);
        var served = SchemaCandidates(compilation, topology.HasTopology)
            .Where(t => PragmaticShape.IsPragmaticEntity(t) && engines.Serves(t, PragmaticEngine.Sql) && DatabaseOf(t) is not null)
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        if (served.Count == 0)
            return EquatableArray<EngineSchemaModel>.Empty;

        var diagnostics = new List<Diagnostic>(); // the entities' problems are Emit's to report, at their declarations
        var models = new Dictionary<INamedTypeSymbol, EntityModel?>(SymbolEqualityComparer.Default);
        EntityModel? Lookup(INamedTypeSymbol type)
        {
            if (!models.TryGetValue(type, out var model))
                models[type] = model = EntityModel.IsEntity(type) ? EntityModel.Read(type, diagnostics, entityTypes, compilation) : null;
            return model;
        }

        // A module's table (an outbox's row, declared by its engines package) in the database of each boundary the engine serves that enables
        // the module (read off the boundary, not its entities: the boundary owns none of them when the assembly declares several and none claims
        // them), with the indexes EF Core's configuration gives it.
        var moduleTables = new List<(EntityModel Row, HashSet<string> Databases, Func<TableSchemaModel, TableSchemaModel> Indexes)>();
        void ModuleTable(string rowType, Func<INamedTypeSymbol, bool> enables, Func<TableSchemaModel, TableSchemaModel> indexes)
        {
            var rowDatabases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var boundary in SchemaCandidates(compilation, topology.HasTopology).Where(t => enables(t) && engines.ServesBoundary(t, PragmaticEngine.Sql)))
            {
                if (!topology.HasTopology)
                    rowDatabases.Add("Default");
                else if (databases.TryGetValue(boundary.ToDisplayString(), out var database))
                    rowDatabases.Add(database);
            }

            ModuleTableIn(rowType, rowDatabases, indexes);
        }

        void ModuleTableIn(string rowType, HashSet<string> rowDatabases, Func<TableSchemaModel, TableSchemaModel> indexes)
        {
            if (compilation.GetTypeByMetadataName(rowType) is { } rowSymbol && Lookup(rowSymbol) is { } row)
                moduleTables.Add((row, rowDatabases, indexes));
        }

        ModuleTable(EventOutboxRowType, PragmaticRegistration.EnablesEventOutbox, WithClaimIndex); // [EnableEventOutbox]
        ModuleTable(MessagingOutboxRowType, PragmaticRegistration.EnablesMessagingOutbox, WithMessagingOutboxIndexes); // [EnableOutbox]

        // [Audited]: the trail's three tables in each database that holds an audited entity, as EF Core's schema puts them there.
        var auditedDatabases = new HashSet<string>(served.Where(PragmaticRegistration.IsAudited).Select(t => DatabaseOf(t)!), StringComparer.Ordinal);
        ModuleTableIn(AuditEntryRowType, auditedDatabases, WithAuditEntryIndexes);
        ModuleTableIn(AuditSegmentRowType, auditedDatabases, WithAuditSegmentIndexes);
        ModuleTableIn(AuditPrunedRangeRowType, auditedDatabases, static table => table);

        // [DataSubject]: the subject registry's two tables in each database that holds a data subject, as EF Core's schema puts them there.
        var subjectDatabases = new HashSet<string>(served.Where(PragmaticRegistration.IsDataSubject).Select(t => DatabaseOf(t)!), StringComparer.Ordinal);
        ModuleTableIn(SubjectRowType, subjectDatabases, table => WithSubjectIndexes(table, dialect));
        ModuleTableIn(ConsentRowType, subjectDatabases, WithConsentIndexes);
        TableSchemaModel ModuleIndexes(TableSchemaModel table)
            => moduleTables.FirstOrDefault(m => m.Row.Table == table.Name) is { Row: not null } module ? module.Indexes(table) : table;

        var schemas = new List<EngineSchemaModel>();
        foreach (var database in served.GroupBy(t => DatabaseOf(t)!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var entities = database.Select(Lookup).Where(e => e is not null).Select(e => e!).ToList();
            if (entities.Count == 0)
                continue;
            entities.AddRange(moduleTables.Where(m => m.Databases.Contains(database.Key)).Select(m => m.Row));
            // The namespace Pragmatic's EF branch derives from the database's entities: one of the application's, the first by name.
            var representative = database.FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly)) ?? database.First();
            var ns = RootNamespace(representative.ContainingNamespace.IsGlobalNamespace ? compilation.AssemblyName ?? "" : representative.ContainingNamespace.ToDisplayString());
            schemas.Add(new EngineSchemaModel(database.Key, ns, ProviderName(dialect), configKeys.TryGetValue(database.Key, out var key) ? key : null,
                PragmaticTables(entities, Lookup, dialect).Select(t => SchemaTableOf(ModuleIndexes(t))).ToEquatableArray()));
        }

        return schemas.ToEquatableArray();
    }


    /// <summary>
    ///     The types a Sql-served entity of the schema can be: this compilation's, and with a topology the referenced assemblies' that see
    ///     Pragmatic.Persistence too, since a host's boundaries are libraries it references and its <c>[Include]</c> says which of them it
    ///     composes. Without a topology the database is the compilation's own entities', as it was before there was one.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> SchemaCandidates(Compilation compilation, bool hasTopology)
    {
        foreach (var type in PragmaticNames.AllTypes(compilation.Assembly.GlobalNamespace))
            yield return type;
        if (!hasTopology)
            yield break;
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assembly.Modules.Any(m => m.ReferencedAssemblies.Any(r => r.Name == "Pragmatic.Persistence")))
                continue;
            foreach (var type in PragmaticNames.AllTypes(assembly.GlobalNamespace))
                yield return type;
        }
    }

    /// <summary>
    ///     The dialect the tables are typed for: EF Core's provider when EF Core is referenced (the database's other tables are typed for it),
    ///     else the driver the compilation references (PostgreSQL, then SQL Server, then SQLite, the order EF's provider is detected in, then
    ///     MySQL and Oracle); null with neither.
    /// </summary>
    /// <remarks>ASSUMPTION: one provider per compilation, as EF Core's branch has it; a topology of databases on two providers is not described.</remarks>
    private static SqlDialect? SchemaDialect(Compilation compilation, DetectedFeatures features)
    {
        if (features.HasPersistenceEFCore && features.EfCoreProvider switch
            {
                EfCoreProvider.PostgreSql => SqlDialect.PostgreSql, EfCoreProvider.SqlServer => SqlDialect.SqlServer, EfCoreProvider.Sqlite => SqlDialect.Sqlite, _ => (SqlDialect?)null,
            } is { } efCore)
            return efCore;
        return compilation.GetTypeByMetadataName("Npgsql.NpgsqlConnection") is not null ? SqlDialect.PostgreSql
            : compilation.GetTypeByMetadataName("Microsoft.Data.SqlClient.SqlConnection") is not null ? SqlDialect.SqlServer
            : compilation.GetTypeByMetadataName("Microsoft.Data.Sqlite.SqliteConnection") is not null ? SqlDialect.Sqlite
            : compilation.GetTypeByMetadataName("MySqlConnector.MySqlConnection") is not null ? SqlDialect.MySql
            : compilation.GetTypeByMetadataName("Oracle.ManagedDataAccess.Client.OracleConnection") is not null ? SqlDialect.Oracle
            : null;
    }

    /// <summary>A table of the engine's model in the shape the schema template writes.</summary>
    private static MigratedTableModel SchemaTableOf(TableSchemaModel table) => new(
        table.Name,
        table.Schema,
        null,
        table.Columns.Select(c => new Persistence.Models.ColumnSchemaModel(c.Name, c.Type, c.Nullable, c.Key, c.DefaultValue)).ToEquatableArray(),
        table.Indexes.Select(i => new Persistence.Models.IndexSchemaModel(i.Name, i.Columns, i.Unique, i.Filter)).ToEquatableArray(),
        table.ForeignKeys.Select(f => new Persistence.Models.ForeignKeySchemaModel(f.Name, f.Column, f.Table, f.ReferencedColumn, f.OnDelete)).ToEquatableArray(),
        table.Checks.IsDefaultOrEmpty ? default : table.Checks.Select(c => new Persistence.Models.CheckConstraintSchemaModel(c.Name, c.Expression)).ToEquatableArray());

    /// <summary>The provider name Pragmatic's template writes (and Pragmatic.Migrations' <c>MigrationConstants</c> holds).</summary>
    private static string ProviderName(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => "PostgreSql", SqlDialect.SqlServer => "SqlServer", SqlDialect.MySql => "MySql", SqlDialect.Oracle => "Oracle", _ => "Sqlite",
    };

    /// <summary>Pragmatic's <c>DeriveRootNamespace</c>: without a trailing <c>.Entities</c>, then without the last segment when there are two or more.</summary>
    private static string RootNamespace(string ns)
    {
        if (ns.EndsWith(".Entities", StringComparison.Ordinal))
            ns = ns.Substring(0, ns.Length - ".Entities".Length);
        var parts = ns.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts.Take(parts.Length - 1)) : ns;
    }

    /// <summary>The tables of <paramref name="entities" /> on <paramref name="dialect" />, in table order, the join tables with them.</summary>
    private static List<TableSchemaModel> PragmaticTables(List<EntityModel> entities, Func<INamedTypeSymbol, EntityModel?> lookup, SqlDialect dialect)
    {
        var schema = dialect switch { SqlDialect.PostgreSql => "public", SqlDialect.SqlServer => "dbo", _ => null };
        // T-SQL has no boolean literal (EF Core's branch writes 0 there too); MySQL's bool is tinyint(1), Oracle's BOOLEAN reads FALSE back.
        var falsy = dialect switch { SqlDialect.SqlServer or SqlDialect.MySql => "0", SqlDialect.Oracle => "FALSE", _ => "false" };
        var live = $"\"IsDeleted\" = {falsy}";
        var tables = new SortedDictionary<string, TableSchemaModel>(StringComparer.Ordinal);
        bool Holds(INamedTypeSymbol type) => entities.Exists(e => SymbolEqualityComparer.Default.Equals(e.Type, type));

        foreach (var entity in entities)
        {
            var softDelete = PragmaticShape.Has(entity.Type, "SoftDeleteAttribute");
            var tenant = entity.Type.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity");
            var indexes = new List<(string Name, string[] Columns, bool Unique, string? Filter)>();
            void Index(string[] columns, bool unique = false, string? filter = null)
            {
                var name = $"IX_{entity.Table}_{string.Join("_", columns)}";
                if (!indexes.Exists(i => i.Name == name))
                    indexes.Add((name, columns, unique, filter));
            }

            // The domain key's unique index, read by the rule EF Core's branch reads it with (its parts' order, the tenant leading unless a
            // part is global): two readings of one key would be two indexes.
            var logicKey = EntityTransform.CollectLogicKeys(entity.Type);
            if (logicKey.Count > 0)
            {
                var names = logicKey.Select(p => entity.Column(p.Name)?.Column ?? p.Name).ToList();
                Index(tenant && !logicKey.Any(p => p.IsGlobal) ? names.Prepend("TenantId").ToArray() : names.ToArray(), true, softDelete ? live : null);
            }

            foreach (var unique in entity.Type.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Entity.UniqueAttribute"))
            {
                var names = unique.ConstructorArguments.SelectMany(a => a.Kind == TypedConstantKind.Array ? a.Values.Select(v => v.Value as string) : [a.Value as string]).Where(n => n is not null).Select(n => entity.Column(n!)?.Column ?? n!).ToList();
                var global = unique.NamedArguments.Any(n => n.Key == "Scope" && n.Value.Value is { } scope && n.Value.Type is INamedTypeSymbol kind && kind.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => Equals(f.ConstantValue, scope))?.Name == "Global");
                Index(tenant && !global ? names.Prepend("TenantId").ToArray() : names.ToArray(), true, softDelete ? live : null);
            }

            if (softDelete)
                Index(["IsDeleted"]);
            if (PragmaticShape.Has(entity.Type, "HasOwnerAttribute"))
                Index(["OwnerId"]);
            if (tenant)
                Index(["TenantId"]);
            var foreignKeys = new List<ForeignKeySchemaModel>();
            foreach (var navigation in entity.Navigations.Where(n => !n.IsComposite && entity.Column(n.ForeignKey) is not null))
            {
                var column = entity.Column(navigation.ForeignKey)!.Column;
                Index([column]);
                if (!Holds(navigation.Target) || lookup(navigation.Target) is not { } target)
                    continue; // another engine's, boundary's or database's row: the key without the constraint, as Pragmatic skips a cross-boundary one
                foreignKeys.Add(ForeignKey($"FK_{entity.Table}_{column}_{target.Table}", column, target.Table, target.KeyColumn, PragmaticRelations.OnDelete(entity.Type, navigation.Property, navigation.Target)));
            }

            var keyed = new HashSet<string>(indexes.SelectMany(i => i.Columns).Concat(entity.Keys.Select(k => k.Column)), StringComparer.Ordinal);
            var columns = new List<ColumnSchemaModel>();
            foreach (var column in entity.Columns)
            {
                if (column.IsRowVersion)
                {
                    // PostgreSQL's row version is xmin, no column (the owner's decision of 2026-10-22, not Pragmatic's shadow property). SQL Server's
                    // is written timestamp, the name its catalog reads back (rowversion is the DDL's synonym, which the introspector would diff forever).
                    // Anywhere else the writes advance a plain column, which starts at 0 for an insert that does not name it (EF Core's bulk insert).
                    if (dialect == SqlDialect.SqlServer)
                        columns.Add(new ColumnSchemaModel(column.Column, "timestamp", false, false, null));
                    else if (dialect != SqlDialect.PostgreSql)
                        columns.Add(new ColumnSchemaModel(column.Column, dialect switch { SqlDialect.MySql => "bigint", SqlDialect.Oracle => "NUMBER(19)", _ => "INTEGER" }, false, false, "0"));
                    continue;
                }

                // Pragmatic's scopes column admits null, and so does every member of an optional owned type (EF Core's optional dependent).
                var nullable = column.Column == "AccessScopes" || EntityModel.IsNullable(column.Type) || column.OptionalOwner;
                var type = SchemaTypes.Of(column, dialect, SchemaTypes.MaxLength(SchemaTypes.PropertyOf(entity, column)), keyed.Contains(column.Column)); // on SQL Server an indexed string is nvarchar(450), as EF Core's branch writes it
                if (entity.Identity && entity.Keys.Contains(column))
                    type = SchemaTypes.Identity(type, dialect);
                columns.Add(new ColumnSchemaModel(column.Column, type, nullable, entity.Keys.Contains(column), softDelete && column.Column == "IsDeleted" ? falsy : null));
            }

            EquatableArray<CheckSchemaModel> checks = PragmaticShape.Has(entity.Type, "TemporalRelationAttribute")
                ? [new CheckSchemaModel($"CK_{entity.Table}_ValidRange", "\"ValidTo\" IS NULL OR \"ValidTo\" >= \"ValidFrom\"")]
                : [];
            tables[entity.Table] = new TableSchemaModel(entity.Table, columns.ToEquatableArray(), indexes.Select(i => new IndexSchemaModel(i.Name, i.Columns.ToEquatableArray(), i.Unique, i.Filter)).ToEquatableArray(),
                foreignKeys.ToEquatableArray(), schema, checks);

            foreach (var collection in entity.Collections.Where(c => c.IsLink && !tables.ContainsKey(c.LinkTable!)))
            {
                if (lookup(collection.Child) is not { } child || collection.LinkProblem(entity, child) is not null)
                    continue;
                // Pragmatic's join table: its side whose type name sorts first gives the first column; the index on the second; both foreign keys cascade.
                var (left, leftColumn, right, rightColumn) = string.CompareOrdinal(entity.Type.Name, child.Type.Name) <= 0
                    ? (entity, collection.ForeignKey, child, collection.LinkChildColumn!) : (child, collection.LinkChildColumn!, entity, collection.ForeignKey);
                var link = new List<ColumnSchemaModel>
                {
                    new(leftColumn, SchemaTypes.Of(left.Keys[0], dialect, null, true), false, true, null),
                    new(rightColumn, SchemaTypes.Of(right.Keys[0], dialect, null, true), false, true, null),
                };
                var linkKeys = new List<ForeignKeySchemaModel>();
                if (Holds(left.Type))
                    linkKeys.Add(ForeignKey($"FK_{collection.LinkTable}_{leftColumn}_{left.Table}", leftColumn, left.Table, left.KeyColumn, "Cascade"));
                if (Holds(right.Type))
                    linkKeys.Add(ForeignKey($"FK_{collection.LinkTable}_{rightColumn}_{right.Table}", rightColumn, right.Table, right.KeyColumn, "Cascade"));
                tables[collection.LinkTable!] = new TableSchemaModel(collection.LinkTable!, link.ToEquatableArray(), [new IndexSchemaModel($"IX_{collection.LinkTable}_{rightColumn}", [rightColumn], false, null)], linkKeys.ToEquatableArray(), schema);
            }
        }

        return tables.Values.ToList();
    }

    /// <summary>A foreign key with Pragmatic's delete behavior as Migrations' action (<c>MapOnDelete</c>'s: what it does not name is no action).</summary>
    private static ForeignKeySchemaModel ForeignKey(string name, string column, string table, string referenced, string onDelete)
        => new(name, column, table, referenced, onDelete is "Cascade" or "SetNull" or "Restrict" ? onDelete : "NoAction");
}
