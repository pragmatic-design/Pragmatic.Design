using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Benchmarks.Endpoints;

/// <summary>
///     Never mapped: it exists so the generator treats the document as a response and plans its writer, or says,
///     with PRAG0555, why it does not.
/// </summary>
[Endpoint(HttpVerb.Get, "/twitter")]
public partial class TwitterEndpoint : Endpoint<Twitter.Root>
{
    public override Task<Result<Twitter.Root>> HandleAsync(CancellationToken ct = default)
        => throw new NotSupportedException("A benchmark endpoint is not called.");
}
