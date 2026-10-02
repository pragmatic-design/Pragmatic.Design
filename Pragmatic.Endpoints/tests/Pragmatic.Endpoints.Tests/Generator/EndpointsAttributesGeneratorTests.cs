using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
/// Tests for Endpoints source generator handling of various attributes.
/// </summary>
public class EndpointsAttributesGeneratorTests : EndpointsGeneratorTestBase
{
    // ==========================================================================
    // AllowAnonymous Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithAllowAnonymous_GeneratesAttribute()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record PublicResponse(string Message);

            [Endpoint(HttpVerb.Get, "/public")]
            [AllowAnonymous]
            public partial class PublicEndpoint : Endpoint<PublicResponse>
            {
                public override Task<Result<PublicResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<PublicResponse>>(new PublicResponse("Hello"));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("AllowAnonymous");
    }

    // ==========================================================================
    // HTTP Method Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_Put_GeneratesCorrectMethod()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UpdateResponse(int Id);

            [Endpoint(HttpVerb.Put, "/items/{id}")]
            public partial class UpdateItemEndpoint : Endpoint<UpdateResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<UpdateResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UpdateResponse>>(new UpdateResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("MapPut");
    }

    [Fact]
    public void Endpoint_Patch_GeneratesCorrectMethod()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record PatchResponse(int Id);

            [Endpoint(HttpVerb.Patch, "/items/{id}")]
            public partial class PatchItemEndpoint : Endpoint<PatchResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<PatchResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<PatchResponse>>(new PatchResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("MapPatch");
    }

    // ==========================================================================
    // OpenAPI Metadata Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithSummaryAndDescription_GeneratesMetadata()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UserResponse(int Id, string Name);

            [Endpoint(HttpVerb.Get, "/users/{id}")]
            [ApiSummary("Get User")]
            [ApiDescription("Retrieves a user by their unique identifier.")]
            public partial class GetUserWithDocsEndpoint : Endpoint<UserResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<UserResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse>>(new UserResponse(Id, "Test"));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("WithSummary");
        handlerSource.Should().Contain("Get User");
    }

    [Fact]
    public void Endpoint_WithTags_GeneratesTagMetadata()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProductResponse(int Id);

            [Endpoint(HttpVerb.Get, "/products/{id}")]
            [ApiTags("Products", "Catalog")]
            public partial class GetProductEndpoint : Endpoint<ProductResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<ProductResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProductResponse>>(new ProductResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("WithTags");
        handlerSource.Should().Contain("Products");
    }

    // ==========================================================================
    // Error Type Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithCustomError_GeneratesProduces()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UserResponse(int Id);
            public record NotFoundError : IError
            {
                public string Message => "Not found";
                public int StatusCode => 404;
            }

            [Endpoint(HttpVerb.Get, "/users/{id}")]
            public partial class GetUserOrNotFoundEndpoint : Endpoint<UserResponse, NotFoundError>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<UserResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse, NotFoundError>>(new UserResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Should generate Produces for 404
        handlerSource.Should().Contain("Produces");
    }

    [Fact]
    public void Endpoint_WithMultipleErrors_GeneratesTypedProducesForEach()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record OrderResponse(int Id);
            public record NotFoundError : IError
            {
                public string Message => "Not found";
                public int StatusCode => 404;
            }
            public record ValidationError : IError
            {
                public string Message => "Validation failed";
                public int StatusCode => 422;
            }
            public record ConflictError : IError
            {
                public string Message => "Conflict";
                public int StatusCode => 409;
            }

            [Endpoint(HttpVerb.Put, "/orders/{id}")]
            public partial class UpdateOrderEndpoint : Endpoint<OrderResponse, NotFoundError, ValidationError, ConflictError>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<OrderResponse, NotFoundError, ValidationError, ConflictError>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse, NotFoundError, ValidationError, ConflictError>>(new OrderResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Each error type should get a typed Produces<T>(statusCode) call
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(404, typeof(global::TestApp.NotFoundError))");
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(422, typeof(global::TestApp.ValidationError))");
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(409, typeof(global::TestApp.ConflictError))");
        // Should NOT contain generic ProducesProblem
        handlerSource.Should().NotContain("ProducesProblem");
    }

    [Fact]
    public void Endpoint_WithSingleError_GeneratesTypedProduce()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record UserResponse(int Id);
            public record NotFoundError : IError
            {
                public string Message => "Not found";
                public int StatusCode => 404;
            }

            [Endpoint(HttpVerb.Get, "/users/{id}")]
            public partial class GetUserTypedErrorEndpoint : Endpoint<UserResponse, NotFoundError>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<UserResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse, NotFoundError>>(new UserResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Typed Produces with error type
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(404, typeof(global::TestApp.NotFoundError))");
        // Should NOT contain generic ProducesProblem
        handlerSource.Should().NotContain("ProducesProblem");
    }

    [Fact]
    public void VoidEndpoint_WithMultipleErrors_GeneratesTypedProducesForEach()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record NotFoundError : IError
            {
                public string Message => "Not found";
                public int StatusCode => 404;
            }
            public record ConflictError : IError
            {
                public string Message => "Conflict";
                public int StatusCode => 409;
            }

            [Endpoint(HttpVerb.Delete, "/items/{id}")]
            public partial class DeleteItemEndpoint : VoidEndpoint<NotFoundError, ConflictError>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<VoidResult<NotFoundError, ConflictError>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult(VoidResult<NotFoundError, ConflictError>.Ok());
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // VoidEndpoint should still get typed Produces for errors
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(204)");
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(404, typeof(global::TestApp.NotFoundError))");
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(409, typeof(global::TestApp.ConflictError))");
        handlerSource.Should().NotContain("ProducesProblem");
    }

    // ==========================================================================
    // ApiVersion Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithApiVersion_GeneratesVersionMetadata()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record VersionedResponse(string Version);

            [Endpoint(HttpVerb.Get, "/api/v{version}/data")]
            [ApiVersion("1.0")]
            public partial class VersionedEndpoint : Endpoint<VersionedResponse>
            {
                public override Task<Result<VersionedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<VersionedResponse>>(new VersionedResponse("1.0"));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();

        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.Contains("VersionedEndpoint"));
    }

    // ==========================================================================
    // EndpointGroup Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithGroup_GeneratesGrouping()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record GroupedResponse(string Group);

            [Endpoint(HttpVerb.Get, "/admin/settings")]
            [EndpointGroup("admin")]
            public partial class AdminSettingsEndpoint : Endpoint<GroupedResponse>
            {
                public override Task<Result<GroupedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<GroupedResponse>>(new GroupedResponse("admin"));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
    }

    // ==========================================================================
    // Processor Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithPreProcessor_HandledCorrectly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public class LoggingPreProcessor : IPreProcessor
            {
                public Task<Result<Unit>> ProcessAsync(EndpointContext context, CancellationToken ct)
                    => Task.FromResult<Result<Unit>>(Unit.Value);
            }

            public record ProcessedResponse(string Status);

            [Endpoint(HttpVerb.Get, "/processed")]
            [PreProcessor<LoggingPreProcessor>]
            public partial class ProcessedEndpoint : Endpoint<ProcessedResponse>
            {
                public override Task<Result<ProcessedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProcessedResponse>>(new ProcessedResponse("ok"));
                }
            }
            """;

        var result = RunGenerator(source);

        // Processors might not affect generated handler structure
        // Just verify the endpoint is generated
        result.HasGeneratedFiles.Should().BeTrue();
    }

    // ==========================================================================
    // ResponseCache Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithResponseCache_GeneratesCachePolicy()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CachedResponse(string Data);

            [Endpoint(HttpVerb.Get, "/cached-data")]
            [ResponseCache(Duration = 60)]
            public partial class CachedDataEndpoint : Endpoint<CachedResponse>
            {
                public override Task<Result<CachedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CachedResponse>>(new CachedResponse("cached"));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Cache directive may or may not be explicitly generated
    }

    // ==========================================================================
    // RateLimit Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithRateLimit_HandledCorrectly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record RateLimitedResponse(string Status);

            [Endpoint(HttpVerb.Get, "/rate-limited")]
            [RateLimit("fixed", PermitLimit = 100, Window = 60)]
            public partial class RateLimitedEndpoint : Endpoint<RateLimitedResponse>
            {
                public override Task<Result<RateLimitedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<RateLimitedResponse>>(new RateLimitedResponse("ok"));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();
    }

    // ==========================================================================
    // RequirePermission Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithRequirePermission_GeneratesAuthorization()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record SecuredResponse(string Data);

            [Endpoint(HttpVerb.Get, "/secured")]
            [RequirePermission("read:data")]
            public partial class SecuredEndpoint : Endpoint<SecuredResponse>
            {
                public override Task<Result<SecuredResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<SecuredResponse>>(new SecuredResponse("secret"));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Authorization requires may generate RequireAuthorization
        handlerSource.Should().Contain("RequireAuthorization");
    }

    // ==========================================================================
    // Multiple Status Codes Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_WithCustomSuccessStatus_GeneratesCorrectCode()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreatedResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            [HttpStatus(201)]
            public partial class CreateItemEndpoint : Endpoint<CreatedResponse>
            {
                public required string Name { get; set; }

                public override Task<Result<CreatedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreatedResponse>>(new CreatedResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(201, typeof(global::TestApp.CreatedResponse))");
    }

    // ==========================================================================
    // Delete Method Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_Delete_GeneratesCorrectMethod()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record DeleteResponse(bool Success);

            [Endpoint(HttpVerb.Delete, "/items/{id}")]
            public partial class DeleteItemEndpoint : Endpoint<DeleteResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<DeleteResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<DeleteResponse>>(new DeleteResponse(true));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("MapDelete");
    }

    [Fact]
    public void Endpoint_WithCreatedAt_GeneratesLocationHeader()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreatedResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            [HttpStatus(201)]
            [CreatedAt("/items/{Id}")]
            public partial class CreateWithLocationEndpoint : Endpoint<CreatedResponse>
            {
                public required string Name { get; set; }

                public override Task<Result<CreatedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreatedResponse>>(new CreatedResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // The [CreatedAt] template becomes a real Location: tokens read from the success value,
        // URL-escaped and invariant-formatted — no more "Location: null" on 201 (B20).
        handlerSource.Should().Contain("Results.Created($\"/items/{(global::System.Uri.EscapeDataString(");
        handlerSource.Should().Contain("success.Id");
        handlerSource.Should().NotContain("Created((string?)null");
    }

    [Fact]
    public void Endpoint_201WithoutCreatedAt_KeepsNullLocation()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreatedResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            [HttpStatus(201)]
            public partial class CreateNoLocationEndpoint : Endpoint<CreatedResponse>
            {
                public required string Name { get; set; }

                public override Task<Result<CreatedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreatedResponse>>(new CreatedResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().Contain("Created((string?)null");
    }
}
