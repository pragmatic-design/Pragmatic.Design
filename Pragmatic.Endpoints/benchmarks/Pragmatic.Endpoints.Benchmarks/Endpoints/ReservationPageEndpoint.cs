using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Benchmarks.Endpoints;

/// <summary>Never mapped: see <see cref="TwitterEndpoint" />.</summary>
[Endpoint(HttpVerb.Get, "/reservations")]
public partial class ReservationPageEndpoint : Endpoint<ReservationPage>
{
    public override Task<Result<ReservationPage>> HandleAsync(CancellationToken ct = default)
        => throw new NotSupportedException("A benchmark endpoint is not called.");
}
