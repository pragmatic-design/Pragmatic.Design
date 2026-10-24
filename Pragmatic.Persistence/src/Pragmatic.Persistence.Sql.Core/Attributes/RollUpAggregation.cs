namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>How a <see cref="RollUpAttribute{TChild}" /> aggregates the children.</summary>
public enum RollUpAggregation
{
    /// <summary>The sum of the named child property.</summary>
    Sum = 0,

    /// <summary>The number of children (the child property is ignored: pass an empty string).</summary>
    Count = 1,
}
