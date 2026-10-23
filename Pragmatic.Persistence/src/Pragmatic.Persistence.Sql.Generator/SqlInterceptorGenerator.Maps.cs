using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The maps, readers and writers registered at module start for the entities and DTOs this assembly declares.</summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>
    ///     The entity readers the plans call (for every entity this assembly queries), and the maps the runtime path translates
    ///     with, registered at module start by the assembly that declares the entity or the DTO: one description per type.
    /// </summary>
    /// <summary>A JSON column's initializer: the mark, and the members whose JSON name is not their own, for the runtime path's JSON paths.</summary>
    private static string JsonInitializer(ColumnModel column)
    {
        var renamed = JsonNames.Renamed(column.Type);
        if (renamed.Count == 0)
            return " { IsJson = true }";
        var entries = string.Join(", ", renamed.Select(r => $"[{Literal(r.Path)}] = {Literal(r.Name)}"));
        return $" {{ IsJson = true, JsonNames = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal) {{ {entries} }} }}";
    }

    private static void EmitReaders(StringBuilder source, List<EntityModel> entities, List<(INamedTypeSymbol Dto, EntityModel Entity, Projection Projection, List<string> Joins)> projections, List<AppFunction> functions, Compilation compilation, Func<INamedTypeSymbol, EntityModel?> lookup)
    {
        source.AppendLine("    file static class EntityMaps");
        source.AppendLine("    {");
        // The pools of the [Interned] columns this assembly reads: one per column, shared by the entity's reader, the projections and the runtime path.
        foreach (var pool in entities.SelectMany(e => e.Columns).Where(c => c.InternPool is not null).Select(c => (Field: c.InternPool!, Key: c.InternKey!)).Distinct().OrderBy(p => p.Field, StringComparer.Ordinal))
            source.AppendLine($"        internal static readonly global::Pragmatic.Persistence.Sql.SqlInternPool {pool.Field} = global::Pragmatic.Persistence.Sql.SqlInternPool.For({Literal(pool.Key)});");
        source.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("        internal static void Register()");
        source.AppendLine("        {");
        foreach (var entity in entities.Where(e => !e.IsRawShape && SymbolEqualityComparer.Default.Equals(e.Type.ContainingAssembly, compilation.Assembly)))
        {
            var columns = string.Join(", ", entity.Columns.Select(c => $"new global::Pragmatic.Persistence.Sql.SqlColumn(\"{c.Property}\", \"{c.Column}\", global::Pragmatic.Persistence.Sql.SqlParamKind.{LambdaTranslator.KindOf(c.Store)}, static (r, i, d) => {c.Read("r", "i", "d")}, {ToStore(c)}){(LambdaTranslator.IsJsonConverter(c.Converter) ? JsonInitializer(c) : c.FullTextTable is not null ? $" {{ FullTextTable = {Literal(c.FullTextTable)}, FullTextKey = {Literal(c.FullTextKey!)} }}" : "")}"));
            // Navigations and collections carry what an Include on the runtime path needs: the target read at an offset, the property's get and set, the children's assignment.
            var navigations = string.Join(", ", entity.Navigations.Select(n => Navigation(entity, n, lookup))); // a composite navigation with one foreign key column per key column of its target
            // A many-to-many side carries the join table and its child column; its child key is the child's own key (the links are matched on it).
            var collections = string.Join(", ", entity.Collections.Select(c =>
            {
                var childType = c.Child.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var childModel = lookup(c.Child);
                var foreignKeyColumn = childModel is null ? c.ForeignKey : c.ForeignKeyColumn(childModel);
                var childKey = c.IsLink ? childModel?.KeyProperty ?? "Id" : c.ForeignKey;
                // A composite parent's: the children grouped on all their foreign keys (a link's child: on its whole key), the columns listed for the match.
                var initializers = new List<string>();
                if (c.IsLink)
                    initializers.Add($"LinkTable = {Literal(c.LinkTable!)}, LinkChildColumn = {Literal(c.LinkChildColumn!)}");
                if (c.IsLink && c.LinkChildColumns.Length > 1)
                    initializers.Add($"LinkChildColumns = new string[] {{ {string.Join(", ", c.LinkChildColumns.Select(Literal))} }}");
                if (c.IsComposite && childModel is not null)
                    initializers.Add($"ForeignKeyColumns = new string[] {{ {string.Join(", ", c.ForeignKeyColumns(childModel).Select(Literal))} }}");
                var link = initializers.Count == 0 ? "" : " { " + string.Join(", ", initializers) + " }";
                var childKeyText = c.IsLink && childModel is { HasCompositeKey: true }
                    ? $"new global::Pragmatic.Persistence.Sql.SqlCompositeKey({string.Join(", ", childModel.Keys.Select(k => $"(({childType})c).{k.Property}"))})"
                    : c.IsComposite && !c.IsLink ? $"new global::Pragmatic.Persistence.Sql.SqlCompositeKey({string.Join(", ", c.ForeignKeys.Select(fk => $"(({childType})c).{fk}"))})" : $"((({childType})c).{childKey})";
                return $"new global::Pragmatic.Persistence.Sql.SqlCollection(\"{c.Property}\", typeof({childType}), \"{foreignKeyColumn}\", static c => {childKeyText}, static e => (({entity.FullName})e).{c.Property}, static (e, rows) => (({entity.FullName})e).{c.Property} = rows.ConvertAll(static x => ({childType})x)){link}";
            }));
            var rules = string.Join(", ", entity.Rules.Select(r => $"static () => new {r.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}().ToExpression()"));
            // The writes: every column in order, bound from the entity; the key columns and the version by position; the keys alone for a delete.
            var keys = entity.Keys;
            // [DatabaseGenerated] columns stay out of every write and come back by key after it.
            var written = entity.Columns.Where(c => c.Generated == 0).ToList();
            var filled = entity.Columns.Where(c => c.Generated > 0).ToList();
            var generated = filled.Count == 0 ? "null"
                : $"new global::Pragmatic.Persistence.Sql.SqlGenerated<{entity.FullName}>(new string[] {{ {string.Join(", ", filled.Select(c => Literal(c.Column)))} }}, {(filled.Any(c => c.Generated == 2) ? "true" : "false")}, static (e, r, d) => {{ {string.Join(" ", filled.Select((c, i) => $"e.{c.Property} = {c.Read("r", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "d")};"))} }})";
            // Table per type: the root's columns go to the root's table, the key and each level's own columns to that level's table (a part per
            // level below the root, nested: Car's then SportsCar's); the discriminator is the view's, never written.
            var levels = new List<(EntityModel Model, HashSet<string> Own)>();
            for (var level = entity.Type; entity.PartTable is not null && level is not null && lookup(level) is { PartTable: not null } levelModel; level = level.BaseType)
                levels.Insert(0, (levelModel, new HashSet<string>(level.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name))));
            var rootColumns = entity.Tpt ? written.Where(c => !c.IsDiscriminator && c.DerivedOf is null && !levels.Any(l => l.Own.Contains(c.Property))).ToList() : written;
            var part = "null";
            for (var i = levels.Count - 1; i >= 0; i--)
                part = WriterText(entity, written.Where(c => keys.Contains(c) || (c.DerivedOf is null && levels[i].Own.Contains(c.Property))).ToList(), levels[i].Model.PartTable!, "null", part, root: false);
            var writer = WriterText(entity, rootColumns, entity.WriteTable ?? entity.Table, generated, part, root: true);
            var initializers = new List<string>();
            if (entity.StampsText() is { } stamps)
                initializers.Add($"Stamps = {stamps}"); // [Auditable], [SoftDelete]: the root's table holds the stamped columns
            if (rootColumns.FindIndex(c => c.IsRowVersion) is var native and >= 0)
            {
                // [ConcurrencyAware]: where the database keeps the version (xmin, rowversion) an insert binds every column but it.
                var binds = string.Join(" ", rootColumns.Where((_, i) => i != native).Select((c, i) => $"p[o + {i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {c.Bind(c.Value("e"))});"));
                initializers.Add($"NativeVersion = new global::Pragmatic.Persistence.Sql.SqlNativeVersion<{entity.FullName}>(static (e, p, o, d) => {{ {binds} }})");
            }

            if (initializers.Count > 0)
                writer += " { " + string.Join(", ", initializers) + " }";
            var fixedPredicate = entity.FixedPredicate is null ? "null" : Literal(entity.FixedPredicate);
            // A view has no writer: the session says so when asked to write it. A TPC root's writes go to the row's concrete type (Dispatch).
            var tail = entity.ReadOnly ? "ReadOnly = true" : entity.Tpc && entity.IsHierarchyRoot ? "Dispatch = true" : entity.Tpt ? $"Dispatch = true, Writer = {writer}" : $"Writer = {writer}"; // TPT: every write through a batch, the derived table's statement beside the root's
            if (entity.SoftDeleteColumn is { } softDelete)
                tail += $", SoftDeleteColumn = {Literal(softDelete)}{(entity.SoftDeleteCascade ? ", SoftDeleteCascade = true" : "")}"; // the runtime path's reads leave the deleted rows out
            if (entity.IsPragmatic)
                tail += ", CallerFiltered = true"; // its rows depend on the caller: the runtime path refuses it until it applies the families
            if (keys.Count > 1)
                tail += $", CompositeKeyOf = static e => new global::Pragmatic.Persistence.Sql.SqlCompositeKey({string.Join(", ", keys.Select(k => $"(({entity.FullName})e).{k.Property}"))})"; // what a composite parent's children group on
            foreach (var rollUp in entity.Columns.Where(c => c.RollUpChild is not null))
            {
                // The rule goes with the parent's map; the child's writer finds it by its table on its first write.
                if (lookup(rollUp.RollUpChild!) is not { } child || keys.Count != 1)
                    continue;
                var foreignKey = child.Column(entity.Type.Name + "Id")?.Column ?? entity.Type.Name + "Id";
                var amount = rollUp.RollUpProperty is null ? "null" : Literal(child.Column(rollUp.RollUpProperty)?.Column ?? rollUp.RollUpProperty);
                var live = child.SoftDeleteColumn is null ? "" : $" {{ LiveColumn = {Literal(child.SoftDeleteColumn)} }}"; // a soft-deleted child counts for nothing
                source.AppendLine($"            global::Pragmatic.Persistence.Sql.SqlRollUp.Register(new({Literal(entity.WriteTable ?? entity.Table)}, {Literal(keys[0].Column)}, {Literal(rollUp.Column)}, {Literal(child.WriteTable ?? child.Table)}, {Literal(foreignKey)}, {amount}){live});");
            }

            source.AppendLine($"            global::Pragmatic.Persistence.Sql.SqlEntityMap<{entity.FullName}>.Register(new(\"{entity.Table}\", new string[] {{ {string.Join(", ", keys.Select(k => Literal(k.Column)))} }}, new global::Pragmatic.Persistence.Sql.SqlColumn[] {{ {columns} }}, new global::Pragmatic.Persistence.Sql.SqlNavigation[] {{ {navigations} }}, new global::Pragmatic.Persistence.Sql.SqlCollection[] {{ {collections} }}, new global::System.Func<global::System.Linq.Expressions.LambdaExpression>[] {{ {rules} }}, Read_{Safe(entity)}, static e => e.{(keys.Count > 0 ? keys[0] : entity.Columns[0]).Property}, {fixedPredicate}) {{ {tail} }});");
        }

        foreach (var (dto, entity, projection, joins) in projections)
        {
            var dtoName = dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var columns = string.Join(", ", projection.Columns.Select(Literal));
            // The DTO's collection members: the child DTO's own registration gives its columns and reader; here the key the loader groups on and the assignment.
            var collections = projection.Collections.Count == 0 ? "" : " { Collections = new global::Pragmatic.Persistence.Sql.SqlProjectedCollection[] { " + string.Join(", ", projection.Collections.Select(c =>
                projection.KeyProperties is { } keyProperties // a composite parent: its key as the DTO's properties hold it, the child's foreign key columns
                    ? $"new global::Pragmatic.Persistence.Sql.SqlProjectedCollection(\"{c.Property}\", typeof({c.Child.FullName}), typeof({c.ChildDtoName}), \"{c.ForeignKeyColumn}\", static p => new global::Pragmatic.Persistence.Sql.SqlCompositeKey({string.Join(", ", keyProperties.Select(k => $"(({dtoName})p).{k}"))}), static (p, rows) => (({dtoName})p).{c.Property} = rows.ConvertAll(static x => ({c.ChildDtoName})x)) {{ ForeignKeyColumns = new string[] {{ {string.Join(", ", c.ForeignKeyColumns.Select(Literal))} }} }}"
                    : $"new global::Pragmatic.Persistence.Sql.SqlProjectedCollection(\"{c.Property}\", typeof({c.Child.FullName}), typeof({c.ChildDtoName}), \"{c.ForeignKeyColumn}\", static p => (({dtoName})p).{projection.KeyProperty}, static (p, rows) => (({dtoName})p).{c.Property} = rows.ConvertAll(static x => ({c.ChildDtoName})x))")) + " } }";
            source.AppendLine($"            global::Pragmatic.Persistence.Sql.SqlProjectionMap<{entity.FullName}, {dtoName}>.Register(new(new string[] {{ {columns} }}, new string[] {{ {string.Join(", ", joins.Select(Literal))} }}, static (r, d) => {projection.Construct}){collections});");
        }

        // The application's functions, for the runtime path: the method from a delegate, its renderer per dialect as the generator read it.
        EmitFunctionRegistrations(source, functions);
        source.AppendLine("        }");
        source.AppendLine();
        foreach (var entity in entities)
        {
            source.AppendLine($"        internal static {entity.FullName} Read_{Safe(entity)}(global::System.Data.Common.DbDataReader r, global::Pragmatic.Persistence.Sql.SqlDialect d)");
            source.AppendLine($"            => {entity.Construct("r", "d", static i => i.ToString())};");
            source.AppendLine();
        }

        EmitPrivateSetters(source, entities);
        source.AppendLine("    }");
        source.AppendLine();
    }

    /// <summary>
    ///     The writer of <paramref name="written" /> into <paramref name="table" />: every column in order, bound from the entity; the key columns
    ///     and the version by position; the keys alone for a delete. A table-per-type part (<paramref name="root" /> false) has no version and no
    ///     identity: the root's table holds them.
    /// </summary>
    private static string WriterText(EntityModel entity, List<ColumnModel> written, string table, string generated, string part, bool root)
    {
        var keys = entity.Keys;
        var keyIndexes = string.Join(", ", keys.Select(k => written.IndexOf(k)));
        var keyIndex = keys.Count > 0 ? written.IndexOf(keys[0]) : 0;
        var versionIndex = !root || entity.Version is null ? -1 : written.FindIndex(c => c.Property == entity.Version);
        var binds = string.Join(" ", written.Select((c, i) => $"p[o + {i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {c.Bind(c.Value("e"))});"));
        var bindKeys = string.Join(" ", keys.Select((k, i) => $"p[o + {i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {k.Bind(k.Value("e"))});"));
        var keyText = keys.Count == 1 ? $"static e => e.{keys[0].Property}" : $"static e => \"(\" + {string.Join(" + \", \" + ", keys.Select(k => $"e.{k.Property}"))} + \")\"";
        // The upsert reads the version back; an identity entity binds its inserts without the key and reads the key back.
        var readVersion = versionIndex >= 0 ? $"static (e, r, d) => e.{entity.Version} = {written[versionIndex].Read("r", "0", "d")}" : "null";
        var identity = "null";
        if (root && entity.Identity)
        {
            var withoutKey = string.Join(" ", written.Where((_, i) => i != keyIndex).Select((c, i) => $"p[o + {i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {c.Bind(c.Value("e"))});"));
            identity = $"new global::Pragmatic.Persistence.Sql.SqlIdentity<{entity.FullName}>(static (e, p, o, d) => {{ {withoutKey} }}, static (e, r, d) => e.{written[keyIndex].Property} = {written[keyIndex].Read("r", "0", "d")})";
        }

        // PostgreSQL casts on writes ([Column(Cast = "jsonb")]): one entry per column, null where the value goes as bound.
        var casts = written.Any(c => c.Cast is not null) ? "new string?[] { " + string.Join(", ", written.Select(c => c.Cast is null ? "null" : Literal(c.Cast))) + " }" : "null";
        // A save of the modified columns: the property each column reads, and one column bound at a parameter index (a switch, no delegate per column).
        var properties = string.Join(", ", written.Select(c => Literal(c.Property)));
        var bindColumn = string.Join(" ", written.Select((c, i) => $"case {i}: p[i].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {c.Bind(c.Value("e"))}); break;"));
        // A snapshot of the row: one column's value as it is bound (a switch expression, no delegate per column).
        var columnValue = string.Join(", ", written.Select((c, i) => $"{i} => global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {c.Bind(c.Value("e"))})"));
        return $"new global::Pragmatic.Persistence.Sql.SqlWriter<{entity.FullName}>(\"{table}\", new string[] {{ {string.Join(", ", written.Select(c => Literal(c.Column)))} }}, new global::Pragmatic.Persistence.Sql.SqlParamKind[] {{ {string.Join(", ", written.Select(c => "global::Pragmatic.Persistence.Sql.SqlParamKind." + LambdaTranslator.KindOf(c.Store)))} }}, new int[] {{ {keyIndexes} }}, {versionIndex}, static (e, p, o, d) => {{ {binds} }}, static (e, p, o, d) => {{ {bindKeys} }}, {keyText}, {(versionIndex >= 0 ? $"static e => e.{entity.Version}" : "null")}, {(versionIndex >= 0 ? $"static e => e.{entity.Version}++" : "null")}, {readVersion}, {identity}, {casts}, new string[] {{ {properties} }}, static (e, c, p, i, d) => {{ switch (c) {{ {bindColumn} }} }}, {generated}, {part}, static (e, c, d) => c switch {{ {columnValue}, _ => null }})";
    }

    /// <summary>The boxed converter towards the column, for the runtime path's predicates; null when the column stores the property's type.</summary>
    private static string ToStore(ColumnModel column)
        => column.Converter is null ? "null" : $"static v => v is null ? null : (object){column.Converter}.ToStore(({EntityModel.Unwrap(column.Type).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})v)";

    /// <summary>The registered description of a to-one navigation: the join column, and the target read at an offset (its columns in the target's order) when the target entity is known.</summary>
    private static string Navigation(EntityModel entity, NavigationModel navigation, Func<INamedTypeSymbol, EntityModel?> lookup)
    {
        var targetName = navigation.Target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var target = lookup(navigation.Target);
        var readAt = target is null
            ? "static (r, o, d) => throw new global::System.InvalidOperationException(\"" + targetName + " is not an [Entity] the generator could read\")"
            : $"static (r, o, d) => {target.Construct("r", "d", static i => "o + " + i)}";
        var composite = navigation.IsComposite ? $" {{ ForeignKeyColumns = new string[] {{ {string.Join(", ", navigation.ForeignKeys.Select(fk => Literal(entity.Column(fk)?.Column ?? fk)))} }} }}" : ""; // one per key column of the target
        return $"new global::Pragmatic.Persistence.Sql.SqlNavigation(\"{navigation.Property}\", \"{entity.Column(navigation.ForeignKey)?.Column ?? navigation.ForeignKey}\", {(navigation.Required ? "true" : "false")}, typeof({targetName}), {readAt}, static e => (({entity.FullName})e).{navigation.Property}, static (e, v) => (({entity.FullName})e).{navigation.Property} = ({targetName})v!){composite}"; // the setter takes a missing row as null whatever the property's annotation (a cast of null to a class is null)
    }

    /// <summary>
    ///     The private setters the rows are built through (Pragmatic's generated <c>OwnerId</c>, <c>{Navigation}Id</c>): an <c>[UnsafeAccessor]</c>
    ///     on the declaring type, no reflection (Native AOT), and a helper that returns the row, generic so a derived row keeps its type.
    /// </summary>
    private static void EmitPrivateSetters(StringBuilder source, List<EntityModel> entities)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in entities.SelectMany(e => e.Columns).Where(c => c.PrivateSetter && c.Owner is null))
        {
            if (!emitted.Add(column.SetterName))
                continue;
            var type = column.Type.ToDisplayString(Annotated);
            source.AppendLine($"        internal static TRow {column.SetterName}<TRow>(TRow row, {type} value) where TRow : {column.SetterOwner}");
            source.AppendLine("        {");
            source.AppendLine($"            {column.SetterName}_Accessor(row, value);");
            source.AppendLine("            return row;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine($"        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_{column.Property}\")]");
            source.AppendLine($"        private static extern void {column.SetterName}_Accessor({column.SetterOwner} row, {type} value);");
            source.AppendLine();
        }
    }
}
