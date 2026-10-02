using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string or collection has at least a minimum length/count.
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
///     [MinLength(2)]
///     public string Name { get; init; }
/// 
///     [MinLength(1)]
///     public List&lt;string&gt; Tags { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MinLengthAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the specified minimum length.
    /// </summary>
    /// <param name="length">The minimum length/count required.</param>
    public MinLengthAttribute(int length)
    {
        Ensure.Ensure.ThrowIfNegative(length);
        Length = length;
    }

    /// <summary>
    ///     Gets the minimum length/count required.
    /// </summary>
    public int Length { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.minlength";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => true,
            string s => s.Length >= Length,
            Array arr => arr.Length >= Length,
            ICollection col => col.Count >= Length,
            IEnumerable enumerable => EnumerableHelper.Count(enumerable) >= Length,
            _ => true
        };
    }
}