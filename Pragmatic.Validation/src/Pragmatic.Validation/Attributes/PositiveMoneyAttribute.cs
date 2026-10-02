using Pragmatic.Internationalization.Types;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a <see cref="Money" /> amount is greater than zero.
/// </summary>
/// <remarks>
///     Null passes: use <c>[Required]</c> for that. For zero-or-greater use
///     <see cref="NonNegativeMoneyAttribute" />.
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateOrderRequest
/// {
///     [Required]
///     [PositiveMoney]
///     public Money Total { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PositiveMoneyAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.positivemoney";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        return value is Money money && money.Amount > 0;
    }
}
