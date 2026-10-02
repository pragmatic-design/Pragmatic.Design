namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property is required when another property has a specific value.
/// </summary>
/// <remarks>
///     <para>
///         Makes a property conditionally required based on another property's value.
///         The property is required when the other property equals the expected value.
///     </para>
///     <para>
///         Use <see cref="RequiredIfNotAttribute" /> for the inverse condition.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record PaymentRequest
/// {
///     [Required]
///     public PaymentMethod Method { get; init; }
/// 
///     [RequiredIf(nameof(Method), PaymentMethod.CreditCard)]
///     public string? CardNumber { get; init; }
/// 
///     [RequiredIf(nameof(Method), PaymentMethod.BankTransfer)]
///     public string? BankAccount { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class RequiredIfAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance for when another property equals a value.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property to check.</param>
    /// <param name="expectedValue">The value that triggers the requirement.</param>
    public RequiredIfAttribute(string otherPropertyName, object? expectedValue)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
        ExpectedValue = expectedValue;
    }

    /// <summary>
    ///     Gets the name of the property that determines whether this property is required.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <summary>
    ///     Gets the value that triggers the requirement.
    /// </summary>
    public object? ExpectedValue { get; }

    /// <summary>
    ///     When <c>true</c>, an empty string satisfies the requirement; when <c>false</c> (the default),
    ///     an empty string is rejected just like null once the condition is met.
    /// </summary>
    public bool AllowEmptyStrings { get; set; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.requiredif";

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
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "RequiredIf");
        var conditionMet = Equals(otherValue, ExpectedValue);
        if (!conditionMet)
            return true;
        if (value is null)
            return false;
        if (!AllowEmptyStrings && value is string s && string.IsNullOrEmpty(s))
            return false;
        return true;
    }
}