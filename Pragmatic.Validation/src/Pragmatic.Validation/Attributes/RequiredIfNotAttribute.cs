namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property is required when another property does NOT have a specific value.
/// </summary>
/// <remarks>
///     <para>
///         Makes a property conditionally required based on another property's value.
///         The property is required when the other property does NOT equal the expected value.
///     </para>
///     <para>
///         Common use case: requiring a field when another field is not filled in (null).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record ContactRequest
/// {
///     public string? Email { get; init; }
/// 
///     [RequiredIfNot(nameof(Email), null)]  // Required if Email is provided
///     public string? Phone { get; init; }
/// }
/// 
/// // Alternative: require Phone when ContactMethod is NOT Email
/// public partial record ContactRequest2
/// {
///     [Required]
///     public ContactMethod Method { get; init; }
/// 
///     [RequiredIfNot(nameof(Method), ContactMethod.Email)]
///     public string? Phone { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class RequiredIfNotAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance for when another property does NOT equal a value.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property to check.</param>
    /// <param name="excludedValue">The value that, when NOT present, triggers the requirement.</param>
    public RequiredIfNotAttribute(string otherPropertyName, object? excludedValue)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
        ExcludedValue = excludedValue;
    }

    /// <summary>
    ///     Gets the name of the property that determines whether this property is required.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <summary>
    ///     Gets the value that, when NOT present, triggers the requirement.
    /// </summary>
    public object? ExcludedValue { get; }

    /// <summary>
    ///     When <c>true</c>, an empty string satisfies the requirement; when <c>false</c> (the default),
    ///     an empty string is rejected just like null once the condition is met.
    /// </summary>
    public bool AllowEmptyStrings { get; set; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.requiredifnot";

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
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "RequiredIfNot");
        var conditionMet = !Equals(otherValue, ExcludedValue);
        if (!conditionMet)
            return true;
        if (value is null)
            return false;
        if (!AllowEmptyStrings && value is string s && string.IsNullOrEmpty(s))
            return false;
        return true;
    }
}