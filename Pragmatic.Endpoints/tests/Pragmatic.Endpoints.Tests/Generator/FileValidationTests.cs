using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for [MaxFileSize] and [AllowedContentTypes] file validation attributes
///     on [FromForm] IFormFile properties.
/// </summary>
public class FileValidationTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Endpoint_WithMaxFileSize_GeneratesValidationCheck()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(string FileName);

            [Endpoint(HttpVerb.Post, "/upload")]
            public partial class UploadEndpoint : Endpoint<UploadResult>
            {
                [FromForm]
                [MaxFileSize(10485760)]
                public IFormFile File { get; set; } = null!;

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(File.FileName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("file.Length > 10485760L");
        handlerSource.Should().Contain("statusCode: 413");
        handlerSource.Should().Contain("10 MB");
    }

    [Fact]
    public void Endpoint_WithNonPositiveMaxFileSize_ReportsPrag0516()
    {
        // A non-positive limit would generate `if (file.Length > 0L)` (rejects every upload)
        // — PRAG0516 rejects it at compile time, symmetric to PRAG0517 for [MaxBodySize].
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(string FileName);

            [Endpoint(HttpVerb.Post, "/upload")]
            public partial class UploadEndpoint : Endpoint<UploadResult>
            {
                [FromForm]
                [MaxFileSize(0)]
                public IFormFile File { get; set; } = null!;

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(File.FileName));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0516").Should().BeTrue();
    }

    [Fact]
    public void Endpoint_WithAllowedContentTypes_GeneratesValidationCheck()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(string FileName);

            [Endpoint(HttpVerb.Post, "/upload")]
            public partial class UploadEndpoint : Endpoint<UploadResult>
            {
                [FromForm]
                [AllowedContentTypes("image/jpeg", "image/png", "image/webp")]
                public IFormFile Photo { get; set; } = null!;

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(Photo.FileName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("image/jpeg");
        handlerSource.Should().Contain("image/png");
        handlerSource.Should().Contain("image/webp");
        handlerSource.Should().Contain("statusCode: 415");
    }

    [Fact]
    public void Endpoint_WithBothFileValidations_GeneratesBothChecks()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(string FileName);

            [Endpoint(HttpVerb.Post, "/upload")]
            public partial class UploadEndpoint : Endpoint<UploadResult>
            {
                [FromForm]
                [MaxFileSize(5242880)]
                [AllowedContentTypes("application/pdf")]
                public IFormFile Document { get; set; } = null!;

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(Document.FileName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Size check
        handlerSource.Should().Contain("document.Length > 5242880L");
        handlerSource.Should().Contain("statusCode: 413");
        handlerSource.Should().Contain("5 MB");
        // Content type check
        handlerSource.Should().Contain("application/pdf");
        handlerSource.Should().Contain("statusCode: 415");
    }

    [Fact]
    public void Endpoint_WithFormFileCollection_GeneratesCorrectBinding()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(int Count);

            [Endpoint(HttpVerb.Post, "/upload-multiple")]
            public partial class MultiUploadEndpoint : Endpoint<UploadResult>
            {
                [FromForm]
                public IFormFileCollection Files { get; set; } = null!;

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(Files.Count));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("ReadFormAsync");
        handlerSource.Should().Contain("IFormFileCollection");
        handlerSource.Should().Contain("DisableAntiforgery");
        handlerSource.Should().Contain("multipart/form-data");
    }

    [Fact]
    public void Endpoint_WithNoFileValidation_DoesNotGenerateChecks()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UploadResult(string FileName);

            [Endpoint(HttpVerb.Post, "/upload")]
            public partial class UploadEndpoint : Endpoint<UploadResult>
            {
                [FromForm]
                public IFormFile File { get; set; } = null!;

                public override Task<Result<UploadResult>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UploadResult>>(new UploadResult(File.FileName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // No per-property validation checks when no validation attributes
        // (global defaults from PragmaticEndpointsOptions may still be present via __fileOpts)
        // Per-property checks use hardcoded literals like "file.Length > 10485760L"
        handlerSource.Should().NotContain("10485760L");
        handlerSource.Should().NotContain("5242880L");
        // But still has form configuration
        handlerSource.Should().Contain("DisableAntiforgery");
    }

    [Fact]
    public void DomainAction_WithFileValidation_GeneratesChecks()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp;

            public record PhotoResult(string Id);

            [DomainAction]
            [Endpoint(HttpVerb.Post, "/photos")]
            public partial class UploadPhotoAction : DomainAction<PhotoResult, IError>
            {
                [FromForm]
                [MaxFileSize(10485760)]
                [AllowedContentTypes("image/jpeg", "image/png")]
                public IFormFile Photo { get; set; } = null!;

                public override Task<Result<PhotoResult, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<PhotoResult, IError>>(new PhotoResult("ok"));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("photo.Length > 10485760L");
        handlerSource.Should().Contain("statusCode: 413");
        handlerSource.Should().Contain("image/jpeg");
        handlerSource.Should().Contain("statusCode: 415");
    }

    [Fact]
    public void DomainAction_WithUnattributedFile_AppliesGlobalUploadDefaults()
    {
        // A Domain Action file upload with no [MaxFileSize]/[AllowedContentTypes] must
        // still be bound by the app-wide PragmaticEndpointsOptions limits, like standalone endpoints.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp;

            public record PhotoResult(string Id);

            [DomainAction]
            [Endpoint(HttpVerb.Post, "/photos")]
            public partial class UploadPhotoAction : DomainAction<PhotoResult, IError>
            {
                [FromForm]
                public IFormFile Photo { get; set; } = null!;

                public override Task<Result<PhotoResult, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<PhotoResult, IError>>(new PhotoResult("ok"));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Global fallback wired in, for Domain Actions too.
        handlerSource.Should().Contain("PragmaticEndpointsOptions");
        handlerSource.Should().Contain("__fileOpts.MaxUploadFileSize");
        handlerSource.Should().Contain("DefaultAllowedContentTypes");
        // No hardcoded per-property limit since the file is unattributed.
        handlerSource.Should().NotContain("10485760L");
    }
}
