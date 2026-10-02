using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Verifies that [RateLimit] configuration propagates correctly to DomainAction handler templates.
/// </summary>
public class RateLimitOnDomainActionTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void DomainAction_WithRateLimitPolicy_GeneratesRequireRateLimiting()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            [RateLimit(Policy = "order-creation")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string ProductName { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("IDomainActionInvoker");
        handlerSource.Should().Contain("RequireRateLimiting(\"order-creation\")");
    }

    [Fact]
    public void DomainAction_WithoutRateLimit_DoesNotGenerateRateLimiting()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderNoLimitAction : DomainAction<OrderId>
            {
                public required string ProductName { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().NotContain("RequireRateLimiting");
    }

    [Fact]
    public void InlineRateLimit_PolicyName_DerivesFromFullyQualifiedIdentity()
    {
        // Inline policy names must derive from the fully-qualified identity, not the
        // simple TypeName — otherwise same-named endpoints in different namespaces would share
        // a single policy and their limits could conflict. The namespace must be part of the name.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Alpha;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/alpha/orders")]
            [RateLimit(Requests = 5, Window = "1m")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string ProductName { get; init; }
                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
            }
            """;

        var result = RunGenerator(source);

        var combined = string.Join("\n", GetGeneratedSourcesAsDictionary(result).Values);

        // Namespace-qualified, collision-free policy name...
        combined.Should().Contain("__pragmatic_ratelimit_TestApp_Alpha_PlaceOrderAction");
        // ...not the old simple-name scheme that collided across namespaces.
        combined.Should().NotContain("\"__pragmatic_ratelimit_PlaceOrderAction\"");
    }

    [Fact]
    public void VoidDomainAction_WithRateLimitPolicy_GeneratesRequireRateLimiting()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            [Endpoint(HttpVerb.Delete, "/orders/{id}")]
            [RateLimit(Policy = "delete-limit")]
            public partial class DeleteOrderAction : VoidDomainAction
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult(VoidResult<IError>.Ok());
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("IVoidDomainActionInvoker");
        handlerSource.Should().Contain("RequireRateLimiting(\"delete-limit\")");
    }
}
