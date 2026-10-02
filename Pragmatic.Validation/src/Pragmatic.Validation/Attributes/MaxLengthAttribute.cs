using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string or collection does not exceed a maximum length/count.
/// </summary>
/// <remarks>
///     <para>
///         For strings: validates the character count.
///         For collections/arrays: validates the element count.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateUserRequest
/// {
///     [MaxLength(100)]
///     public string Name { get; init; }
/// 
///     [MaxLength(10)]
///     public List&lt;string&gt; Tags { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MaxLengthAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the specified maximum length.
    /// </summary>
    /// <param name="length">The maximum length/count allowed.</param>
    public MaxLengthAttribute(int length)
    {
        Ensure.Ensure.ThrowIfNegative(length);
        Length = length;
    }

    /// <summary>
    ///     Gets the maximum length/count allowed.
    /// </summary>
    public int Length { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.maxlength";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => true,
            string s => s.Length <= Length,
            Array arr => arr.Length <= Length,
            ICollection col => col.Count <= Length,
            IEnumerable enumerable => EnumerableHelper.Count(enumerable) <= Length,
            _ => true
        };
    }
}