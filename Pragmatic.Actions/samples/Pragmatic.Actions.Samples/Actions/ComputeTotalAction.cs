using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Computes an order total from given items. Demonstrates: DomainAction
///     with no dependencies (pure computation).
/// </summary>
/// <remarks>
///     The source generator will produce an Invoker but NO SetDependencies
///     since there are no injectable fields.
/// </remarks>
[DomainAction]
public partial class ComputeTotalAction : DomainAction<decimal>
{
    /// <summary>
    ///     The unit prices of the items.
    /// </summary>
    public required decimal[] Prices { get; init; }

    /// <summary>
    ///     The quantities of each item.
    /// </summary>
    public required int[] Quantities { get; init; }

    public override Task<Result<decimal, IError>> Execute(CancellationToken ct = default)
    {
        var total = 0m;
        var count = Math.Min(Prices.Length, Quantities.Length);

        for (var i = 0; i < count; i++)
            total += Prices[i] * Quantities[i];

        return Task.FromResult(Result<decimal, IError>.Success(total));
    }
}
