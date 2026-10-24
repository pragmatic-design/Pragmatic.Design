namespace Pragmatic.Persistence.Sql.Filters;

/// <summary>What one filter of a statement does in one execution.</summary>
public enum SqlFilterState : byte
{
    /// <summary>The filter's predicate is in the statement.</summary>
    Active = 0,

    /// <summary>The filter is lifted: its predicate is left out.</summary>
    Lifted = 1,

    /// <summary>The entity's rows are not visible at all (an anonymous caller where a permission filter applies and fails closed).</summary>
    Closed = 2,
}
