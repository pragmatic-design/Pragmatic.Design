using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     An explicit join: <c>Join(inner, o => key, c => key, (o, c) => result)</c>. The inner chain is a builder of its own (this
///     plan's parameters and slots continued): the table under the alias <c>jN</c>, its predicates in the ON clause (a filter on the
///     inner side of a LEFT JOIN belongs to the join, not to the WHERE, and the key's index stays usable); a derived table exposing
///     its columns as <c>cN</c> only when it has joins of its own, which the ON clause could not reach. The keys are columns on each
///     side (several as an anonymous object or a tuple); the result selector is the shape of the row from here on. A GroupJoin
///     prepares the same join and leaves it pending in the row, for the SelectMany that reads the group.
/// </summary>
internal sealed partial class PlanBuilder
{
    private int _joinCount;

    private bool AddJoin(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        // Which side may be missing from a row: the inner one (LEFT, FULL), the outer one (RIGHT, FULL).
        var innerOptional = step.Name is "LeftJoin" or "FullJoin";
        var outerOptional = step.Name is "RightJoin" or "FullJoin";
        var arguments = step.Invocation.ArgumentList.Arguments;
        if (arguments.Count != 4 || step.Method.TypeArguments.Length != 3 || arguments[3].Expression is not LambdaExpressionSyntax resultLambda
            || !SqlInterceptorGenerator.TryParameters(model, resultLambda, out var resultOuter, out var resultInner) || resultInner is null)
            return Refuse("a join takes the inner chain (written in place, starting at session.Query<TInner>()), the two key selectors and the result selector over both rows ((o, c) => new { o.Number, c.Name })");
        if (outerOptional && !CanLoseTheOuterRow())
            return false;
        var pending = PrepareJoin(step, model, innerOptional);
        if (pending is null)
            return false;

        var outerRow = Row;
        if (outerOptional)
        {
            if (!JoinOuterOptional(pending, step.Name == "FullJoin"))
                return false;
            outerRow = new EntityRow(_entity, _alias, optional: true);
        }
        else
            _joins.Add(pending.Join(innerOptional));

        var translator = Bound(new LambdaTranslator(model, _entities, outerRow, resultOuter) { InlineConstants = true });
        translator.WithJoined(resultInner, new EntityRow(pending.Entity, pending.Alias, innerOptional, pending.Derived), innerOptional);
        if (!FinishJoinedRow(translator, resultLambda, step.Method.TypeArguments[2]))
            return false;
        // A navigation read off a side that may be missing would be joined after the outer join, and its INNER JOIN would drop the rows without that side.
        if ((outerOptional && translator.Joins.Count > 0) || (outerOptional && innerOptional && translator.JoinedJoins.Count > 0))
            return Refuse($"a {step.Name}'s result selector reads the columns of the two rows, not a navigation of a side that may be missing: join the navigation's entity in the chain of that side, or read it in a [Query]");
        return true;
    }

    /// <summary>
    ///     A RIGHT or FULL join may leave this chain's row out: it starts at the entity's own rows (not a previous join's, a set operation, a
    ///     hand-written statement or a named query), and keeps no cursor or runtime-composed filter, whose text the join would have to repeat.
    /// </summary>
    private bool CanLoseTheOuterRow()
    {
        if (_rowOpen || _row is not (null or EntityRow) || _members is not null || _raw is not null || _rawFrom is not null || _cte is not null || _entity.KeyColumn is null)
            return Refuse("a RIGHT or FULL join starts from the entity's own rows: one per chain, before any other join, on an entity with a key");
        if (_cursorParameters is not null || _predicateSlots.Count > 0 || _orderings.Any(o => o.StartsWith("{dyn:", System.StringComparison.Ordinal)))
            return Refuse("a RIGHT or FULL join does not follow After, Before, Matching or OrderedBy: filter with Where and order with OrderBy");
        return true;
    }

    /// <summary>
    ///     Joins with the outer row optional. This chain's filters and rules move into the ON clause, so a row that fails them matches nothing; a
    ///     RIGHT join drops the inner rows that fail the inner chain's in the WHERE (its inner row is always there); a FULL join keeps each side's
    ///     filters in the ON clause and, in the WHERE, applies each one unless that side is missing from the row (its key is null).
    /// </summary>
    private bool JoinOuterOptional(PendingJoin pending, bool full)
    {
        if (!ApplyRules(Location.None, null))
            return false; // the rules pick the outer rows that may match: they go into the join with the chain's filters
        var outerFilters = _predicates.Select(p => $"({p})").ToList();
        _predicates.Clear();
        var keys = pending.On.Take(pending.On.Count - pending.InnerFilters.Count);
        if (!full)
        {
            _joins.Add(pending.Join("RIGHT", keys.Concat(outerFilters)));
            _predicates.AddRange(pending.InnerFilters.Select(Unwrap));
            return true;
        }

        if (pending.InnerFilters.Count > 0 && pending.Entity.KeyColumn is null)
            return Refuse($"a FULL join tells an inner row that matched nothing by its key, and {pending.Entity.Type.Name} has none: filter the inner chain through a [Query] with a key");
        MarkFullJoin();
        _joins.Add(pending.Join("FULL", pending.On.Concat(outerFilters)));
        if (outerFilters.Count > 0)
            _predicates.Add($"{_alias}.\"{_entity.KeyColumn}\" IS NULL OR {string.Join(" AND ", outerFilters)}");
        if (pending.InnerFilters.Count > 0)
            _predicates.Add($"{new EntityRow(pending.Entity, pending.Alias).ColumnSql(pending.Entity.Columns.First(c => c.Column == pending.Entity.KeyColumn))} IS NULL OR {string.Join(" AND ", pending.InnerFilters)}");
        return true;

        static string Unwrap(string filter) => filter.Substring(1, filter.Length - 2);
    }

    /// <summary>A GroupJoin: the join prepared, the row's member for the group holding it until the SelectMany that reads it.</summary>
    private bool AddGroupJoin(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        var arguments = step.Invocation.ArgumentList.Arguments;
        if (arguments.Count != 4 || step.Method.TypeArguments.Length != 3 || arguments[3].Expression is not LambdaExpressionSyntax resultLambda
            || !SqlInterceptorGenerator.TryParameters(model, resultLambda, out var resultOuter, out var group) || group is null)
            return Refuse("a GroupJoin takes the inner chain, the two key selectors and a result selector over the row and the group ((o, g) => new { o, g }), followed by a SelectMany over the group");
        var pending = PrepareJoin(step, model, left: true);
        if (pending is null)
            return false;
        var translator = Bound(new LambdaTranslator(model, _entities, Row, resultOuter) { InlineConstants = true });
        translator.WithJoined(group, new GroupRow(pending), optional: false);
        var shape = translator.FromSelector(resultLambda, step.Method.TypeArguments[2]);
        if (shape is null)
            return Refuse($"the GroupJoin's result selector: '{translator.ErrorExpression ?? resultLambda.ToString()}' {translator.Error ?? "names the row and the group"}");
        if (!CompositeRow.HasGroup(shape))
            return Refuse("the GroupJoin's result selector keeps the group as a member ((o, g) => new { o, g }), for the SelectMany that follows");
        Merge(_joins, translator.Joins);
        _row = shape;
        _rowOpen = true;
        _projection = null; // the row is not readable until the SelectMany joins the group
        return true;
    }

    /// <summary>
    ///     The join of an explicit Join/LeftJoin/GroupJoin step: the inner chain translated under the join's alias (its predicates in
    ///     the ON clause, or a derived table when it has joins of its own), the keys, the ON clause. Null with <see cref="StepError" />.
    /// </summary>
    private PendingJoin? PrepareJoin(SqlInterceptorGenerator.Step step, SemanticModel model, bool left)
    {
        var arguments = step.Invocation.ArgumentList.Arguments;
        if (step.Inner is null)
        {
            Refuse("the inner chain must be written in place, starting at session.Query<TInner>() or at a declared chain");
            return null;
        }

        if ((_projection is not null && !_rowOpen) || _groupKey is not null)
        {
            Refuse("a join works on rows (the entity's, or a previous join's): it comes before a Select or a GroupBy");
            return null;
        }

        if (_raw is not null)
        {
            Refuse("a hand-written statement is not joined: write the join in the statement"); // a table-valued function (_rawFrom) is: its source has the entity's columns
            return null;
        }

        if (arguments[1].Expression is not LambdaExpressionSyntax outerKeyLambda || arguments[2].Expression is not LambdaExpressionSyntax innerKeyLambda
            || !SqlInterceptorGenerator.TryParameter(model, outerKeyLambda, out var outerKeyParameter) || !SqlInterceptorGenerator.TryParameter(model, innerKeyLambda, out var innerKeyParameter))
        {
            Refuse("the key selectors take the row (o => o.CustomerId)");
            return null;
        }

        var innerEntity = step.Method.TypeArguments[0] is INamedTypeSymbol innerType ? _entities(innerType) : null;
        if (innerEntity is null || innerEntity.IsRawShape)
        {
            Refuse($"the inner chain's rows must be an [Entity] ({step.Method.TypeArguments[0].Name} is none)");
            return null;
        }

        var alias = "j" + (++_joinCount);
        var inner = new PlanBuilder(this, innerEntity) { _alias = alias };
        foreach (var innerStep in step.Inner)
        {
            // A With at its head (session.With(anchor, step)): a named query of this statement's WITH list, joined by name.
            var filtersOnly = (innerStep.Name == "With" && ReferenceEquals(innerStep, step.Inner[0])) || IsInnerFilter(innerStep.Name) || (innerStep.IsFragment && innerStep.Inner is { } body && body.All(s => IsInnerFilter(s.Name)));
            if (!filtersOnly)
            {
                Refuse($"the inner chain of a join filters its rows (Where, WithoutFilters, WithoutFilter<TRule>, IncludeDeleted, a declared chain of those) and nothing else ('{innerStep.Name}' found): project, order and page the joined result");
                return null;
            }

            if (inner.AddStep(innerStep, model))
                continue;
            StepError = inner.StepError ?? (inner.LastError is { } why ? $"{inner.LastErrorExpression}: {why}" : "the inner chain's predicate could not be translated");
            return null;
        }

        if (!inner.ApplyRules(Location.None, null))
        {
            StepError = $"the inner chain's {inner.StepError}"; // a rule left out would show the rows it hides
            return null;
        }
        _slot = inner._slot;
        var derived = inner._joins.Count > 0;

        // The keys: columns of each side, the inner's under its alias (a derived table's as cN).
        var keyType = step.Method.TypeArguments[1];
        var outerTranslator = Bound(new LambdaTranslator(model, _entities, Row, outerKeyParameter));
        var outerKey = GroupKey.TryRead(model, outerKeyLambda, keyType, outerTranslator, _joins, out var keyError);
        if (outerKey is null)
        {
            Refuse($"the outer key: {keyError}");
            return null;
        }

        var innerJoins = new List<string>();
        var innerKey = GroupKey.TryRead(model, innerKeyLambda, keyType, new LambdaTranslator(model, _entities, new EntityRow(innerEntity, alias, false, derived), innerKeyParameter), innerJoins, out keyError);
        if (innerKey is null)
        {
            Refuse($"the inner key: {keyError}");
            return null;
        }

        if (innerKey.Parts.Count != outerKey.Parts.Count)
        {
            Refuse("the two keys have the same members, in the same order");
            return null;
        }

        if (innerJoins.Count > 0)
        {
            Refuse("the inner key is a column of the inner row itself (a navigation's column would join a table the ON clause comes before)");
            return null;
        }

        var on = new List<string>(outerKey.Parts.Count);
        for (var i = 0; i < outerKey.Parts.Count; i++)
            on.Add($"{innerKey.Parts[i].Column} = {outerKey.Parts[i].Column}");

        string source;
        var innerFilters = new List<string>();
        if (inner._cte is not null)
        {
            if (derived)
            {
                Refuse("a joined With reads its rows as they are: no navigation after it in the inner chain");
                return null;
            }

            source = HoistWith(inner, alias); // the CTE by name; the filters after it in the join itself
            innerFilters.AddRange(inner._predicates.Select(p => $"({p})"));
        }
        else if (derived)
        {
            // The inner statement per dialect (its alias inside is the join's too), as a piece the plan resolves like a function's rendering.
            inner._terminal = "ToListAsync";
            var list = string.Join(", ", inner.MemberColumns().Select((c, i) => $"{c} AS c{i}"));
            _dialectPieces.Add(new DialectPiece(inner.Statement(SqlDialect.Sqlite, list), inner.Statement(SqlDialect.PostgreSql, list), inner.Statement(SqlDialect.SqlServer, list), inner.Statement(SqlDialect.MySql, list), inner.Statement(SqlDialect.Oracle, list)));
            source = "({d" + (_dialectPieces.Count - 1) + "})";
        }
        else
        {
            source = $"\"{innerEntity.Table}\"";
            innerFilters.AddRange(inner._predicates.Select(p => $"({p})")); // the inner's own filter, in the join itself: a LEFT JOIN keeps the unmatched rows
        }

        on.AddRange(innerFilters);
        return new PendingJoin(innerEntity, alias, source, on, derived, innerFilters);
    }

    /// <summary>The result selector as the shape of the row from here on: entities carried forward (never materialised between operators), values rendered; the reader built from it.</summary>
    private bool FinishJoinedRow(LambdaTranslator translator, LambdaExpressionSyntax resultLambda, ITypeSymbol resultType)
    {
        var shape = translator.FromSelector(resultLambda, resultType);
        if (shape is null)
            return Refuse($"the result selector: '{translator.ErrorExpression ?? resultLambda.ToString()}' {translator.Error ?? "is not a projection of the two rows"}");
        if (CompositeRow.HasGroup(shape))
            return Refuse("a GroupJoin's group is not a value: read it with a SelectMany before projecting");
        Merge(_joins, translator.Joins);
        Merge(_joins, translator.JoinedJoins);
        _row = shape;
        _rowOpen = true;
        _projection = SqlInterceptorGenerator.Projection.FromShape(shape);
        if (translator.DialectPieces.Count > 0)
        {
            var offset = _dialectPieces.Count;
            _projection.Columns[0] = Absorb(translator, _projection.Columns[0], _kinds.Count);
            for (var i = 1; i < _projection.Columns.Count; i++)
                _projection.Columns[i] = PieceToken.Replace(_projection.Columns[i], m => "{d" + (offset + int.Parse(m.Groups[1].Value)) + "}");
        }

        return true;
    }

    /// <summary>A step an inner chain takes: a filter of its rows, or a switch of the entity's own filters.</summary>
    private static bool IsInnerFilter(string name) => name is "Where" or "WithoutFilters" or "WithoutFilter" or "IncludeDeleted" or "AsNoTracking";

    /// <summary>The joins of the chain, for the census of a Join call site.</summary>
    public string JoinsSummary => string.Join(" ", _joins);
}
