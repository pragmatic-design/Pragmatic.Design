namespace Pragmatic.Persistence.Entity;

/// <summary>How a <see cref="RollUpAttribute{TChild}"/> aggregates a child property onto the parent.</summary>
public enum RollUpAggregation
{
    /// <summary>Sum of the child property across the parent's children.</summary>
    Sum = 0,

    /// <summary>Count of the parent's children.</summary>
    Count = 1
}
