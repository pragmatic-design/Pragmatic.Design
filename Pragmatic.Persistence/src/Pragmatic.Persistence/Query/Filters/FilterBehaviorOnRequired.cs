namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Specifies how a filter behaves when encountering a required navigation.
/// </summary>
/// <remarks>
///     <para>
///         Required navigations are tricky because filtering them out
///         could leave orphaned records or break referential integrity.
///     </para>
/// </remarks>
public enum FilterBehaviorOnRequired
{
    /// <summary>
    ///     Filter the navigation itself.
    ///     May result in null reference if the related entity doesn't match.
    /// </summary>
    FilterNavigation,

    /// <summary>
    ///     Filter the parent entity if the required navigation doesn't match.
    ///     Safer option - excludes the entire entity if its required relation fails.
    /// </summary>
    FilterParent,

    /// <summary>
    ///     Skip filtering for this required navigation.
    ///     The filter is not applied to this relationship.
    /// </summary>
    Skip
}
