using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a property value is greater than or equal to another property value.
/// </summary>
/// <remarks>
///     <para>
///         The inclusive form of <see cref="GreaterThanPropertyAttribute" />: a period that ends on the day
///         it starts is a period, and the strict form refuses it. Both values must be comparable.
///         Null values pass (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial class SubmitLeaveRequest
/// {
///     public required DateOnly From { get; init; }
///
///     [GreaterThanOrEqualProperty(nameof(From))]
///     public required DateOnly To { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class GreaterThanOrEqualPropertyAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the name of the property to compare.
    /// </summary>
    /// <param name="otherPropertyName">The name of the property this value may not be below.</param>
    public GreaterThanOrEqualPropertyAttribute(string otherPropertyName)
    {
        ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    ///     Gets the name of the property this value may not be below.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.greaterthanorequalproperty";

    /// <inheritdoc />
    public override bool RequiresInstance => true;

    /// <inheritdoc />
    public override bool IsValid(object? value) => true;

    /// <inheritdoc />
    public override bool IsValid(object? value, object instance)
    {
        var otherValue = CrossPropertyHelper.GetPropertyValue(instance, OtherPropertyName, "GreaterThanOrEqualProperty");
        // Values that cannot be compared leave the rule inapplicable (valid), as for the strict form.
        return CrossPropertyComparison.Compare(value, otherValue) is not int cmp || cmp >= 0;
    }
}
