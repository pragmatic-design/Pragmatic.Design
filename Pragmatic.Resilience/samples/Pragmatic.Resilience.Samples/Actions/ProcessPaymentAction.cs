using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Resilience.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Samples.Actions;

/// <summary>
///     Simulates a payment processing operation.
///     [ResiliencePolicy("payment-provider")] wraps with retry + circuit breaker.
/// </summary>
[DomainAction]
[ResiliencePolicy("payment-provider")]
public partial class ProcessPaymentAction : DomainAction<string>
{
    public required decimal Amount { get; init; }

    public override async Task<Result<string, IError>> Execute(CancellationToken ct = default)
    {
        // Simulate payment gateway call
        await Task.Delay(100, ct).ConfigureAwait(false);
        return Result<string, IError>.Success($"Payment of {Amount:C} processed");
    }
}
