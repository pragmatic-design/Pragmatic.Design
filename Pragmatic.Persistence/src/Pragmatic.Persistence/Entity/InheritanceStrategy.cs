namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Specifies the inheritance mapping strategy for an entity hierarchy.
/// </summary>
public enum InheritanceStrategy
{
    /// <summary>
    ///     Table Per Hierarchy - all types share one table with a discriminator column.
    ///     Best for small hierarchies with similar data. Most performant for queries.
    /// </summary>
    Tph,

    /// <summary>
    ///     Table Per Type - each type in the hierarchy gets its own table.
    ///     Best for hierarchies with different data per type.
    /// </summary>
    Tpt,

    /// <summary>
    ///     Table Per Concrete Class - only concrete classes get their own tables.
    ///     Best when you rarely query across the hierarchy.
    /// </summary>
    Tpc
}
