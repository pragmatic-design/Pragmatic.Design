using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     One hop of an Include path: the navigation or collection property and the entity it reaches; a collection may carry a filter
///     (<c>c.Orders.Where(o => !o.IsDeleted)</c>), an ordering of the children (<c>OrderBy</c>, <c>ThenBy</c>; the companion statement orders them)
///     and a Take per parent (<c>.OrderBy(…).Take(3)</c>: a window partitioned by the foreign key, in the same statement).
/// </summary>
internal sealed class IncludeHop
{
    public IncludeHop(string property, EntityModel target, NavigationModel? navigation, CollectionModel? collection, LambdaExpressionSyntax? filter = null, SemanticModel? model = null, List<string>? orderings = null, int? take = null)
    {
        Property = property;
        Target = target;
        Navigation = navigation;
        Collection = collection;
        Filter = filter;
        Model = model;
        Orderings = orderings ?? [];
        Take = take;
    }

    /// <summary>The children's ordering, as SQL fragments over the child's alias (<c>t."CreatedAt" DESC</c>).</summary>
    public List<string> Orderings { get; }

    /// <summary>How many children per parent, at most; null for all of them.</summary>
    public int? Take { get; }

    public string Property { get; }

    public EntityModel Target { get; }

    public NavigationModel? Navigation { get; }

    public CollectionModel? Collection { get; }

    /// <summary>The collection's filter, a predicate over the child on columns and constants, which the child plan adds to its WHERE.</summary>
    public LambdaExpressionSyntax? Filter { get; }

    /// <summary>The model the filter binds in.</summary>
    public SemanticModel? Model { get; }

    public bool IsCollection => Collection is not null;
}

/// <summary>
///     A node of the include tree: what one Include path (with its Then steps) loads under an entity. A to-one
///     node is a join in this plan and a nested object in its reader; a collection node is a child plan on the
///     parents' keys, whose own children it carries.
/// </summary>
internal sealed class IncludeNode
{
    public IncludeNode(IncludeHop hop) => Hop = hop;

    public IncludeHop Hop { get; }

    public List<IncludeNode> Children { get; } = [];

    public bool IsCollection => Hop.IsCollection;

    public EntityModel Target => Hop.Target;

    public string Property => Hop.Property;

    /// <summary>True when a collection sits anywhere below (this node included): the plan needs a loader.</summary>
    public bool LoadsAnything => IsCollection || Children.Any(c => c.LoadsAnything);
}

/// <summary>The C# text of a plan's reader with its to-one includes, of its collection loaders, and of the child plans those need.</summary>
internal sealed partial class PlanBuilder
{
    private readonly List<IncludeNode> _includes = [];

    private bool HasToOneIncludes => _includes.Any(n => !n.IsCollection);

    private bool HasLoads => _includes.Any(n => n.LoadsAnything) || _projection is { Collections.Count: > 0 };

    /// <summary>
    ///     Adds a path of hops under the root, sharing the nodes already there (two Includes through the same navigation join once).
    ///     ASSUMPTION: one filter per collection in a chain; a second Include through the same collection reuses the first node, filter included.
    /// </summary>
    private void AddIncludePath(IReadOnlyList<IncludeHop> hops)
    {
        var level = _includes;
        foreach (var hop in hops)
        {
            var node = level.FirstOrDefault(n => n.Property == hop.Property);
            if (node is null)
            {
                node = new IncludeNode(hop);
                level.Add(node);
            }

            level = node.Children;
        }
    }

    /// <summary>The joins of the to-one nodes (depth first, parent before child) and the columns they add to the select list.</summary>
    private void IncludeJoins(IEnumerable<IncludeNode> nodes, EntityModel parent, string parentAlias, List<string> columns)
    {
        foreach (var node in nodes.Where(n => !n.IsCollection))
        {
            var alias = AliasOf(node, parentAlias);
            Merge(_joins, [parent.Join(node.Hop.Navigation!, node.Target, parentAlias, alias) + MarkJoin(node.Target, alias)]); // a target the caller cannot see: no row (required), null (optional)
            columns.AddRange(node.Target.Columns.Select(c => c.Select(alias)));
            IncludeJoins(node.Children, node.Target, alias, columns);
        }
    }

    private static string AliasOf(IncludeNode node, string parentAlias) => parentAlias == "t" ? "j_" + node.Property : parentAlias + "_" + node.Property;

    private string IncludeReader()
    {
        var text = new StringBuilder();
        text.Append("static (r, d) => { var e = EntityMaps.Read_").Append(SqlInterceptorGenerator.Safe(_entity)).Append("(r, d);");
        var ordinal = _entity.Columns.Count;
        foreach (var node in _includes.Where(n => !n.IsCollection))
            text.Append(" e.").Append(node.Property).Append(" = ").Append(Hydrate(node, ref ordinal)).Append(';');
        text.Append(" return e; }");
        return text.ToString();
    }

    /// <summary>The included object read from its columns (null when the join found nothing), its own to-one children nested in the initializer; the ordinals follow the select list's depth-first order.</summary>
    private static string Hydrate(IncludeNode node, ref int ordinal)
    {
        var target = node.Target;
        var start = ordinal;
        ordinal += target.Columns.Count;
        var keyOrdinal = start + Math.Max(0, target.Columns.FindIndex(c => c.Column == target.KeyColumn));
        var children = new List<string>();
        foreach (var child in node.Children.Where(c => !c.IsCollection))
            children.Add($"{child.Property} = {Hydrate(child, ref ordinal)}");
        return $"(r.IsDBNull({keyOrdinal}) ? null : {target.Construct("r", "d", i => (start + i).ToString(), string.Join(", ", children))})";
    }

    /// <summary>The reader lambda (or method group) that shapes one row of this statement: the entity, with its to-one includes nested; a projection; an aggregate's value; null for a scalar.</summary>
    public string ReaderExpression()
        => _returning is not null ? (_returning.Construct.Length == 0 ? $"EntityMaps.Read_{SqlInterceptorGenerator.Safe(_entity)}" : $"static (r, d) => {_returning.Construct}")
            : IsScalar && !IsAggregate ? "null"
            : IsAggregate ? $"static (r, d) => {_aggregateReader}"
            : _projection is not null ? $"static (r, d) => {_projection.Construct}"
            : !HasToOneIncludes ? $"EntityMaps.Read_{SqlInterceptorGenerator.Safe(_entity)}"
            : IncludeReader();

    // Collection includes: one companion statement per collection, in the same command, read from the result sets after the rows'
    // own and assigned in memory. A collection under an included to-one object is loaded for the objects the rows carry; a collection
    // under a collection is read after its parent's rows, which are its parents. Statements and loader walk the tree in the same order.

    /// <summary>The companion statement of every collection node under <paramref name="nodes" />, depth first, registered on the root in reading order.</summary>
    private void BuildChildren(List<IncludeNode> nodes, PlanBuilder parentStatement, string parentAlias, EntityModel keyOwner)
    {
        foreach (var node in nodes)
        {
            if (node.IsCollection)
            {
                var child = node.Target;
                var collection = node.Hop.Collection!;
                var foreignKey = collection.ForeignKeyColumn(child);
                // A composite parent: its statement selects its key columns as k0, k1, …, matched one by one against the child's foreign keys.
                var parentKey = collection.IsComposite ? string.Join(", ", keyOwner.Keys.Select((k, i) => $"{parentAlias}.\"{k.Column}\" AS k{i}")) : $"{parentAlias}.\"{keyOwner.KeyColumn}\"";
                var builder = new PlanBuilder(child, parentStatement, parentKey, foreignKey, collection.LinkJoin(child, "t", "t_l")) { _foreignKeys = collection.IsComposite ? collection.ForeignKeyColumns(child) : null };
                if (node.Hop.Filter is { } filter && node.Hop.Model is { } model)
                    builder.Predicate(model, filter, null); // validated at the Include call site: columns and constants
                builder._withoutFilters = _withoutFilters; // the root's WithoutFilters() reads past its includes' filters too, as past its joins'
                if (!builder.ApplyRules(Location.None, null))
                    _includeProblem ??= $"'{node.Property}': {builder.StepError}"; // a rule the companion cannot carry: refused, not dropped
                builder._orderings.AddRange(node.Hop.Orderings);
                builder._perParentTake = node.Hop.Take;
                if (node.Hop.Take is not null && builder._joins.Count > 0)
                    _includeProblem ??= $"'{node.Property}' is taken per parent but a visibility rule of {child.Type.Name} joins another table: the window takes the child's own columns; filter in memory, or WithoutFilters()";
                builder._includes.AddRange(node.Children);
                var columns = child.Columns.Select(c => c.Select("t")).ToList();
                builder.IncludeJoins(builder._includes, child, "t", columns);
                if (collection.IsLink)
                {
                    // The parent's key from the join table, last (every column of a composite one): the child carries none, the loader groups on it.
                    var first = columns.Count;
                    columns.AddRange(collection.ForeignKeys.Select(fk => $"t_l.\"{fk}\""));
                    builder._linkKeyReader = collection.IsComposite
                        ? "(" + string.Join(", ", keyOwner.Keys.Select((k, i) => k.Read("r", (first + i).ToString(), "d"))) + ")"
                        : keyOwner.Keys[0].Read("r", first.ToString(), "d");
                }

                builder._selectList = string.Join(", ", columns);
                Root._children.Add(builder);
                BuildChildren(node.Children, builder, "t", child);
            }
            else
            {
                BuildChildren(node.Children, parentStatement, AliasOf(node, parentAlias), node.Target);
            }
        }
    }

    /// <summary>The loader: reads the companion statements' result sets in <see cref="BuildChildren" />'s order and hangs the children on their parents.</summary>
    private string Loader()
    {
        if (_projection is { Collections.Count: > 0 })
            return ProjectedLoader();
        var text = new StringBuilder();
        text.Append($"static async (global::System.Data.Common.DbDataReader r, SqlDialect d, global::System.Collections.Generic.List<{_entity.FullName}> parents, global::System.Threading.CancellationToken ct) =>").AppendLine();
        text.AppendLine("            {");
        var statement = 0;
        Loads(text, _includes, _entity, "parents", "", ref statement);
        text.Append("            }");
        return text.ToString();
    }

    private void Loads(StringBuilder text, List<IncludeNode> nodes, EntityModel parent, string parents, string prefix, ref int statement)
    {
        var key = parent.KeyProperty;
        var keyType = parent.Keys.FirstOrDefault()?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Guid";
        foreach (var node in nodes)
        {
            var name = prefix + node.Property;
            if (node.IsCollection)
            {
                var child = node.Target;
                var companion = _children[statement++];
                text.AppendLine("                await r.NextResultAsync(ct).ConfigureAwait(false);");
                text.AppendLine($"                global::System.Func<global::System.Data.Common.DbDataReader, SqlDialect, {child.FullName}> {name}_read = {companion.ReaderExpression()};");
                text.AppendLine($"                var {name}_rows = new global::System.Collections.Generic.List<{child.FullName}>();");
                var hop = node.Hop.Collection!;
                // A composite parent's children grouped on the tuple of their foreign keys, found by the tuple of the parent's key.
                var groupType = hop.IsComposite ? "(" + string.Join(", ", parent.Keys.Select(k => k.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")" : keyType;
                var childKey = hop.IsComposite ? "(" + string.Join(", ", hop.ForeignKeys.Select(fk => "child." + fk)) + ")" : "child." + hop.ForeignKey;
                var parentKey = hop.IsComposite ? "(" + string.Join(", ", parent.Keys.Select(k => "parent." + k.Property)) + ")" : "parent." + key;
                text.AppendLine($"                var {name}_groups = new global::System.Collections.Generic.Dictionary<{groupType}, global::System.Collections.Generic.List<{child.FullName}>>();");
                if (companion._linkKeyReader is { } linkKey)
                {
                    // Through a join table: the parent's key is the row's last column, read beside the child.
                    text.AppendLine($"                while (await r.ReadAsync(ct).ConfigureAwait(false)) {{ var child = {name}_read(r, d); {name}_rows.Add(child); var key = {linkKey}; if (!{name}_groups.TryGetValue(key, out var group)) {name}_groups[key] = group = new(); group.Add(child); }}");
                }
                else
                {
                    text.AppendLine($"                while (await r.ReadAsync(ct).ConfigureAwait(false)) {name}_rows.Add({name}_read(r, d));");
                    text.AppendLine($"                foreach (var child in {name}_rows) {{ if (!{name}_groups.TryGetValue({childKey}, out var group)) {name}_groups[{childKey}] = group = new(); group.Add(child); }}");
                }

                text.AppendLine($"                foreach (var parent in {parents}) parent.{node.Property} = {name}_groups.TryGetValue({parentKey}, out var {name}_mine) ? {name}_mine : new();");
                Loads(text, node.Children, child, $"{name}_rows", name + "_", ref statement);
            }
            else if (node.LoadsAnything)
            {
                text.AppendLine($"                var {name}_parents = new global::System.Collections.Generic.List<{node.Target.FullName}>({parents}.Count);");
                text.AppendLine($"                foreach (var parent in {parents}) if (parent.{node.Property} is {{ }} {name}_one) {name}_parents.Add({name}_one);");
                Loads(text, node.Children, node.Target, $"{name}_parents", name + "_", ref statement);
            }
        }
    }
}
