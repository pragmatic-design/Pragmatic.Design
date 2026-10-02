namespace Pragmatic.Persistence.Query;

/// <summary>
///     Controls how the repository executes a query: tracking, filtering, and projection behavior.
/// </summary>
public enum QueryStrategy
{
    /// <summary>Projects directly into DTO — no entity tracking, best performance.</summary>
    Projection,

    /// <summary>Loads full entity — tracked, all filters applied.</summary>
    Entity,

    /// <summary>Loads entity with custom FilterMap applied.</summary>
    Filtered,

    /// <summary>Raw query — no filters, no tracking. Use with care.</summary>
    Raw
}
