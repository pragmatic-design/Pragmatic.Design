using System.Collections;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a collection or string is not empty.
/// </summary>
/// <remarks>
///     <para>
///         For strings: validates that the string has at least one character.
///         For collections: validates that the collection has at least one element.
///         For arrays: validates that the array has at least one element.
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
///     [NotEmpty]  // Must have at least one item
///     public List&lt;OrderItem&gt; Items { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.notempty";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => true, // Null check is [Required]'s job
            string s => s.Length > 0,
            Array arr => arr.Length > 0,
            ICollection col => col.Count > 0,
            IEnumerable enumerable => EnumerableHelper.HasAny(enumerable),
            _ => true
        };
    }
}