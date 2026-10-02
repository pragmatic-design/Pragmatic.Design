using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
/// Edge case tests for Endpoints source generator.
/// </summary>
public class EndpointsEdgeCasesGeneratorTests : EndpointsGeneratorTestBase
{
    // ==========================================================================
    // Namespace Edge Cases
    // ==========================================================================

    [Fact]
    public void Endpoint_InNestedNamespace_GeneratesCorrectly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Api.V1.Users.Endpoints;

            public record NestedResponse(int Id);

            [Endpoint(HttpVerb.Get, "/users/{id}")]
            public partial class GetNestedUserEndpoint : Endpoint<NestedResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<NestedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<NestedResponse>>(new NestedResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();

        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.Contains("GetNestedUserEndpoint.Endpoint"));
    }

    [Fact]
    public void Endpoint_WithGlobalNamespace_MayNotBeSupported()
    {
        // Global namespace (no namespace) may not be supported by the generator
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            public record GlobalResponse(string Value);

            [Endpoint(HttpVerb.Get, "/global")]
            public partial class GlobalEndpoint : Endpoint<GlobalResponse>
            {
                public override Task<Result<GlobalResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<GlobalResponse>>(new GlobalResponse("global"));
                }
            }
            """;

        var result = RunGenerator(source);

        // Generator may or may not support global namespace
        // This test verifies no exceptions are thrown
        result.Diagnostics.Should().NotBeNull();
    }

    // ==========================================================================
    // Complex Type Edge Cases
    // ==========================================================================

    [Fact]
    public void Endpoint_WithGenericTypeResponse_GeneratesCorrectly()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record Item(int Id, string Name);
            public record PagedResponse<T>(List<T> Items, int Total);

            [Endpoint(HttpVerb.Get, "/items")]
            public partial class ListItemsEndpoint : Endpoint<PagedResponse<Item>>
            {
                public override Task<Result<PagedResponse<Item>>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<PagedResponse<Item>>>(
                        new PagedResponse<Item>(new List<Item>(), 0));
                }
            }
            """;

        var result = RunGenerator(source);

        result.HasGeneratedFiles.Should().BeTrue();

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("PagedResponse");
    }

    // ==========================================================================
    // Parameter Edge Cases
    // ==========================================================================

    [Fact]
    public void Endpoint_WithGuidRouteParameter_GeneratesCorrectly()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record GuidResponse(Guid Id);

            [Endpoint(HttpVerb.Get, "/items/{id}")]
            public partial class GetByGuidEndpoint : Endpoint<GuidResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public Guid Id { get; set; }

                public override Task<Result<GuidResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<GuidResponse>>(new GuidResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("Guid id");
    }

    [Fact]
    public void Endpoint_WithMultipleQueryParameters_GeneratesCorrectly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record SearchResponse(string Query, int Page, int Size, string? Sort);

            [Endpoint(HttpVerb.Get, "/search")]
            public partial class AdvancedSearchEndpoint : Endpoint<SearchResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public required string Query { get; set; }

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public int Page { get; set; } = 1;

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public int PageSize { get; set; } = 10;

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public string? SortBy { get; set; }

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public bool Descending { get; set; }

                public override Task<Result<SearchResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<SearchResponse>>(
                        new SearchResponse(Query, Page, PageSize, SortBy));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("string query");
        handlerSource.Should().Contain("int? page");
        handlerSource.Should().Contain("int? pageSize");
        handlerSource.Should().Contain("string? sortBy");
        handlerSource.Should().Contain("bool? descending");
    }

    // ==========================================================================
    // Empty/Minimal Endpoint Tests
    // ==========================================================================

    [Fact]
    public void Endpoint_NoParameters_GeneratesCorrectly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record HealthResponse(string Status);

            [Endpoint(HttpVerb.Get, "/health")]
            public partial class HealthCheckEndpoint : Endpoint<HealthResponse>
            {
                public override Task<Result<HealthResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<HealthResponse>>(new HealthResponse("healthy"));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("MapGet");
        handlerSource.Should().Contain("/health");
    }

    // ==========================================================================
    // Multiple Endpoints in Same Namespace
    // ==========================================================================

    [Fact]
    public void MultipleEndpoints_SameNamespace_GeneratesAll()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Api;

            public record Response1(string Value1);
            public record Response2(string Value2);
            public record Response3(string Value3);

            [Endpoint(HttpVerb.Get, "/endpoint1")]
            public partial class Endpoint1 : Endpoint<Response1>
            {
                public override Task<Result<Response1>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<Response1>>(new Response1("1"));
            }

            [Endpoint(HttpVerb.Get, "/endpoint2")]
            public partial class Endpoint2 : Endpoint<Response2>
            {
                public override Task<Result<Response2>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<Response2>>(new Response2("2"));
            }

            [Endpoint(HttpVerb.Get, "/endpoint3")]
            public partial class Endpoint3 : Endpoint<Response3>
            {
                public override Task<Result<Response3>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<Response3>>(new Response3("3"));
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.Contains("Endpoint1"));
        sources.Keys.Should().Contain(k => k.Contains("Endpoint2"));
        sources.Keys.Should().Contain(k => k.Contains("Endpoint3"));

        // Registration should include all endpoints
        var registrationSource = sources.First(s => s.Key.Contains("Endpoints.Registration")).Value;
        registrationSource.Should().Contain("Endpoint1.MapEndpoint");
        registrationSource.Should().Contain("Endpoint2.MapEndpoint");
        registrationSource.Should().Contain("Endpoint3.MapEndpoint");
    }

    // ==========================================================================
    // Route Pattern Edge Cases
    // ==========================================================================

    [Fact]
    public void Endpoint_WithConstrainedRouteParameter_GeneratesCorrectly()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ConstrainedResponse(int Id);

            [Endpoint(HttpVerb.Get, "/items/{id:int}")]
            public partial class GetWithConstraintEndpoint : Endpoint<ConstrainedResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<ConstrainedResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ConstrainedResponse>>(new ConstrainedResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("/items/{id:int}");
    }

    [Fact]
    public void Endpoint_WithCatchAllRouteParameter_GeneratesCorrectly()
    {
        var source = """
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
                {
                    return Task.FromResult<Result<FileResponse>>(new FileResponse(Path));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("/files/{*path}");
    }

    // ==========================================================================
    // Diagnostics Edge Cases
    // ==========================================================================

    [Fact]
    public void Endpoint_MissingEndpointAttribute_NoGeneration()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record MissingAttrResponse(string Value);

            // Missing [Endpoint] attribute
            public partial class MissingAttributeEndpoint : Endpoint<MissingAttrResponse>
            {
                public override Task<Result<MissingAttrResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<MissingAttrResponse>>(new MissingAttrResponse("test"));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().NotContain(k => k.Contains("MissingAttributeEndpoint"));
    }

    // ==========================================================================
    // Complex DI Dependencies
    // ==========================================================================

    [Fact]
    public void Endpoint_WithMultipleDependencies_GeneratesSetDependencies()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public interface IService1 { }
            public interface IService2 { }
            public interface IService3 { }

            public record ComplexResponse(string Status);

            [Endpoint(HttpVerb.Get, "/complex")]
            public partial class ComplexDependenciesEndpoint : Endpoint<ComplexResponse>
            {
                private IService1 _service1 = null!;
                private IService2 _service2 = null!;
                private IService3 _service3 = null!;

                public override Task<Result<ComplexResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ComplexResponse>>(new ComplexResponse("ok"));
                }
            }
            """;

        var result = RunGenerator(source);

        var setDepsSource = GetGeneratedSource(result, "SetDependencies");
        setDepsSource.Should().NotBeNull();
        setDepsSource.Should().Contain("IService1");
        setDepsSource.Should().Contain("IService2");
        setDepsSource.Should().Contain("IService3");
    }
}
