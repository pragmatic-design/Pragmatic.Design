namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Marker interface for tenant-scoped query filters.
///     Applied by the source generator on generated TenantFilter classes.
///     Used by <see cref="DefaultQueryFilterProvider" /> to identify tenant filters
///     without reflection or string-based naming conventions.
/// </summary>
public interface ITenantFilter;
