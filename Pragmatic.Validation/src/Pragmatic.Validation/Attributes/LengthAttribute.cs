using System.Collections;
using System.Collections.Generic;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string or collection has a length/count within a specified range.
/// </summary>
/// <remarks>
///     <para>
///         Combines <see cref="MinLengthAttribute" /> and <see cref="MaxLengthAttribute" /> into a single attribute.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateUserRequest
/// {
///     [Length(2, 100)]  // Between 2 and 100 characters
///     public string Name { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LengthAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the specified length range.
    /// </summary>
    /// <param name="minimumLength">The minimum length/count required.</param>
    /// <param name="maximumLength">The maximum length/count allowed.</param>
    public LengthAttribute(int minimumLength, int maximumLength)
    {
        Ensure.Ensure.ThrowIfNegative(minimumLength);
        Ensure.Ensure.ThrowIfNegative(maximumLength);
        Ensure.Ensure.ThrowIfGreaterThan(minimumLength, maximumLength);

        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
    }

    /// <summary>
    ///     Gets the minimum length/count required.
    /// </summary>
    public int MinimumLength { get; }

    /// <summary>
    ///     Gets the maximum length/count allowed.
    /// </summary>
    public int MaximumLength { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.length";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        var length = value switch
        {
            null => -1, // Will pass (null check is [Required]'s job)
            string s => s.Length,
            Array arr => arr.Length,
            ICollection col => col.Count,
            IEnumerable enumerable => EnumerableHelper.Count(enumerable),
            _ => -1
        };

        if (length < 0)
            return true;
        return length >= MinimumLength && length <= MaximumLength;
    }
}