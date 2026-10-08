using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Benchmarks.Endpoints;

/// <summary>Never mapped: see <see cref="TwitterEndpoint" />.</summary>
[Endpoint(HttpVerb.Get, "/canada")]
public partial class CanadaEndpoint : Endpoint<Canada.Root>
{
    public override Task<Result<Canada.Root>> HandleAsync(CancellationToken ct = default)
        => throw new NotSupportedException("A benchmark endpoint is not called.");
}
