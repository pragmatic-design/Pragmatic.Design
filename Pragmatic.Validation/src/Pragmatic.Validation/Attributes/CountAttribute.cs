using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a collection has a number of elements within a specified range.
/// </summary>
/// <remarks>
///     <para>
///         Combines <see cref="MinCountAttribute" /> and <see cref="MaxCountAttribute" /> into a single attribute.
///         Works with arrays, lists, and any <see cref="ICollection" /> or <see cref="IEnumerable" />.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateTeamRequest
/// {
///     [Required]
///     [Count(2, 10)]  // Between 2 and 10 team members
///     public List&lt;UserId&gt; Members { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class CountAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the specified count range.
    /// </summary>
    /// <param name="minimumCount">The minimum number of elements required.</param>
    /// <param name="maximumCount">The maximum number of elements allowed.</param>
    public CountAttribute(int minimumCount, int maximumCount)
    {
        Ensure.Ensure.ThrowIfNegative(minimumCount);
        Ensure.Ensure.ThrowIfNegative(maximumCount);
        Ensure.Ensure.ThrowIfGreaterThan(minimumCount, maximumCount);

        MinimumCount = minimumCount;
        MaximumCount = maximumCount;
    }

    /// <summary>
    ///     Gets the minimum number of elements required.
    /// </summary>
    public int MinimumCount { get; }

    /// <summary>
    ///     Gets the maximum number of elements allowed.
    /// </summary>
    public int MaximumCount { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.count";

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

        return elementCount >= MinimumCount && elementCount <= MaximumCount;
    }

}