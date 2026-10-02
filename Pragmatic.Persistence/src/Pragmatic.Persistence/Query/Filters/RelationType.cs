namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Describes the type of relationship for navigation context.
/// </summary>
public enum RelationType
{
    /// <summary>
    ///     No relation (root entity).
    /// </summary>
    None,

    /// <summary>
    ///     One-to-many relationship (collection navigation).
    /// </summary>
    OneToMany,

    /// <summary>
    ///     Many-to-one relationship (reference navigation).
    /// </summary>
    ManyToOne,

    /// <summary>
    ///     One-to-one relationship.
    /// </summary>
    OneToOne,

    /// <summary>
    ///     Many-to-many relationship.
    /// </summary>
    ManyToMany
}
