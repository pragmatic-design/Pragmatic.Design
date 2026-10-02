namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a class as a filter DTO.
///     The source generator creates an ApplyFilter extension method that converts
///     the DTO properties to a LINQ expression.
/// </summary>
/// <typeparam name="TEntity">The entity type to filter.</typeparam>
/// <remarks>
///     <para>
///         Properties marked with <see cref="FilterAttribute" /> are converted to
///         filter conditions. Null properties are skipped (no filter applied).
///     </para>
///     <para>
///         Properties marked with <see cref="FilterGroupAttribute" /> create nested
///         filter groups with AND/OR logic.
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
///         [Filter(Operator = FilterOperator.Contains)]
///         public string? Email { get; init; }
///
///         [Filter]
///         public bool? IsActive { get; init; }
///
///         [FilterGroup(FilterLogic.Or)]
///         public SearchGroup? Search { get; init; }
///     }
///
///     // Usage:
///     var users = await db.Users
///         .ApplyFilter(filter)  // Generated extension
///         .ToListAsync();
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class FilterDtoAttribute<TEntity> : Attribute
    where TEntity : class
{
}
