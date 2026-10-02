namespace Pragmatic.Persistence.Query;

/// <summary>
///     Specifies the loading profile for a query, controlling navigation depth and includes.
/// </summary>
/// <typeparam name="T">The root entity type being loaded.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LoadWithAttribute<T> : Attribute
    where T : class
{
    /// <summary>
    ///     Maximum depth of navigation properties to include. 0 = no navigations.
    /// </summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>
    ///     Whether to use split queries for collection navigations.
    /// </summary>
    public bool SplitQuery { get; init; }
}
