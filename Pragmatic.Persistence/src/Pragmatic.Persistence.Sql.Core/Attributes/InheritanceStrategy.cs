namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>Where a hierarchy's rows live.</summary>
public enum InheritanceStrategy
{
    /// <summary>Table per hierarchy: one table, a discriminator column, the derived types' columns nullable.</summary>
    Tph = 0,

    /// <summary>Table per concrete type: one table per derived type; the root is a view over them.</summary>
    Tpc = 1,

    /// <summary>
    ///     Table per type: the root's [Table] holds the root's columns for every row, each derived type's [Table] its key and its own columns;
    ///     reads go through the view <c>{RootTable}View</c> (the root LEFT JOIN each derived table, the discriminator computed from which one
    ///     holds the row), a write of a derived row is two statements in one command. One level of derived types.
    /// </summary>
    Tpt = 2,
}
