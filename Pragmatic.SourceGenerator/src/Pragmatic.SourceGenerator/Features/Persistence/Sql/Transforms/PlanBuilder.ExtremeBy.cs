using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     <c>MaxByAsync</c> and <c>MinByAsync</c>, as EF Core translates them: the chain ordered by the key (descending for MaxBy), one row, read
///     as <c>FirstOrDefaultAsync</c> reads it. The entity's key follows the key in the ordering, so rows that tie on it read the same row on
///     every run and every dialect.
/// </summary>
internal sealed partial class PlanBuilder
{
    /// <summary>True for the two terminals; the plan they run is <c>FirstOrDefaultAsync</c>'s.</summary>
    public static bool IsExtremeBy(string terminal) => terminal is "MaxByAsync" or "MinByAsync";

    /// <summary>The ordering the terminal stands for. False with <see cref="StepError" /> set, or when the key was reported at its own site.</summary>
    public bool AddExtremeBy(string terminal, InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model)
    {
        if (_projection is not null || _groupKey is not null || _members is not null || _raw is not null || _rawFrom is not null || _rowOpen || _row is not (null or EntityRow) || _entity.Keys.Count == 0)
        {
            StepError = $"{terminal} reads the rows of an entity with a key: put it on the entity's chain, before a Select, a join, a group or a set operation";
            return false;
        }

        if (_hasSkip || _hasTake || _distinct || _cursorParameters is not null)
        {
            StepError = $"{terminal} orders the rows the chain selects and reads the first: a Skip, Take, Distinct or cursor before it would page rows in another order";
            return false;
        }

        if (!ChainStep(new SqlInterceptorGenerator.Step(terminal == "MaxByAsync" ? "OrderByDescending" : "OrderBy", invocation, method), model))
        {
            StepError ??= $"the key of {terminal} is outside the translated subset: a column, a navigation's column or an aggregate of a collection";
            return false;
        }

        var row = (EntityRow)Row;
        foreach (var key in _entity.Keys)
        {
            var column = row.ColumnSql(key);
            _orderings.Add(column);
            RecordOrdering(key, column, descending: false, reset: false);
        }

        return true;
    }
}
