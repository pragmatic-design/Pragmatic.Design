namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property value does not equal another property value.
/// </summary>
/// <remarks>
///     <para>
///         Uses <see cref="object.Equals(object?, object?)" /> for comparison.
///         If either value is null, the comparison passes (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
///     <para>
///         Common use case: ensuring new password differs from current password.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record ChangePasswordRequest
/// {
///     [Required]
///     public string CurrentPassword { get; init; }
/// 
///     [Required]
///     [NotEqualTo(nameof(CurrentPassword))]
///     public string NewPassword { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEqualToAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the name of the property to compare.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property that this value must differ from.</param>
    public NotEqualToAttribute(string otherPropertyName)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    ///     Gets the name of the property that this value must differ from.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.notequalto";

    /// <inheritdoc />
    public override bool RequiresInstance => true;

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        // Cross-property rule: without the parent instance there is nothing to compare, so defer
        // (valid) rather than throw — consistent with the other RequiresInstance attributes.
        return true;
    }

    /// <inheritdoc />
    public override bool IsValid(object? value, object instance)
    {
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "NotEqualTo");
        if (value is null || otherValue is null)
            return true;
        return !Equals(value, otherValue);
    }
}