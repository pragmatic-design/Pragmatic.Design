using Pragmatic.Internationalization.Types;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a <see cref="Money" /> amount is zero or greater.
/// </summary>
/// <remarks>
///     Null passes: use <c>[Required]</c> for that. For strictly greater than zero use
///     <see cref="PositiveMoneyAttribute" />.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NonNegativeMoneyAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.nonnegativemoney";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        return value is Money money && money.Amount >= 0;
    }
}
