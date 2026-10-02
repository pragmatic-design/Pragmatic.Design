using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     ApiRoutes (Tier 1 of the typed testing surface): per-boundary route constants and
///     typed URL builders generated from the endpoint models; name collisions warn (PRAG0526).
/// </summary>
public class ApiRoutesGeneratorTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void ApiRoutes_RouteAndQueryParams_GenerateTypedBuilder()
    {
        var source = CommonUsings + """

            namespace Shop.Orders.Endpoints;

            public class OrderDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/orders/{id}")]
            public partial class GetOrderEndpoint : Endpoint<OrderDto>
            {
                [FromRoute] public Guid Id { get; set; }
                [FromQuery] public bool IncludeLines { get; set; }
                [FromQuery] public string? Search { get; set; }

                public override Task<Result<OrderDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<OrderDto>.Success(new OrderDto()));
            }
            """;

        var result = RunGenerator(source);

        var routes = GetGeneratedSource(result, "_Infra.Endpoints.Routes");
        routes.Should().NotBeNull();
        routes.Should().Contain("public static class ApiRoutes");
        routes.Should().Contain("public static class Orders");
        routes.Should().Contain("public const string GetOrderMethod = \"GET\";");
        routes.Should().Contain("public const string GetOrderTemplate = \"/api/orders/{id}\";");
        routes.Should().Contain("public static string GetOrder(");
        routes.Should().Contain("{__Fmt(id)}");
        routes.Should().Contain("includeLines");
        routes.Should().Contain("if (search is not null)");
    }

    [Fact]
    public void ApiRoutes_NameCollision_ReportsPrag0526()
    {
        var source = CommonUsings + """

            namespace Shop.Orders.Endpoints;

            public class OrderDto { public string Name { get; set; } = ""; }

            [Endpoint(HttpVerb.Get, "/api/orders")]
            public partial class ListOrdersEndpoint : Endpoint<OrderDto>
            {
                public override Task<Result<OrderDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<OrderDto>.Success(new OrderDto()));
            }

            [Endpoint(HttpVerb.Post, "/api/orders/search")]
            public partial class ListOrdersAction : Endpoint<OrderDto>
            {
                public override Task<Result<OrderDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<OrderDto>.Success(new OrderDto()));
            }
            """;

        var result = RunGenerator(source);

        // Both types collapse to "ListOrders" after suffix stripping.
        HasDiagnostic(result, "PRAG0526").Should().BeTrue();
    }
}
