namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a nested DTO property as a filter group with specified logic.
///     Creates parentheses in the generated expression.
/// </summary>
/// <remarks>
///     <para>
///         The nested DTO must also have <see cref="FilterDtoAttribute{TEntity}" />.
///     </para>
///     <para>
///         Properties within the group are combined with the specified logic.
///         The group itself is combined with AND to the parent filters.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [FilterDto&lt;User&gt;]
///     public partial class UserFilterDto
///     {
///         [Filter]
///         public string? Name { get; init; }
///
///         // Creates: (Name = X) AND ((Email LIKE Y) OR (Username LIKE Y))
///         [FilterGroup(FilterLogic.Or)]
///         public TextSearchFilter? Search { get; init; }
///     }
///
///     [FilterDto&lt;User&gt;]
///     public partial class TextSearchFilter
///     {
///         [Filter(Operator = FilterOperator.Contains)]
///         public string? Email { get; init; }
///
///         [Filter(Operator = FilterOperator.Contains)]
///         public string? Username { get; init; }
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FilterGroupAttribute : Attribute
{
    /// <summary>
    ///     Creates a filter group with the specified logic.
    /// </summary>
    /// <param name="logic">The logic to use for combining filters in this group.</param>
    public FilterGroupAttribute(FilterLogic logic = FilterLogic.And)
    {
        Logic = logic;
    }

    /// <summary>
    ///     The logic for combining filters within this group.
    /// </summary>
    public FilterLogic Logic { get; }
}
