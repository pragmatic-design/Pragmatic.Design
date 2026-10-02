using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     Every error an operation returns is written as <c>application/problem+json</c>, whatever its
///     status: RFC 9457, what the generated OpenAPI document declares, and what a binding failure
///     already sends.
/// </summary>
/// <remarks>
///     Every status is covered, not a sample: answering <c>application/json</c> on 400, 404, 409 and
///     422 while 401, 403 and 5xx answer <c>application/problem+json</c>, with the same ProblemDetails
///     body in both, would make the published contract and the wire disagree for exactly the four
///     statuses a client sees most.
/// </remarks>
public class EveryErrorAnswersProblemJsonTests
{
    private static readonly IServiceProvider Services =
        new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AnError_IsWrittenAsProblemJson(int status)
    {
        var http = new DefaultHttpContext { RequestServices = Services };
        http.Response.Body = new MemoryStream();

        await new TestError(status).ToResult(http).ExecuteAsync(http).ConfigureAwait(true);

        http.Response.StatusCode.Should().Be(status);
        http.Response.ContentType.Should().StartWith("application/problem+json");
    }

    private sealed record TestError(int Status) : Error
    {
        public override string Code => "TEST_ERROR";
        public override int StatusCode => Status;
    }
}
