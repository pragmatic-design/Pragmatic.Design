using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property value equals another property value.
/// </summary>
/// <remarks>
///     <para>
///         Uses <see cref="object.Equals(object?, object?)" /> for comparison.
///         Both null values are considered equal.
///     </para>
///     <para>
///         Common use case: password confirmation fields.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record ChangePasswordRequest
/// {
///     [Required]
///     public string NewPassword { get; init; }
/// 
///     [Required]
///     [EqualTo(nameof(NewPassword))]
///     public string ConfirmPassword { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EqualToAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the name of the property to compare.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property to compare with.</param>
    public EqualToAttribute(string otherPropertyName)
    {
        ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    ///     Gets the name of the property to compare with.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.equalto";

    /// <inheritdoc />
    public override bool RequiresInstance => true;

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return true;
        // Requires instance
    }

    /// <inheritdoc />
    public override bool IsValid(object? value, object instance)
    {
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "EqualTo");
        return Equals(value, otherValue);
    }
}