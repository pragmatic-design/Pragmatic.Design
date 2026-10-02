using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Caching.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>Publishes new rates, so what <see cref="ReadRateAction" /> cached is stale.</summary>
[DomainAction]
[InvalidatesCache("rates")]
public partial class PublishRatesAction : VoidDomainAction
{
    /// <inheritdoc />
    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(VoidResult<IError>.Success());
}
