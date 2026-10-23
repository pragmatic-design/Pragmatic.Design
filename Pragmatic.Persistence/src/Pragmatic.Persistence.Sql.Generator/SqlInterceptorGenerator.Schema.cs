using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The schema of each <c>[SqlDatabase]</c> class of the assembly, for Pragmatic.Migrations: <c>Schema(SqlDialect)</c> on the class, one
///     <c>SchemaVersion</c> per provider Migrations has. A table per entity stored in the database (a hierarchy in one table once, at its root; a
///     table per concrete type's tables, not its view), its columns as the writer binds them (nullable where the property, an optional owner or
///     a derived type's own column admits null), its key, an index per <c>[Indexed]</c> column and per foreign key, a foreign key per to-one
///     navigation and per collection whose child names no navigation back (NO ACTION: the graph deletes say what goes with a row), and the
///     join table of each many-to-many side. Emitted only when the compilation references Pragmatic.Migrations.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private sealed class SchemaColumn(string name, ColumnModel source, bool nullable, bool key, string? defaultValue, int? maxLength, bool keyed)
    {
        public string Name { get; } = name;
        public ColumnModel Source { get; } = source;
        public bool Nullable { get; } = nullable;
        public bool Key { get; } = key;
        public string? DefaultValue { get; } = defaultValue;
        public int? MaxLength { get; } = maxLength;
        public bool Keyed { get; set; } = keyed; // a key, indexed or foreign key column: SQL Server's indexable string length
    }

    private sealed class SchemaTable(string name)
    {
        public string Name { get; } = name;
        public List<SchemaColumn> Columns { get; } = [];
        public List<(string Name, string[] Columns, bool Unique, string? Filter)> Indexes { get; } = [];
        public List<(string Name, string Column, string Table, string ReferencedColumn)> ForeignKeys { get; } = [];

        public void Index(string column, bool unique = false, string? filter = null) => Index([column], unique, filter, null);

        public void Index(string[] columns, bool unique, string? filter, string? name)
        {
            name ??= $"IX_{Name}_{string.Join("_", columns)}";
            if (Indexes.Exists(i => i.Name == name))
                return; // the first declaration of a name holds it ([Unique] before the foreign keys' plain indexes)
            Indexes.Add((name, columns, unique, filter));
            foreach (var column in columns)
            {
                if (Columns.Find(c => c.Name == column) is { } keyed)
                    keyed.Keyed = true;
            }
        }

        public void ForeignKey(string column, EntityModel target)
        {
            var name = $"FK_{Name}_{column}_{target.Table}";
            if (!ForeignKeys.Exists(f => f.Name == name))
                ForeignKeys.Add((name, column, target.Table, target.KeyColumn));
            Index(column);
        }
    }

    private const string Migrations = "global::Pragmatic.Migrations.Schema.";

    private static void EmitSchemas(StringBuilder extensions, Compilation compilation, List<EntityModel> own, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        var databases = AllTypes(compilation.Assembly.GlobalNamespace).Where(t => t.GetAttributes().Any(a => a.AttributeClass?.Name == "SqlDatabaseAttribute")).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        if (databases.Count == 0)
            return;
        foreach (var database in databases)
        {
            var where = database.Locations.FirstOrDefault() ?? Location.None;
            if (compilation.GetTypeByMetadataName("Pragmatic.Migrations.Schema.SchemaVersion") is null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, where, database.Name, "anything", "the compilation does not reference Pragmatic.Migrations, whose SchemaVersion the schema is"));
                continue;
            }

            if (!database.DeclaringSyntaxReferences.All(r => r.GetSyntax() is ClassDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, where, database.Name, "its tables", "the class must be partial: the generator adds Schema(SqlDialect) to it"));
                continue;
            }

            var members = own.Where(e => StoredIn(e.Type, databases) is { } stored && SymbolEqualityComparer.Default.Equals(stored, database)).ToList();
            var tables = SchemaTables(database, members, lookup, diagnostics);
            if (tables is null)
                continue;
            var configKey = database.GetAttributes().First(a => a.AttributeClass?.Name == "SqlDatabaseAttribute").NamedArguments.FirstOrDefault(n => n.Key == "ConfigKey").Value.Value as string;
            var ns = database.ContainingNamespace.IsGlobalNamespace ? null : database.ContainingNamespace.ToDisplayString();
            extensions.AppendLine();
            if (ns is not null)
                extensions.AppendLine($"namespace {ns}\n{{");
            extensions.AppendLine($"    partial class {database.Name}");
            extensions.AppendLine("    {");
            extensions.AppendLine($"        /// <summary>The schema of {database.Name} for Pragmatic.Migrations ({tables.Count} tables), generated from the entities stored in it: its column types per provider.</summary>");
            extensions.AppendLine($"        public static {Migrations}SchemaVersion Schema(global::Pragmatic.Persistence.Sql.SqlDialect dialect) => dialect switch");
            extensions.AppendLine("        {");
            foreach (var (dialect, provider) in new[] { (SqlDialect.PostgreSql, "ProviderPostgreSql"), (SqlDialect.SqlServer, "ProviderSqlServer"), (SqlDialect.Sqlite, "ProviderSqlite") })
                extensions.AppendLine($"            global::Pragmatic.Persistence.Sql.SqlDialect.{dialect} => new([{string.Join(", ", tables.Select(t => TableText(t, dialect)))}], {Literal(database.Name)}, global::Pragmatic.Migrations.MigrationConstants.{provider}, {(configKey is null ? "null" : Literal(configKey))}),");
            extensions.AppendLine($"            _ => throw new global::System.NotSupportedException($\"Pragmatic.Migrations migrates PostgreSQL, SQL Server and SQLite, not {{dialect}}.\"),");
            extensions.AppendLine("        };");
            extensions.AppendLine("    }");
            if (ns is not null)
                extensions.AppendLine("}");
        }

        // An entity no declared database holds, when there are several: say so rather than leave its table out.
        if (databases.Count > 1)
        {
            foreach (var entity in own.Where(e => !e.IsRawShape && !e.ReadOnly && StoredIn(e.Type, databases) is null))
                diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, entity.Type.Locations.FirstOrDefault() ?? Location.None, "the assembly's databases", entity.Type.Name, $"the assembly declares {databases.Count} databases: mark it [StoredIn<TDatabase>]"));
        }
    }

    /// <summary>The database a type is stored in: its [StoredIn&lt;T&gt;] or a base type's; the one database when the assembly declares one.</summary>
    private static INamedTypeSymbol? StoredIn(INamedTypeSymbol type, List<INamedTypeSymbol> databases)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "StoredInAttribute", TypeArguments.Length: 1 }) is { } mark)
                return mark.AttributeClass!.TypeArguments[0] as INamedTypeSymbol;
        }

        return databases.Count == 1 ? databases[0] : null;
    }

    /// <summary>The tables of the database's entities, in table order; null with a diagnostic when one cannot be described.</summary>
    private static List<SchemaTable>? SchemaTables(INamedTypeSymbol database, List<EntityModel> members, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        var tables = new Dictionary<string, SchemaTable>(StringComparer.Ordinal);
        var stored = new List<EntityModel>();
        foreach (var entity in members.OrderBy(e => e.Table, StringComparer.Ordinal))
        {
            if (entity.IsRawShape || entity.ReadOnly || (entity.Tpc && entity.IsHierarchyRoot) || (!entity.Tpc && entity.FixedPredicate is not null))
                continue; // a view; a table per concrete type's root view; a type below the root of a hierarchy in one table (the root's table has its columns)
            var where = entity.Type.Locations.FirstOrDefault() ?? Location.None;
            string? problem = entity.Tpt ? "a table per type hierarchy: write its tables in the schema by hand (the spike emits none)"
                : entity.Identity ? "a key the database assigns ([Identity]): Pragmatic.Migrations describes no identity column; give it a Guid key"
                : entity.Columns.FirstOrDefault(c => c.Generated > 0 && c.RollUpChild is null) is { } generated ? $"'{generated.Property}' is [DatabaseGenerated]: the schema has no default or computation for it"
                : null;
            if (problem is not null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, where, database.Name, entity.Type.Name, problem));
                return null;
            }

            var table = new SchemaTable(entity.Table);
            var live = entity.SoftDeleteColumn is { } softDelete ? $"\"{softDelete}\" = {{bt:0}}" : null; // a unique index holds the live rows only
            foreach (var column in entity.Columns)
            {
                if (entity.Tpc && column.IsDiscriminator)
                    continue; // the view adds the type's name
                var property = SchemaTypes.PropertyOf(entity, column);
                var nullable = !column.IsDiscriminator && (EntityModel.IsNullable(column.Type) || column.OptionalOwner || column.DerivedOf is not null);
                table.Columns.Add(new SchemaColumn(column.Column, column, nullable, entity.Keys.Contains(column), column.RollUpChild is not null ? "0" : null, SchemaTypes.MaxLength(property), entity.Keys.Contains(column)));
                if (!column.IsKey && SchemaTypes.IsMarkedIndexed(property))
                    table.Index(column.Column, SchemaTypes.IsUnique(property), SchemaTypes.IsUnique(property) ? live : null);
            }

            foreach (var unique in entity.Type.GetAttributes().Where(a => a.AttributeClass?.Name == "UniqueAttribute"))
            {
                var names = unique.ConstructorArguments.Length == 1 && unique.ConstructorArguments[0].Kind == TypedConstantKind.Array ? unique.ConstructorArguments[0].Values.Select(v => v.Value as string).ToList() : [];
                var columns = names.Select(n => n is null ? null : entity.Column(n)?.Column).ToList();
                if (columns.Count == 0 || columns.Any(c => c is null))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaShape, where, database.Name, entity.Type.Name, $"[Unique({string.Join(", ", names)})] names a property that is not a column of {entity.Type.Name}"));
                    return null;
                }

                var filter = unique.NamedArguments.FirstOrDefault(n => n.Key == "Filter").Value.Value as string;
                var both = filter is null ? live : live is null ? filter : $"({filter}) AND {live}";
                table.Index(columns.Select(c => c!).ToArray(), true, both, unique.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string);
            }

            if (entity.SoftDeleteColumn is { } deleted)
                table.Index(deleted); // the reads' implicit filter

            tables[table.Name] = table;
            stored.Add(entity);
        }

        bool Holds(EntityModel? target) => target is not null && tables.ContainsKey(target.Table) && !(target.Tpc && target.IsHierarchyRoot);
        foreach (var entity in stored)
        {
            var table = tables[entity.Table];
            foreach (var navigation in entity.Navigations.Where(n => !n.IsComposite))
            {
                var column = entity.Column(navigation.ForeignKey)?.Column ?? navigation.ForeignKey;
                if (lookup(navigation.Target) is { } target && Holds(target))
                    table.ForeignKey(column, target);
                else
                    table.Index(column); // another database's row: the key without the constraint
            }

            foreach (var collection in entity.Collections)
            {
                if (lookup(collection.Child) is not { } child)
                    continue;
                if (collection.IsLink)
                {
                    LinkTable(tables, entity, child, collection, Holds);
                    continue;
                }

                if (!collection.IsComposite && Holds(child) && !child.Navigations.Exists(n => n.ForeignKey == collection.ForeignKey))
                    tables[child.Table].ForeignKey(collection.ForeignKeyColumn(child), entity); // the child's key back, without a navigation on its side
            }
        }

        return tables.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>A many-to-many side's join table, once: the parent's key columns, the child's, all of them the key; a foreign key to each single-column side.</summary>
    private static void LinkTable(Dictionary<string, SchemaTable> tables, EntityModel parent, EntityModel child, CollectionModel collection, Func<EntityModel?, bool> holds)
    {
        if (tables.ContainsKey(collection.LinkTable!) || collection.LinkProblem(parent, child) is not null)
            return; // the other side declared it first; a mismatched side is reported where its queries are read
        var table = new SchemaTable(collection.LinkTable!);
        for (var i = 0; i < collection.ForeignKeys.Length; i++)
            table.Columns.Add(new SchemaColumn(collection.ForeignKeys[i], parent.Keys[i], false, true, null, null, true));
        for (var i = 0; i < collection.LinkChildColumns.Length; i++)
            table.Columns.Add(new SchemaColumn(collection.LinkChildColumns[i], child.Keys[i], false, true, null, null, true));
        if (parent.Keys.Count == 1 && holds(parent))
            table.ForeignKey(collection.ForeignKey, parent);
        if (child.Keys.Count == 1 && holds(child))
            table.ForeignKey(collection.LinkChildColumn!, child);
        tables[table.Name] = table;
    }

    private static string TableText(SchemaTable table, SqlDialect dialect)
    {
        var columns = table.Columns.Select(c => $"new {Migrations}ColumnSchema({Literal(c.Name)}, {Literal(SchemaTypes.Of(c.Source, dialect, c.MaxLength, c.Keyed))}, {(c.Nullable ? "true" : "false")}, {(c.Key ? "true" : "false")}{(c.DefaultValue is null ? "" : ", " + Literal(c.DefaultValue))})");
        var indexes = table.Indexes.Select(i => $"new {Migrations}IndexSchema({Literal(i.Name)}, [{string.Join(", ", i.Columns.Select(Literal))}]{(i.Unique ? ", IsUnique: true" : "")}{(i.Filter is null ? "" : $", Filter: {Literal(SqlDialects.ResolveBooleanLiterals(i.Filter, dialect))}")})");
        var foreignKeys = table.ForeignKeys.Select(f => $"new {Migrations}ForeignKeySchema({Literal(f.Name)}, {Literal(f.Column)}, {Literal(f.Table)}, {Literal(f.ReferencedColumn)}, {Migrations}ReferentialAction.NoAction)");
        return $"new {Migrations}TableSchema({Literal(table.Name)}, null, [{string.Join(", ", columns)}], [{string.Join(", ", indexes)}], [{string.Join(", ", foreignKeys)}])";
    }
}
