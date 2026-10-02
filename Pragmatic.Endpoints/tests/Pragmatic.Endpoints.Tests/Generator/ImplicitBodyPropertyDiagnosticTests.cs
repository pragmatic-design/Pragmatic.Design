using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for PRAG0512 diagnostic: property implicitly binds to request body.
///     The diagnostic fires when an endpoint has 5+ body properties without explicit binding attributes.
/// </summary>
public class ImplicitBodyPropertyDiagnosticTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Endpoint_With5ImplicitBodyProperties_EmitsPRAG0512()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreateResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            public partial class CreateItemEndpoint : Endpoint<CreateResponse>
            {
                // 5 body properties without explicit binding — triggers PRAG0512
                public required string Name { get; set; }
                public required string Description { get; set; }
                public required decimal Price { get; set; }
                public required int Quantity { get; set; }
                public required string Category { get; set; }

                public override Task<Result<CreateResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateResponse>>(new CreateResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0512").Should().BeTrue(
            because: "5+ implicit body properties should trigger PRAG0512");

        var diagnostics = GetDiagnosticsById(result, "PRAG0512").ToList();
        diagnostics.Should().HaveCount(5,
            because: "each implicit body property should produce a separate diagnostic");
    }

    [Fact]
    public void Endpoint_With4ImplicitBodyProperties_DoesNotEmitPRAG0512()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreateResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            public partial class CreateItemEndpoint : Endpoint<CreateResponse>
            {
                // 4 body properties — below threshold, no diagnostic
                public required string Name { get; set; }
                public required string Description { get; set; }
                public required decimal Price { get; set; }
                public required int Quantity { get; set; }

                public override Task<Result<CreateResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateResponse>>(new CreateResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0512").Should().BeFalse(
            because: "fewer than 5 implicit body properties should not trigger PRAG0512");
    }

    [Fact]
    public void Endpoint_WithExplicitFromBody_DoesNotCountAsImplicit()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreateResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            public partial class CreateItemEndpoint : Endpoint<CreateResponse>
            {
                // 3 explicit + 2 implicit = 2 implicit (below threshold)
                [Microsoft.AspNetCore.Mvc.FromBody]
                public required string Name { get; set; }

                [Microsoft.AspNetCore.Mvc.FromBody]
                public required string Description { get; set; }

                [Microsoft.AspNetCore.Mvc.FromBody]
                public required decimal Price { get; set; }

                public required int Quantity { get; set; }
                public required string Category { get; set; }

                public override Task<Result<CreateResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateResponse>>(new CreateResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0512").Should().BeFalse(
            because: "properties with explicit [FromBody] should not count as implicit");
    }

    [Fact]
    public void Endpoint_WithNoBodyProperties_DoesNotEmitPRAG0512()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record GetResponse(int Id);

            [Endpoint(HttpVerb.Get, "/items/{id}")]
            public partial class GetItemEndpoint : Endpoint<GetResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<GetResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<GetResponse>>(new GetResponse(Id));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0512").Should().BeFalse(
            because: "endpoints with no body properties should not trigger PRAG0512");
    }

    [Fact]
    public void Endpoint_WithMixedBindingsAndManyImplicit_EmitsOnlyForImplicit()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreateResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items/{categoryId}")]
            public partial class CreateItemEndpoint : Endpoint<CreateResponse>
            {
                // Route param (not body)
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int CategoryId { get; set; }

                // Query param (not body)
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public string? Filter { get; set; }

                // 5 implicit body properties — triggers PRAG0512
                public required string Name { get; set; }
                public required string Description { get; set; }
                public required decimal Price { get; set; }
                public required int Quantity { get; set; }
                public required string Sku { get; set; }

                public override Task<Result<CreateResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateResponse>>(new CreateResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0512").Should().BeTrue(
            because: "5 implicit body properties should trigger PRAG0512 regardless of other binding types");

        var diagnostics = GetDiagnosticsById(result, "PRAG0512").ToList();
        diagnostics.Should().HaveCount(5,
            because: "only implicit body properties should produce diagnostics, not route/query params");
    }
}
