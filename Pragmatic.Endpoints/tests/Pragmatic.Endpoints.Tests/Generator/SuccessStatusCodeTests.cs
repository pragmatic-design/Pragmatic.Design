using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Non-standard successful status codes must still return the response body. Emitting
///     <c>TypedResults.StatusCode(code)</c> for any success code other than 200/201 would silently
///     drop the payload despite the declared return type.
/// </summary>
public class SuccessStatusCodeTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void Endpoint_With202_PreservesBodyWithStatusCode()
    {
        var source = CommonUsings + """

            namespace TestApp.Jobs;

            public record JobDto(string Id);

            [Endpoint(HttpVerb.Post, "/jobs")]
            [HttpStatus(202)]
            public partial class StartJob : Endpoint<JobDto>
            {
                public override Task<Result<JobDto, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<JobDto, IError>.Success(new JobDto("1")));
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Body preserved with the configured status code, through the type's generated writer...
        handlerSource.Should().Contain("GeneratedJsonResponse<global::TestApp.Jobs.JobDto>(success!, 202,");
        // ...and NOT the payload-dropping bare-status result.
        handlerSource.Should().NotContain("TypedResults.StatusCode(202)");
    }

    [Fact]
    public void Endpoint_With204_ReturnsNoContentWithoutBody()
    {
        var source = CommonUsings + """

            namespace TestApp.Jobs;

            public record JobDto(string Id);

            [Endpoint(HttpVerb.Post, "/jobs/ack")]
            [HttpStatus(204)]
            public partial class AckJob : Endpoint<JobDto>
            {
                public override Task<Result<JobDto, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<JobDto, IError>.Success(new JobDto("1")));
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // 204 must not carry a body.
        handlerSource.Should().Contain("Results.NoContent()");
        handlerSource.Should().NotContain("statusCode: 204");
    }
}
