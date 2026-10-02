using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Samples.Models;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Simple hello world endpoint demonstrating basic usage.
/// </summary>
[Endpoint(HttpVerb.Get, "/hello")]
[ApiSummary("Say Hello")]
[ApiDescription("Returns a greeting message with timestamp.")]
[ApiTags("General")]
public partial class HelloEndpoint : Endpoint<HelloResponse>
{
    /// <inheritdoc />
    public override Task<Result<HelloResponse>> HandleAsync(CancellationToken ct = default)
    {
        var response = new HelloResponse
        {
            Message = "Hello from Pragmatic.Endpoints!",
            Timestamp = DateTimeOffset.UtcNow
        };

        return Task.FromResult(Result<HelloResponse>.Success(response));
    }
}