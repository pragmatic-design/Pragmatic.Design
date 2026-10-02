using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A download publishes bytes, not a record. <c>Produces&lt;FileResponse&gt;(200)</c> would put a
///     schema with a <c>Stream</c> property into the OpenAPI document — something no client can
///     generate or consume — so both endpoint shapes must declare
///     <c>application/octet-stream</c> instead.
/// </summary>
public class FileResponseProducesTests : EndpointsGeneratorTestBase
{
    private const string EndpointOfFileResponse = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Endpoints.Responses;
        using Pragmatic.Result;

        namespace TestApp;

        [Endpoint(HttpVerb.Get, "/documents/{id}/content")]
        public partial class DownloadDocumentEndpoint : Endpoint<FileResponse>
        {
            [Microsoft.AspNetCore.Mvc.FromRoute]
            public System.Guid Id { get; set; }

            public override Task<Result<FileResponse>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<FileResponse>>(
                    new FileResponse(new System.IO.MemoryStream(), "application/pdf", "doc.pdf"));
        }
        """;

    private const string DomainActionOfFileResponse = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Responses;
        using Pragmatic.Result;

        namespace TestApp;

        [DomainAction]
        [Endpoint(HttpVerb.Get, "/documents/{id}/content")]
        public partial class DownloadDocumentAction : DomainAction<FileResponse>
        {
            [Microsoft.AspNetCore.Mvc.FromRoute]
            public System.Guid Id { get; set; }

            public override Task<Result<FileResponse, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<FileResponse, IError>>(
                    new FileResponse(new System.IO.MemoryStream(), "application/pdf", "doc.pdf"));
        }
        """;

    /// <summary>A consumer type that merely shares the name must not be mistaken for the framework's.</summary>
    private const string EndpointOfLookalikeType = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp;

        public record FileResponse(string Path);

        [Endpoint(HttpVerb.Get, "/files/{*path}")]
        public partial class GetFileEndpoint : Endpoint<FileResponse>
        {
            [Microsoft.AspNetCore.Mvc.FromRoute]
            public string Path { get; set; } = null!;

            public override Task<Result<FileResponse>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<FileResponse>>(new FileResponse(Path));
        }
        """;

    [Fact]
    public void EndpointOfFileResponse_DeclaresOctetStream()
    {
        var handler = GetGeneratedSource(RunGenerator(EndpointOfFileResponse), "Endpoint");

        handler.Should().Contain("ProducesResponseTypeMetadata(200, null, new[] { \"application/octet-stream\" })");
    }

    [Fact]
    public void EndpointOfFileResponse_DoesNotPublishTheRecordSchema()
    {
        var handler = GetGeneratedSource(RunGenerator(EndpointOfFileResponse), "Endpoint");

        handler.Should().NotContain("Produces<global::Pragmatic.Endpoints.Responses.FileResponse>");
    }

    [Fact]
    public void DomainActionOfFileResponse_DeclaresOctetStream()
    {
        var handler = GetGeneratedSource(RunGenerator(DomainActionOfFileResponse), "Endpoint");

        handler.Should().Contain("ProducesResponseTypeMetadata(200, null, new[] { \"application/octet-stream\" })");
        handler.Should().NotContain("Produces<global::Pragmatic.Endpoints.Responses.FileResponse>");
    }

    [Fact]
    public void LookalikeResponseType_KeepsItsTypedSchema()
    {
        // Matching on the "FileResponse" suffix would route this through
        // FileResponseExtensions.ToResult, which cannot accept it — the generated code would not compile.
        var handler = GetGeneratedSource(RunGenerator(EndpointOfLookalikeType), "Endpoint");

        handler.Should().Contain("ProducesResponseTypeMetadata(200, typeof(global::TestApp.FileResponse))");
        handler.Should().NotContain("FileResponseExtensions.ToResult");
    }

    [Fact]
    public void LookalikeResponseType_IsReturnedAsJson()
    {
        var handler = GetGeneratedSource(RunGenerator(EndpointOfLookalikeType), "Endpoint");

        handler.Should().Contain("Results.Ok(success)");
    }
}
