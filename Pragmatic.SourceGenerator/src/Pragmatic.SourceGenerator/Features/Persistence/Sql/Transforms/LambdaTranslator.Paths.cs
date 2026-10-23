using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Member paths on the lambda parameter, to any depth: <c>o.Total</c> a column, <c>o.Address.City</c> an owned member,
///     <c>o.Customer.Region.Name</c> two joins (<c>j_Customer</c>, <c>j_Customer_Region</c>), <c>o.Customer</c> alone its foreign key,
///     <c>o.Lines.Count</c> a correlated count; and the collection predicates <c>o.Lines.Any(l => …)</c>, <c>All</c>, <c>Count()</c>
///     as correlated subqueries with a nested translator over the child, sharing this one's parameters. The alias of the
///     row is <c>t</c> at the root and <c>c1</c>, <c>c2</c>… inside subqueries.
/// </summary>
internal sealed partial class LambdaTranslator
{
    private int _depth; // nesting of correlated subqueries: the child's alias is c{depth}

    /// <summary>A nested translator over a child entity inside a correlated subquery: its own alias and joins, this one's parameters, pieces and notes.</summary>
    private LambdaTranslator(LambdaTranslator parent, EntityModel child, IParameterSymbol parameter, string alias)
    {
        _model = parent._model;
        _entities = parent._entities;
        _row = new EntityRow(child, alias);
        _parameter = parameter;
        _args = parent._args;
        _inline = parent._inline;
        _parameterPaths = parent._parameterPaths;
        _parameterKinds = parent._parameterKinds;
        _parameterTexts = parent._parameterTexts;
        _parameterIsConstant = parent._parameterIsConstant;
        DialectPieces = parent.DialectPieces;
        Emulations = parent.Emulations;
        InlineConstants = parent.InlineConstants;
        MarkJoin = parent.MarkJoin;
        _depth = parent._depth + 1;
    }

    /// <summary>
    ///     The plan's mark on a navigation's join (the target and the join's alias in, the text appended to the ON clause out): the plan resolves it
    ///     into the target's rules once the chain is complete. Null outside a plan: the join carries nothing.
    /// </summary>
    public System.Func<EntityModel, string, string>? MarkJoin { get; set; }

    private static string AliasOf(string parentAlias, string property) => parentAlias == "t" ? "j_" + property : parentAlias + "_" + property;

    /// <summary>The member names from the parameter outwards (<c>o.Customer.Name</c> → Customer, Name); null when the chain is not rooted in the parameter (or, with <paramref name="joined" />, in the joined one).</summary>
    private List<string>? Segments(MemberAccessExpressionSyntax member, bool joined = false) => Segments(member, out _, joined);

    /// <summary>The same, with the derived type each segment's receiver is cast to (<c>((Derived)x.Nav).Own</c>: Own's receiver cast to Derived), null where it is not.</summary>
    private List<string>? Segments(MemberAccessExpressionSyntax member, out List<INamedTypeSymbol?> casts, bool joined = false)
    {
        var segments = new List<string>();
        casts = [];
        ExpressionSyntax node = member;
        while (Strip(node) is MemberAccessExpressionSyntax access)
        {
            segments.Insert(0, access.Name.Identifier.ValueText);
            casts.Insert(0, CastOf(access.Expression));
            node = access.Expression;
        }

        var root = joined ? _joined?._parameter : _parameter;
        return Strip(node) is IdentifierNameSyntax identifier && _model.GetSymbolInfo(identifier).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, root) ? segments : null;
    }

    private GroupRow? _lastGroup; // the GroupJoin's group a path ended on (ShapeOf hands it to the SelectMany)

    /// <summary>
    ///     Where a member path enters an entity: the shape's composite members consumed (<c>x.Order</c> → the Order row), then the entity,
    ///     its alias, the index of the first segment left (a column, a navigation…) and whether the row may be absent. Null with the
    ///     error when the path ends inside a composite or goes below a rendered column; <paramref name="columnSql" /> when it ends on one.
    /// </summary>
    private (EntityModel Entity, string Alias, int Index, bool Optional, EntityRow Row)? EntityAt(List<string> segments, SyntaxNode where, out string? columnSql)
    {
        columnSql = null;
        _lastGroup = null;
        var shape = _row;
        var i = 0;
        while (true)
        {
            switch (shape)
            {
                case GroupRow group:
                    if (i == segments.Count)
                    {
                        _lastGroup = group;
                        return null;
                    }

                    Fail(where, "a GroupJoin's group has no members: read it with a SelectMany (x => x.Group.DefaultIfEmpty())");
                    return null;
                case CompositeRow composite:
                    if (i == segments.Count)
                    {
                        Fail(where, "a composite row is not a value: read one of its members");
                        return null;
                    }

                    shape = composite.Member(segments[i]);
                    if (shape is null)
                    {
                        Fail(where, $"'{segments[i]}' is not a member of the joined row");
                        return null;
                    }

                    i++;
                    continue;
                case ColumnRow column:
                    if (i != segments.Count)
                    {
                        Fail(where, $"'{segments[i]}' is read off a value the join already projected: project the column itself instead");
                        return null;
                    }

                    columnSql = column.Sql;
                    LastColumn = null;
                    LastColumnOptional = false;
                    return null;
                default:
                    var entity = (EntityRow)shape;
                    return (entity.Entity, entity.Alias, i, entity.Optional, entity);
            }
        }
    }

    private (EntityModel Entity, string Alias, int Index, bool Optional, EntityRow Row)? EntityAt(List<string> segments, SyntaxNode where) => EntityAt(segments, where, out _);

    private string? Column(MemberAccessExpressionSyntax member)
    {
        if (_groupKey is not null)
        {
            // The parameter is the grouping: its Key (a scalar key) or a member of its Key (a composite one) is a column; the row's columns are undefined after GROUP BY.
            if (Strip(member.Expression) is IdentifierNameSyntax && member.Name.Identifier.ValueText == "Key")
                return _groupKey.Column(null) ?? Fail(member, "the group key is composite: read one of its members (g.Key.Member)");
            if (Strip(member.Expression) is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Key" } keyAccess && Strip(keyAccess.Expression) is IdentifierNameSyntax)
                return _groupKey.Column(member.Name.Identifier.ValueText) ?? Fail(member, $"'{member.Name.Identifier.ValueText}' is not a member of the group key");
            return Fail(member, "after GroupBy a member is g.Key, g.Key.Member or an aggregate of the group (g.Count(), g.Sum(x => x.Col)); a column of the row is not defined");
        }

        var segments = Segments(member, out var casts);
        if (segments is null)
            return Fail(member, "not a member of the lambda parameter");
        return ColumnAt(segments, casts, member);
    }

    /// <summary>
    ///     An owned object read whole (a projection's member <c>x.Address</c>): each mapped member's column, the path extended with the member as a
    ///     range's bounds are; null when the path does not reach an owned object's columns.
    /// </summary>
    public List<(string Sql, ColumnModel Column)>? OwnedColumns(ExpressionSyntax expression, INamedTypeSymbol owned)
    {
        if (Strip(expression) is not MemberAccessExpressionSyntax member || !IsOnParameter(member) || Segments(member, out var casts) is not { } segments)
            return null;
        var columns = new List<(string, ColumnModel)>();
        foreach (var property in owned.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.GetMethod is not null && p.SetMethod is not null && p.DeclaredAccessibility == Accessibility.Public))
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue;
            if (ColumnAt([.. segments, property.Name], [.. casts, null], member) is not { } sql || LastColumn is not { Owner: not null } column)
                return null;
            columns.Add((sql, column));
        }

        return columns;
    }

    /// <summary>The column a member path resolves to (see <see cref="Column" />); the path as segments, so a caller may extend one it read off the syntax (a range's bounds).</summary>
    private string? ColumnAt(List<string> segments, List<INamedTypeSymbol?> casts, MemberAccessExpressionSyntax member)
    {
        var at = EntityAt(segments, member, out var rendered);
        if (at is null)
            return rendered; // a value the join already rendered, or a failure
        var (entity, alias, start, optional, row) = at.Value;
        LastColumnOptional = optional;
        if (start == segments.Count)
        {
            // The entity itself (x.Customer == null): its key stands for it.
            LastColumn = entity.Columns.FirstOrDefault(c => c.Column == entity.KeyColumn);
            return row.ColumnSql(LastColumn!);
        }

        if (row.Derived)
        {
            // A derived table exposes the entity's own columns as cN: one segment, a plain column.
            var exposed = start == segments.Count - 1 ? entity.Column(segments[start]) : null;
            if (exposed is null || exposed.Owner is not null)
                return Fail(member, "an inner chain with joins of its own is a derived table that exposes its own columns only: no navigation or owned member from the joined row there");
            LastColumn = exposed;
            return row.ColumnSql(exposed);
        }

        // Walk the hops: each one a navigation (a join under a nested alias), the last segment a column, an owned member, a navigation's key or a collection's Count.
        for (var i = start; i < segments.Count - 1; i++)
        {
            var model = Narrowed(entity, casts[i], member, out var narrowed);
            if (model is null)
                return null;

            // A JSON column: the segments below it are a path into the document (d.Spec.Pages), or an array's Count.
            if (model.Column(segments[i]) is { } jsonColumn && IsJsonConverter(jsonColumn.Converter))
                return JsonMember(alias, jsonColumn, segments, i + 1, member);

            // An owned type's member: a column of the current table, no join (o.Address.City; the owner the segment before the last).
            if (i == segments.Count - 2 && model.Column($"{segments[i]}.{segments[i + 1]}") is { Owner: not null } flattened)
            {
                LastColumn = flattened;
                if (alias == "t")
                    RootColumns.Add(flattened);
                return $"{alias}.\"{flattened.Column}\"";
            }

            // A collection's Count: a correlated count of the children (o.Lines.Count).
            if (i == segments.Count - 2 && segments[i + 1] == "Count" && model.Collection(segments[i]) is { } counted)
                return CollectionCount(member, counted, model, alias);

            var hopOptional = LastColumnOptional;
            // A comparison on the joined row (NULL compares false): the row must be there, so its join can be INNER.
            var target = Hop(model, ref alias, ref hopOptional, segments[i], narrowed ? casts[i]!.Name : null, member, wanted: _orDepth == 0 && _nullTest != NullTest.IsNull);
            if (target is null)
                return null;
            entity = target;
            LastColumnOptional = hopOptional;
        }

        var ending = Narrowed(entity, casts[segments.Count - 1], member, out _);
        if (ending is null)
            return null;
        entity = ending;
        var last = segments[segments.Count - 1];
        var column = entity.Column(last);
        LastColumn = column;
        if (column is not null)
        {
            if (alias == "t")
                RootColumns.Add(column);
            return $"{alias}.\"{column.Column}\"";
        }

        // A navigation on its own stands for its foreign key: `o.Customer != null` is `t."CustomerId" IS NOT NULL`.
        var asKey = entity.Navigation(last);
        if (asKey is null)
            return Fail(member, $"'{last}' is not a mapped column of {entity.Type.Name}");
        if (_orDepth == 0 && _nullTest == NullTest.IsNotNull)
            _innerAliases.Add(AliasOf(alias, last)); // the predicate asks for the target: its join, if any, can be INNER
        var key = entity.Column(asKey.ForeignKey);
        if (key is not null && alias == "t")
            RootColumns.Add(key);
        return $"{alias}.\"{key?.Column ?? asKey.ForeignKey}\"";
    }

    /// <summary>The collection a receiver names (<c>o.Lines</c>, <c>o.Customer.Orders</c>): the owner entity, its alias and the collection; null when it is not one.</summary>
    private (EntityModel Owner, string Alias, CollectionModel Collection)? CollectionOf(ExpressionSyntax receiver)
    {
        if (Strip(receiver) is not MemberAccessExpressionSyntax member || Segments(member, out var casts) is not { Count: > 0 } segments)
            return null;
        var at = EntityAt(segments, member);
        if (at is null || at.Value.Index >= segments.Count || at.Value.Row.Derived)
            return null;
        var (entity, alias, start, optional, _) = at.Value;
        for (var i = start; i < segments.Count - 1; i++)
        {
            var model = Narrowed(entity, casts[i], member, out var narrowed);
            var target = model is null ? null : Hop(model, ref alias, ref optional, segments[i], narrowed ? casts[i]!.Name : null, member, wanted: false);
            if (target is null)
                return null;
            entity = target;
        }

        var owner = Narrowed(entity, casts[segments.Count - 1], member, out _);
        return owner?.Collection(segments[segments.Count - 1]) is { } collection ? (owner, alias, collection) : null;
    }

    /// <summary>
    ///     One navigation hop of a path: the join to the target under the nested alias (<c>j_Nav</c>, <c>j_Nav_Nav</c>). Through a cast
    ///     (<paramref name="viaCast" /> the derived type's name) the navigation is the derived type's: the alias carries the type's name and
    ///     the join is LEFT whatever the key says, the row may be of another type. <paramref name="wanted" /> marks a join the predicate needs
    ///     a row for (it may turn INNER), never through a cast. The target's model, or null with the error.
    /// </summary>
    private EntityModel? Hop(EntityModel entity, ref string alias, ref bool optional, string segment, string? viaCast, SyntaxNode where, bool wanted)
    {
        var navigation = entity.Navigation(segment);
        if (navigation is null)
        {
            Fail(where, $"'{segment}' is not a navigation of {entity.Type.Name}");
            return null;
        }

        var target = _entities(navigation.Target);
        if (target is null)
        {
            Fail(where, $"{navigation.Target.Name} is not an [Entity] the generator could read");
            return null;
        }

        if (target.HasCompositeKey && navigation.ForeignKeys.Length != target.Keys.Count)
        {
            Fail(where, $"'{navigation.Target.Name}' has a composite key ({target.KeyList}): the navigation joins on one foreign key per key column ({segment}{{KeyProperty}} on {entity.Type.Name})");
            return null;
        }

        var next = AliasOf(alias, viaCast is null ? segment : $"{viaCast}_{segment}");
        var join = entity.Join(navigation, target, alias, next, optional: viaCast is not null) + (MarkJoin?.Invoke(target, next) ?? "");
        if (!_joins.Contains(join))
            _joins.Add(join);
        if (wanted && viaCast is null)
            _innerAliases.Add(next);
        optional |= !navigation.Required || viaCast is not null;
        alias = next;
        return target;
    }

    private string? CollectionCount(SyntaxNode where, CollectionModel collection, EntityModel owner, string ownerAlias)
    {
        var alias = "c" + (_depth + 1);
        var correlation = Correlation(owner, ownerAlias, collection, alias, out var child, out var linkJoin, where);
        if (correlation is null || child is null)
            return null;
        LastColumn = null;
        return $"(SELECT COUNT(*) FROM \"{child.Table}\" {alias}{(linkJoin is null ? "" : " " + linkJoin)} WHERE {correlation})";
    }

    /// <summary>
    ///     <c>Any()</c>, <c>Any(l => …)</c>, <c>All(l => …)</c>, <c>Count()</c>, <c>Count(l => …)</c> on a collection of the row: EXISTS / NOT EXISTS / a
    ///     correlated COUNT over the child's table, the inner predicate translated by a nested translator under the child's alias.
    /// </summary>
    private string? CollectionPredicate(InvocationExpressionSyntax invocation, IMethodSymbol method, string path)
    {
        var receiver = method.ReducedFrom is not null ? ((MemberAccessExpressionSyntax)invocation.Expression).Expression : invocation.ArgumentList.Arguments[0].Expression;
        var located = CollectionOf(receiver);
        if (located is null)
            return Fail(invocation, $"'{receiver}' is not a collection of the row ({method.Name} works on a collection navigation: o.Lines.Any(l => …))");
        var (owner, ownerAlias, collection) = located.Value;
        var alias = "c" + (_depth + 1);
        var correlation = Correlation(owner, ownerAlias, collection, alias, out var child, out var linkJoin, invocation);
        if (correlation is null || child is null)
            return null;

        var lambdaArgument = method.ReducedFrom is not null ? (invocation.ArgumentList.Arguments.Count > 0 ? invocation.ArgumentList.Arguments[0].Expression : null) : invocation.ArgumentList.Arguments.Count > 1 ? invocation.ArgumentList.Arguments[1].Expression : null;
        string? inner = null;
        var joins = new List<string>();
        if (lambdaArgument is LambdaExpressionSyntax lambda)
        {
            if (!SqlInterceptorGenerator.TryParameter(_model, lambda, out var parameter))
                return Fail(lambda, "the predicate takes one parameter, the child row");
            var nested = new LambdaTranslator(this, child, parameter, alias) { _outer = this }; // the rows of every level above stay readable, as in a written subquery
            var argumentIndex = method.ReducedFrom is not null ? 0 : 1;
            inner = nested.Translate(SqlInterceptorGenerator.Body(lambda), $"Captured.Lambda(Captured.Argument({path}, {argumentIndex}))", asPredicate: true);
            if (inner is null)
                return Fail(lambda, nested.Error ?? "the inner predicate is outside the translated subset");
            joins = nested._joins;
        }
        else if (lambdaArgument is not null)
        {
            return Fail(invocation, "the predicate must be a lambda written here");
        }

        LastColumn = null;
        var from = $"FROM \"{child.Table}\" {alias}{(linkJoin is null ? "" : " " + linkJoin)}{string.Concat(joins.Select(j => " " + j))}";
        return method.Name switch
        {
            "Any" => $"EXISTS (SELECT 1 {from} WHERE {correlation}{(inner is null ? "" : $" AND ({inner})")})",
            "All" => inner is null ? Fail(invocation, "All takes a predicate") : $"NOT EXISTS (SELECT 1 {from} WHERE {correlation} AND NOT ({inner}))",
            _ => $"(SELECT COUNT(*) {from} WHERE {correlation}{(inner is null ? "" : $" AND ({inner})")})",
        };
    }
}
