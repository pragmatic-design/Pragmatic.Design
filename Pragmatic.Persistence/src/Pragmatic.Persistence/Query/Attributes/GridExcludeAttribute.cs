namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Excludes a property from the grid: read on a <c>[GridAdapter]</c> class, and on an entity
///     carrying <see cref="GenerateGridBridgeAttribute" />.
/// </summary>
/// <remarks>
///     On an entity it takes back a property that <see cref="FilterableAttribute" /> declared — the
///     subtraction from an allowlist, not a denylist of its own. A property that declares nothing was
///     never in the bridge and does not need excluding.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GridExcludeAttribute : Attribute
{
    /// <summary>
    ///     Creates a grid field exclusion.
    /// </summary>
    /// <param name="property">The entity property name to exclude.</param>
    public GridExcludeAttribute(string property)
    {
        Property = property;
    }

    /// <summary>
    ///     The entity property name to exclude.
    /// </summary>
    public string Property { get; }
}
