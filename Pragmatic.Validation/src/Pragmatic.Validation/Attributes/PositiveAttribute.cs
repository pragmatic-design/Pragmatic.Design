namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a numeric value is positive (greater than zero).
/// </summary>
/// <remarks>
///     <para>
///         Zero is not considered positive. Use <see cref="RangeAttribute" /> with 0 as minimum for >= 0.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record TransferRequest
/// {
///     [Required]
///     [Positive]
///     public decimal Amount { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PositiveAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.positive";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        return value switch
        {
            int i => i > 0,
            long l => l > 0,
            short s => s > 0,
            byte b => b > 0,
            sbyte sb => sb > 0,
            uint ui => ui > 0,
            ulong ul => ul > 0,
            ushort us => us > 0,
            float f => f > 0,
            double d => d > 0,
            decimal dec => dec > 0,
            _ => true
        };
    }
}