using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a collection does not exceed a maximum number of elements.
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
/// public partial record CreateTagsRequest
/// {
///     [MaxCount(10)]  // Maximum 10 tags allowed
///     public List&lt;string&gt; Tags { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MaxCountAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the specified maximum count.
    /// </summary>
    /// <param name="count">The maximum number of elements allowed.</param>
    public MaxCountAttribute(int count)
    {
        Ensure.Ensure.ThrowIfNegative(count);
        Count = count;
    }

    /// <summary>
    ///     Gets the maximum number of elements allowed.
    /// </summary>
    public int Count { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.maxcount";

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

        return elementCount <= Count;
    }

}