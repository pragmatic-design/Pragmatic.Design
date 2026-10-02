namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a property as a sort option in a <see cref="QueryAttribute{TEntity,TResult}" /> or
///     <see cref="GridFilterAttribute{TEntity}" /> class.
///     The generator will auto-generate sorting logic based on this attribute.
/// </summary>
/// <remarks>
///     <para>
///         This attribute unifies sorting for both Query and GridFilter contexts:
///         <list type="bullet">
///             <item>Dynamic sort: nullable <c>SortDirection?</c> property — user controls direction</item>
///             <item>Fixed sort: set <see cref="DefaultDirection" /> + <see cref="Priority" /></item>
///             <item>Multi-sort: multiple [Sort] properties, ordered by <see cref="Priority" /></item>
///         </list>
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Works in both [Query] and [GridFilter] contexts:
///     [Query&lt;Order, OrderDto&gt;]
///     public partial class GetOrders
///     {
///         // Dynamic sort (user chooses direction)
///         [Sort]
///         public SortDirection? OrderNumberSort { get; init; }
///
///         // Fixed default sort (always applied as secondary sort)
///         [Sort(Priority = 1, DefaultDirection = SortDirection.Descending)]
///         public SortDirection? CreatedAtSort { get; init; }
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SortAttribute : Attribute
{
    /// <summary>
    ///     The target property path on the entity for sorting.
    ///     If not specified, derives from the property name by removing "Sort" suffix.
    /// </summary>
    public string? MapTo { get; set; }

    /// <summary>
    ///     The default sort direction when the caller specifies none. Leave it unset for no default,
    ///     and the sort is applied only when the property carries a value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When set, this sort is always applied even if the property value is null. Combine with
    ///         <see cref="Priority" /> for multi-sort ordering.
    ///     </para>
    ///     <para>
    ///         ⚠️ A <c>SortDirection</c>, not an <c>int</c>: as an <c>int</c>, the example above — the
    ///         only documentation of this form, and what a reader sees on hover — would not compile
    ///         (<c>CS0266</c>), and the form that compiles would be a bare number that says nothing about
    ///         which direction it means.
    ///     </para>
    ///     <para>
    ///         ⚠️ Not <c>SortDirection?</c>, which is what this wants to be and what C# forbids: a
    ///         nullable value type is not a valid attribute argument (<c>CS0655</c>). So "unset" is the
    ///         out-of-range default below rather than <c>null</c>. An author never writes it — they
    ///         name a direction or leave the property alone — and the generator reads anything negative
    ///         as "no default".
    ///     </para>
    /// </remarks>
    public SortDirection DefaultDirection { get; set; } = (SortDirection)NoDefault;

    /// <summary>The value that means "the caller decides". Outside <see cref="SortDirection" /> on purpose.</summary>
    internal const int NoDefault = -1;

    /// <summary>
    ///     The sort priority (lower = higher priority, applied first).
    ///     Used when multiple sort properties are defined.
    ///     Default is 0.
    /// </summary>
    public int Priority { get; set; }
}
