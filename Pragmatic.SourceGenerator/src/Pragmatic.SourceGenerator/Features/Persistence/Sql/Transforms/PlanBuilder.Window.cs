using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     <c>DistinctOn(key, order)</c>: one row per key, the first by the ordering. The chain so far (its filters, the entity's rules)
///     becomes a derived table that ranks the rows per key — <c>SELECT DISTINCT ON (key) t.*, 1 AS rn … ORDER BY key, order</c> on
///     PostgreSQL, <c>SELECT t.*, ROW_NUMBER() OVER (PARTITION BY key ORDER BY order) AS rn …</c> elsewhere — and the statement reads it
///     with <c>rn = 1</c>; what follows (ordering, paging, projection, includes, a count) works on the picks.
/// </summary>
internal sealed partial class PlanBuilder
{
    private bool AddDistinctOn(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        var arguments = step.Invocation.ArgumentList.Arguments;
        if (arguments.Count != 2 || arguments[0].Expression is not LambdaExpressionSyntax keyLambda || arguments[1].Expression is not LambdaExpressionSyntax orderLambda
            || !SqlInterceptorGenerator.TryParameter(model, keyLambda, out var keyParameter) || !SqlInterceptorGenerator.TryParameter(model, orderLambda, out var orderParameter))
        {
            StepError = "DistinctOn takes two lambdas over the row: the key, and the ordering (Sql.Desc for descending, Sql.By for several)";
            return false;
        }

        if (_raw is not null || _joins.Count > 0 || _rowOpen || _members is not null || _groupKey is not null || _projection is not null || _includes.Count > 0 || _orderings.Count > 0 || _distinct)
        {
            StepError = "DistinctOn picks rows of the entity: it goes after the filters and before the joins, the includes, the orderings and the projection";
            return false;
        }

        var key = WindowColumn(model, keyLambda, keyParameter);
        var order = WindowColumn(model, orderLambda, orderParameter);
        if (key is null || order is null)
            return false;
        if (!ApplyRules(step.Invocation.GetLocation(), null))
            return false; // the rules pick the candidates: inside the derived table, not on the picks

        var where = _predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", _predicates.Select(p => "(" + p + ")"));
        var from = $"FROM \"{_entity.Table}\" {_alias}{where}";
        var ranked = $"SELECT {_alias}.*, ROW_NUMBER() OVER (PARTITION BY {key} ORDER BY {order}) AS rn {from}";
        var distinctOn = $"SELECT DISTINCT ON ({key}) {_alias}.*, 1 AS rn {from} ORDER BY {key}, {order}";
        _dialectPieces.Add(new DialectPiece(ranked, distinctOn, ranked, ranked, ranked));
        _raw = "{d" + (_dialectPieces.Count - 1) + "}";
        _predicates.Clear();
        _predicates.Add($"{_alias}.rn = 1");
        return true;
    }

    /// <summary>A key or an ordering of the window: columns of the entity (no navigation: the derived table has the entity's columns), constants as literals.</summary>
    private string? WindowColumn(SemanticModel model, LambdaExpressionSyntax lambda, IParameterSymbol parameter)
    {
        var translator = Bound(new LambdaTranslator(model, _entities, Row, parameter) { InlineConstants = true });
        var column = translator.ColumnOf(SqlInterceptorGenerator.Body(lambda));
        if (column is null || translator.ParameterPaths.Count > 0)
        {
            StepError = translator.Error ?? "DistinctOn's key and ordering are columns of the entity (Sql.Desc, Sql.By)";
            return null;
        }

        if (translator.Joins.Count > 0)
        {
            StepError = "DistinctOn's key and ordering are columns of the entity, not of a navigation: the derived table holds the entity's columns only";
            return null;
        }

        return Absorb(translator, column, _kinds.Count);
    }
}
