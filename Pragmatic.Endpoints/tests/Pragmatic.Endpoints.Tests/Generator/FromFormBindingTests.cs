using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for [FromForm] binding in endpoint source generation.
///     Uses standard ASP.NET Core [FromForm] attribute — no custom attribute needed.
/// </summary>
public class FromFormBindingTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Endpoint_WithFormParameter_GeneratesFromFormBinding()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResponse(string FileName);

            [Endpoint(HttpVerb.Post, "/upload")]
            public partial class UploadEndpoint : Endpoint<UploadResponse>
            {
                [FromForm]
                public string Description { get; set; } = "";

                public override Task<Result<UploadResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResponse>>(new UploadResponse(Description));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("RequestValues.Form(__form,");
        handlerSource.Should().Contain("DisableAntiforgery");
    }

    [Fact]
    public void Endpoint_WithFormAndRoute_GeneratesBothBindings()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(Guid FolderId, string Name);

            [Endpoint(HttpVerb.Post, "/folders/{folderId}/upload")]
            public partial class UploadToFolderEndpoint : Endpoint<UploadResult>
            {
                [FromRoute]
                public Guid FolderId { get; set; }

                [FromForm]
                public string FileName { get; set; } = "";

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(FolderId, FileName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("folderId");
        handlerSource.Should().Contain("RequestValues.Form(__form,");
        // Form params should exclude FromBody
        handlerSource.Should().NotContain("FromBody");
    }

    [Fact]
    public void Endpoint_WithFormParam_ExcludesFromBody()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreateResult(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            public partial class CreateWithFormEndpoint : Endpoint<CreateResult>
            {
                [FromForm]
                public string Name { get; set; } = "";

                [FromForm]
                public string Category { get; set; } = "";

                public override Task<Result<CreateResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateResult>>(new CreateResult(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // When form params exist, there should be no body DTO
        handlerSource.Should().NotContain("FromBody");
        handlerSource.Should().Contain("DisableAntiforgery");
    }
}
