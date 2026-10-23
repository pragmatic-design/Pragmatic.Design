using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The schema of Pragmatic's <c>[Entity]</c> types the Sql engine serves, for Pragmatic.Migrations, in the shape Pragmatic's EF branch gives it
///     (<c>SchemaMetadataTemplate</c>, <c>SchemaMetadataTransform</c>): <c>{Database}Schema</c> in the host's root namespace, its <c>Current</c> the
///     provider of the driver the compilation references (PostgreSQL, then SQL Server, then SQLite, the order EF's provider is detected in, then
///     MySQL and Oracle), and
///     <c>For(SqlDialect)</c> for each. The database is <c>Default</c>: a topology of <c>[Include&lt;TModule, TDatabase&gt;]</c> is reported, not
///     followed. Its own source, needing Pragmatic.Migrations and the engine's <c>SqlDialect</c> only, so it compiles alone (<c>SchemaParity</c>).
///     Where Pragmatic's schema cannot migrate on a provider the spike departs from it, each departure listed in <c>SchemaParityTests</c>.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private static readonly SqlDialect[] PragmaticSchemaDialects = [SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.Sqlite, SqlDialect.MySql, SqlDialect.Oracle];

    /// <summary>The schema source (hint, text), or null: no Pragmatic.Migrations, no Pragmatic entity the engine serves, or one it cannot describe.</summary>
    private static (string Hint, string Text)? EmitPragmaticSchema(Compilation compilation, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        if (compilation.GetTypeByMetadataName("Pragmatic.Migrations.Schema.SchemaVersion") is null)
            return null;
        var engines = PragmaticEngines.Of(compilation);
        var served = PragmaticNames.AllTypes(compilation.Assembly.GlobalNamespace)
            .Where(t => t.DeclaringSyntaxReferences.Length > 0 && PragmaticShape.IsPragmaticEntity(t) && engines.Serves(t, PragmaticEngine.Sql))
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        if (served.Count == 0)
            return null;
        var entities = served.Select(lookup).Where(e => e is not null).Select(e => e!).ToList();

        if (HasDatabaseTopology(compilation) is { } include)
            diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, include, "the host", "its databases", "[Include<TModule, TDatabase>] names a database: the spike emits the Default schema only (the topology is a porting item)"));
        foreach (var entity in entities)
        {
            var problem = entity.Columns.FirstOrDefault(c => c.Owner is not null) is { } owned ? $"'{owned.Owner}' is an owned type: the spike does not flatten one into the schema yet"
                : entity.Type.GetAttributes().Any(a => a.AttributeClass?.Name == "LogicKeyAttribute") || entity.Type.GetMembers().Any(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == "LogicKeyAttribute")) ? "[LogicKey]: the spike does not describe its unique index yet"
                : null;
            if (problem is null)
                continue;
            diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, entity.Type.Locations.FirstOrDefault() ?? Location.None, "DefaultSchema", entity.Type.Name, problem));
            return null;
        }

        var current = compilation.GetTypeByMetadataName("Npgsql.NpgsqlConnection") is not null ? SqlDialect.PostgreSql
            : compilation.GetTypeByMetadataName("Microsoft.Data.SqlClient.SqlConnection") is not null ? SqlDialect.SqlServer
            : compilation.GetTypeByMetadataName("Microsoft.Data.Sqlite.SqliteConnection") is not null ? SqlDialect.Sqlite
            : compilation.GetTypeByMetadataName("MySqlConnector.MySqlConnection") is not null ? SqlDialect.MySql
            : compilation.GetTypeByMetadataName("Oracle.ManagedDataAccess.Client.OracleConnection") is not null ? SqlDialect.Oracle
            : (SqlDialect?)null;
        var ns = RootNamespace(served[0].ContainingNamespace.IsGlobalNamespace ? compilation.AssemblyName ?? "" : served[0].ContainingNamespace.ToDisplayString());
        var dialects = PragmaticSchemaDialects.Select(d => new DialectSchemaModel(d.ToString(), ProviderName(d), PragmaticTables(entities, lookup, d).ToEquatableArray()));
        var template = new PragmaticSchemaTemplate(new PragmaticSchemaModel(ns.Length > 0 ? ns : null, entities.Count, current?.ToString(), dialects.ToEquatableArray()));
        return (VirtualFolderHints.ForAssembly("Sql", "PragmaticSchema"), template.ToSourceText().ToString());
    }

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

    /// <summary>The first <c>[Include&lt;TModule, TDatabase&gt;]</c> on a <c>[Module]</c> of the compilation, or null.</summary>
    private static Location? HasDatabaseTopology(Compilation compilation)
    {
        foreach (var type in PragmaticNames.AllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Composition.Attributes.ModuleAttribute"))
                continue;
            if (type.GetAttributes().FirstOrDefault(a => a.AttributeClass is { IsGenericType: true, TypeArguments.Length: 2 } c && c.OriginalDefinition.ToDisplayString().StartsWith("Pragmatic.Composition.Attributes.IncludeAttribute", StringComparison.Ordinal)) is { } include)
                return include.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? type.Locations.FirstOrDefault();
        }

        return null;
    }

    /// <summary>The tables of <paramref name="entities" /> on <paramref name="dialect" />, in table order, the join tables with them.</summary>
    private static List<TableSchemaModel> PragmaticTables(List<EntityModel> entities, Func<INamedTypeSymbol, EntityModel?> lookup, SqlDialect dialect)
    {
        var schema = dialect switch { SqlDialect.PostgreSql => "public", SqlDialect.SqlServer => "dbo", _ => null };
        // DEPARTURE: Pragmatic writes false everywhere, which T-SQL has no literal for; MySQL's bool is tinyint(1), Oracle's BOOLEAN reads FALSE back.
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
                    continue; // another engine's or boundary's row: the key without the constraint, as Pragmatic skips a cross-boundary one
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
                var type = SchemaTypes.Of(column, dialect, SchemaTypes.MaxLength(SchemaTypes.PropertyOf(entity, column)), keyed.Contains(column.Column)); // DEPARTURE on SQL Server: an indexed string is nvarchar(450), not nvarchar(max)
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
