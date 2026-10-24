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
    ///     The schema of each database the host's Sql-served entities live in; empty outside a host, without Pragmatic.Migrations, without a
    ///     provider to type the tables for, or when an entity cannot be described yet (reported where it is declared,
    ///     <see cref="ReportPragmaticSchemaProblems" />).
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

        var schemas = new List<EngineSchemaModel>();
        foreach (var database in served.GroupBy(t => DatabaseOf(t)!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var entities = database.Select(Lookup).Where(e => e is not null).Select(e => e!).ToList();
            if (entities.Count == 0 || entities.Any(e => PragmaticSchemaProblem(e) is not null))
                continue;
            // The namespace Pragmatic's EF branch derives from the database's entities: one of the application's, the first by name.
            var representative = database.FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly)) ?? database.First();
            var ns = RootNamespace(representative.ContainingNamespace.IsGlobalNamespace ? compilation.AssemblyName ?? "" : representative.ContainingNamespace.ToDisplayString());
            schemas.Add(new EngineSchemaModel(database.Key, ns, ProviderName(dialect), configKeys.TryGetValue(database.Key, out var key) ? key : null,
                PragmaticTables(entities, Lookup, dialect).Select(SchemaTableOf).ToEquatableArray()));
        }

        return schemas.ToEquatableArray();
    }

    /// <summary>
    ///     Reports, where they are declared, the Sql-served entities of this compilation the schema cannot describe yet: their database then
    ///     gets none of the engine's tables (<see cref="PragmaticSchemas" />). Only with Pragmatic.Migrations referenced, the schema's reader.
    /// </summary>
    private static void ReportPragmaticSchemaProblems(Compilation compilation, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        if (compilation.GetTypeByMetadataName(SchemaVersionType) is null)
            return;
        var engines = PragmaticEngines.Of(compilation);
        foreach (var type in PragmaticNames.AllTypes(compilation.Assembly.GlobalNamespace)
                     .Where(t => t.DeclaringSyntaxReferences.Length > 0 && PragmaticShape.IsPragmaticEntity(t) && engines.Serves(t, PragmaticEngine.Sql))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (lookup(type) is { } entity && PragmaticSchemaProblem(entity) is { } problem)
                diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, entity.Type.Locations.FirstOrDefault() ?? Location.None, "its database", entity.Type.Name, problem));
        }
    }

    /// <summary>Why <paramref name="entity" /> cannot be described in the schema yet, or null.</summary>
    private static string? PragmaticSchemaProblem(EntityModel entity)
        => entity.Columns.FirstOrDefault(c => c.Owner is not null) is { } owned ? $"'{owned.Owner}' is an owned type: the schema does not flatten one yet"
            : entity.Type.GetAttributes().Any(a => a.AttributeClass?.Name == "LogicKeyAttribute") || entity.Type.GetMembers().Any(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == "LogicKeyAttribute")) ? "[LogicKey]: the schema does not describe its unique index yet"
            : null;

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
                    if (dialect != SqlDialect.PostgreSql)
                        columns.Add(new ColumnSchemaModel(column.Column, dialect switch { SqlDialect.SqlServer => "timestamp", SqlDialect.MySql => "bigint", SqlDialect.Oracle => "NUMBER(19)", _ => "INTEGER" }, false, false, null));
                    continue;
                }

                var nullable = column.Column == "AccessScopes" || EntityModel.IsNullable(column.Type); // Pragmatic's scopes column admits null
                var type = SchemaTypes.Of(column, dialect, SchemaTypes.MaxLength(SchemaTypes.PropertyOf(entity, column)), keyed.Contains(column.Column)); // on SQL Server an indexed string is nvarchar(450), as EF Core's branch writes it
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
