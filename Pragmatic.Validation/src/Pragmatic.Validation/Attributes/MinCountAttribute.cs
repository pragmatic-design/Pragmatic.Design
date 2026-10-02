using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a collection has at least a minimum number of elements.
/// </summary>
/// <remarks>
///     <para>
///         Works with arrays, lists, and any <see cref="ICollection" /> or <see cref="IEnumerable" />.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record PlaceOrderRequest
/// {
///     [Required]
///     [MinCount(1)]  // At least one item required
///     public List&lt;OrderItem&gt; Items { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MinCountAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the specified minimum count.
    /// </summary>
    /// <param name="count">The minimum number of elements required.</param>
    public MinCountAttribute(int count)
    {
        Ensure.Ensure.ThrowIfNegative(count);
        Count = count;
    }

    /// <summary>
    ///     Gets the minimum number of elements required.
    /// </summary>
    public int Count { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.mincount";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var elementCount = value switch
        {
            Array arr => arr.Length,
            ICollection col => col.Count,
            IEnumerable enumerable => EnumerableHelper.Count(enumerable),
            _ => -1
        };

        if (elementCount < 0)
            return true;

        return elementCount >= Count;
    }

}