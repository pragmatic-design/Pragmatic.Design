namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a GridFilter or Query property as a cross-property search field.
///     The filter value is matched against ALL specified entity properties with OR logic.
/// </summary>
/// <remarks>
///     <para>
///         Designed for "search box" scenarios where a single input should search across
///         multiple entity fields (e.g., name, email, description).
///     </para>
///     <example>
///         <code>
///         [GridFilter&lt;Guest&gt;]
///         public partial class GuestGridFilter
///         {
///             [SearchAcross(nameof(Guest.FirstName), nameof(Guest.LastName), nameof(Guest.Email))]
///             public string? Search { get; set; }
///         }
///
///         // Generates:
///         // query.Where(e =&gt;
///         //     e.FirstName.Contains(this.Search!) ||
///         //     e.LastName.Contains(this.Search!) ||
///         //     e.Email.Contains(this.Search!));
///         </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SearchAcrossAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance with the entity property names to search across.
    /// </summary>
    /// <param name="propertyNames">Entity property names to include in the OR search.</param>
    public SearchAcrossAttribute(params string[] propertyNames)
    {
        PropertyNames = propertyNames;
    }

    /// <summary>
    ///     The entity property names this search field matches against.
    /// </summary>
    public string[] PropertyNames { get; }

    /// <summary>
    ///     Whether the search ignores case: both sides are lowered before the comparison.
    /// </summary>
    /// <remarks>
    ///     Off by default, because it was not there before and a grid that searches as written keeps
    ///     doing so. On PostgreSQL <c>Contains</c> compares as written, so a search box usually wants it.
    ///     Lowering a column keeps an ordinary index on it from being used.
    /// </remarks>
    public bool IgnoreCase { get; init; }
}
