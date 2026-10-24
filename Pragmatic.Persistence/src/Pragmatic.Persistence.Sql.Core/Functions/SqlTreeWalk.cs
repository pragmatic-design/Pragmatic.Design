namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     How a recursive With walks its tree (a constant, combined with <c>|</c>), on every dialect: the tree keeps the path of keys from the
///     anchor's row (<c>/k1/k2/</c>), which stops a cycle and orders the rows; the depth orders them level by level. The standard's CYCLE and
///     SEARCH clauses, which only PostgreSQL and Oracle have.
/// </summary>
[Flags]
public enum SqlTreeWalk
{
    None = 0,

    /// <summary>A step does not go back to a row already on its path: a graph with a cycle ends (CYCLE).</summary>
    StopAtCycles = 1,

    /// <summary>The rows in depth-first order: each row followed by its descendants, siblings by key (SEARCH DEPTH FIRST BY key).</summary>
    DepthFirst = 2,

    /// <summary>The rows level by level from the anchor's, by key within a level (SEARCH BREADTH FIRST BY key).</summary>
    BreadthFirst = 4,
}
