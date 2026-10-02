namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is negative (less than zero).
/// </summary>
/// <remarks>
///     <para>
///         Zero is not considered negative. Use <see cref="RangeAttribute" /> for more specific bounds.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record AdjustmentRequest
/// {
///     [Negative]  // Only allow negative adjustments
///     public decimal Adjustment { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NegativeAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.negative";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        return value switch
        {
            int i => i < 0,
            long l => l < 0,
            short s => s < 0,
            sbyte sb => sb < 0,
            float f => f < 0,
            double d => d < 0,
            decimal dec => dec < 0,
            _ => false // Unsigned types can never be negative; always fail
        };
    }
}