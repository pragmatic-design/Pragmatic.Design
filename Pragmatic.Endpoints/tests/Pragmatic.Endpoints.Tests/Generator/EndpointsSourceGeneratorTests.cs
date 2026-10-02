using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for EndpointsSourceGenerator covering all binding scenarios.
///     Note: These tests verify generated source content without full compilation
///     since ASP.NET Core references are complex to set up in generator tests.
/// </summary>
public class EndpointsSourceGeneratorTests : EndpointsGeneratorTestBase
{
    // =========================================================================
    // Basic Endpoint Tests
    // =========================================================================

    [Fact]
    public void SimpleEndpoint_GeneratesHandler()
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
            public partial class GetUserEndpoint : Endpoint<UserResponse>
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

        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.EndsWith("GetUserEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().Contain("MapGet");
        handlerSource.Should().Contain("/users/{id}");
        handlerSource.Should().Contain("int id");
    }

    // =========================================================================
    // Route Parameter Tests
    // =========================================================================

    [Fact]
    public void Endpoint_WithRouteParameters_GeneratesCorrectBinding()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public record OrderResponse(Guid OrderId, string CustomerId);

            [Endpoint(HttpVerb.Get, "/customers/{customerId}/orders/{orderId}")]
            public partial class GetOrderEndpoint : Endpoint<OrderResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public string CustomerId { get; set; } = null!;

                [Microsoft.AspNetCore.Mvc.FromRoute]
                public Guid OrderId { get; set; }

                public override Task<Result<OrderResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse>>(new OrderResponse(OrderId, CustomerId));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("string customerId");
        handlerSource.Should().Contain("global::System.Guid orderId");
        // Object initializer syntax in generated code
        handlerSource.Should().Contain("CustomerId = customerId");
        handlerSource.Should().Contain("OrderId = orderId");
    }

    // =========================================================================
    // Query Parameter Tests
    // =========================================================================

    [Fact]
    public void Endpoint_WithRequiredQueryParameter_GeneratesCorrectBinding()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Search;

            public record SearchResponse(string Query, int Count);

            [Endpoint(HttpVerb.Get, "/search")]
            public partial class SearchEndpoint : Endpoint<SearchResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public required string Query { get; set; }

                public override Task<Result<SearchResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<SearchResponse>>(new SearchResponse(Query, 10));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("string query");
        // Required query params go in object initializer
        handlerSource.Should().Contain("Query = query");
    }

    [Fact]
    public void Endpoint_WithOptionalQueryParameter_GeneratesNullableType()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Users;

            public record UsersResponse(int Page, int PageSize);

            [Endpoint(HttpVerb.Get, "/users")]
            public partial class ListUsersEndpoint : Endpoint<UsersResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public int Page { get; set; } = 1;

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public int PageSize { get; set; } = 10;

                public override Task<Result<UsersResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UsersResponse>>(new UsersResponse(Page, PageSize));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Optional value types should be nullable with default
        handlerSource.Should().Contain("int?");
        handlerSource.Should().Contain("= null;");
        // And should use .Value when assigning
        handlerSource.Should().Contain(".Value");
    }

    [Fact]
    public void Endpoint_WithOptionalStringQueryParameter_NoValueAccess()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Search;

            public record SearchResponse(string Query, string? Filter);

            [Endpoint(HttpVerb.Get, "/search")]
            public partial class FilterSearchEndpoint : Endpoint<SearchResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public required string Query { get; set; }

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public string? Filter { get; set; }

                public override Task<Result<SearchResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<SearchResponse>>(new SearchResponse(Query, Filter));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // String is reference type - no .Value needed
        handlerSource.Should().Contain("if (filter is not null) endpoint.Filter = filter;");
        handlerSource.Should().NotContain("filter.Value");
    }

    // =========================================================================
    // Header Parameter Tests
    // =========================================================================

    [Fact]
    public void Endpoint_WithRequiredHeader_GeneratesFromHeaderAttribute()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Api;

            public record ApiResponse(string ApiKey);

            [Endpoint(HttpVerb.Get, "/protected")]
            public partial class ProtectedEndpoint : Endpoint<ApiResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Api-Key")]
                public required string ApiKey { get; set; }

                public override Task<Result<ApiResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ApiResponse>>(new ApiResponse(ApiKey));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("RequestValues.Header(httpContext, \"X-Api-Key\")");
        handlerSource.Should().Contain("string apiKey");
    }

    [Fact]
    public void Endpoint_WithOptionalHeader_GeneratesNullableParameter()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Api;

            public record TracingResponse(string? CorrelationId);

            [Endpoint(HttpVerb.Get, "/traced")]
            public partial class TracedEndpoint : Endpoint<TracingResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Correlation-Id")]
                public string? CorrelationId { get; set; }

                public override Task<Result<TracingResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<TracingResponse>>(new TracingResponse(CorrelationId));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("RequestValues.Header(httpContext, \"X-Correlation-Id\")");
        handlerSource.Should().Contain("string?");
        // An optional string needs no parsing: absent simply means null, and the handler's own
        // null-check decides. Only the typed kinds get a parse step in front of them.
        handlerSource.Should().NotContain("TryBindString(__raw_correlationId");
    }

    // =========================================================================
    // Body Parameter Tests
    // =========================================================================

    [Fact]
    public void Endpoint_WithBodyProperties_GeneratesHandler()
    {
        // Note: Body property detection relies on full type information from base class Endpoint<T>.
        // In test compilation without full ASP.NET Core context, we verify the handler is generated correctly.
        // The BodyDto generation is tested via the samples project which has full compilation context.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Users;

            public record UserResponse(int Id, string Name, string Email);

            [Endpoint(HttpVerb.Post, "/users")]
            [HttpStatus(201)]
            public partial class CreateUserEndpoint : Endpoint<UserResponse>
            {
                public required string Name { get; set; }

                public required string Email { get; set; }

                public override Task<Result<UserResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse>>(new UserResponse(1, Name, Email));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Handler should be generated with the correct HTTP method and route
        handlerSource.Should().Contain("MapPost");
        handlerSource.Should().Contain("/users");
        // Should produce 201 status code
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(201, typeof(global::TestApp.Users.UserResponse))");
    }

    // =========================================================================
    // DI Injection Tests
    // =========================================================================

    [Fact]
    public void Endpoint_WithPrivateFields_GeneratesSetDependencies()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public interface IOrderRepository
            {
                Task<OrderEntity?> GetByIdAsync(int id, CancellationToken ct);
            }

            public record OrderEntity(int Id, decimal Total);
            public record OrderResponse(int Id, decimal Total);
            public record NotFoundError : IError
            {
                public string Message => "Not found";
                public int StatusCode => 404;
            }

            [Endpoint(HttpVerb.Get, "/orders/{id}")]
            public partial class GetOrderEndpoint : Endpoint<OrderResponse, NotFoundError>
            {
                private IOrderRepository _repository = null!;

                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override async Task<Result<OrderResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
                {
                    var order = await _repository.GetByIdAsync(Id, ct);
                    if (order is null) return new NotFoundError();
                    return new OrderResponse(order.Id, order.Total);
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.EndsWith("GetOrderEndpoint.SetDependencies.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var setDepsSource = GetGeneratedSource(result, "SetDependencies");
        setDepsSource.Should().Contain("internal void SetDependencies");
        setDepsSource.Should().Contain("IOrderRepository");

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().Contain("endpoint.SetDependencies(");
    }

    // =========================================================================
    // DomainAction Endpoint Tests
    // =========================================================================

    [Fact]
    public void DomainActionEndpoint_GeneratesInvokerHandler()
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

            public record CreateOrderResponse(int OrderId);

            [Endpoint(HttpVerb.Post, "/orders")]
            [HttpStatus(201)]
            public partial class CreateOrderEndpoint : DomainAction<CreateOrderResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromBody]
                public required decimal Total { get; init; }

                public override Task<Result<CreateOrderResponse, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateOrderResponse, IError>>(new CreateOrderResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // DomainAction uses invoker pattern
        handlerSource.Should().Contain("IDomainActionInvoker");
        handlerSource.Should().Contain("invoker.InvokeAsync");
        handlerSource.Should().Contain("result.Match");
    }

    [Fact]
    public void VoidDomainActionEndpoint_Returns204OnSuccess()
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
            public partial class DeleteOrderEndpoint : VoidDomainAction
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
        handlerSource.Should().Contain("NoContent()");
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(204)");
    }

    // =========================================================================
    // Mixed Bindings Test (Comprehensive)
    // =========================================================================

    [Fact]
    public void Endpoint_WithAllBindings_GeneratesCorrectHandler()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Comprehensive;

            public interface IOrderService
            {
                Task<OrderEntity> CreateAsync(CreateOrderRequest request, CancellationToken ct);
            }

            public record OrderEntity(Guid Id, string CustomerId, decimal Total);
            public record CreateOrderRequest(string CustomerId, decimal Total, string? Notes);
            public record OrderResponse(Guid OrderId, string CustomerId, decimal Total);

            [Endpoint(HttpVerb.Post, "/customers/{customerId}/orders")]
            [ApiSummary("Create Order")]
            [ApiTags("Orders")]
            [HttpStatus(201)]
            public partial class CreateOrderEndpoint : Endpoint<OrderResponse>
            {
                // DI
                private IOrderService _orderService = null!;

                // Route
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public string CustomerId { get; set; } = null!;

                // Header (required)
                [Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Idempotency-Key")]
                public required string IdempotencyKey { get; set; }

                // Header (optional)
                [Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Correlation-Id")]
                public string? CorrelationId { get; set; }

                // Query (optional value type)
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public bool ApplyDiscount { get; set; }

                // Query (optional reference type)
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public string? PromoCode { get; set; }

                // Body (using set instead of init for test compilation)
                public required decimal Total { get; set; }

                public string? Notes { get; set; }

                public override async Task<Result<OrderResponse>> HandleAsync(CancellationToken ct = default)
                {
                    var order = await _orderService.CreateAsync(
                        new CreateOrderRequest(CustomerId, Total, Notes), ct);
                    return new OrderResponse(order.Id, order.CustomerId, order.Total);
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);

        // Should have Handler and SetDependencies
        // The body DTO IS generated here — see BodyDefaultValueTests, which asserts on it, including the
        // initializer on a defaulted property.
        sources.Keys.Should().Contain(k => k.EndsWith("CreateOrderEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");
        sources.Keys.Should().Contain(k => k.EndsWith("CreateOrderEndpoint.SetDependencies.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var handlerSource = sources.First(kvp => kvp.Key.EndsWith("CreateOrderEndpoint.Endpoint.g.cs")).Value;

        // Route parameter
        handlerSource.Should().Contain("string customerId");

        // Required header with correct name extraction from attribute
        handlerSource.Should().Contain("RequestValues.Header(httpContext, \"X-Idempotency-Key\")");
        handlerSource.Should().Contain("string idempotencyKey");

        // Optional header (should come after required params)
        handlerSource.Should().Contain("RequestValues.Header(httpContext, \"X-Correlation-Id\")");
        // Generator uses = default for optional params
        handlerSource.Should().Contain("= null;");

        // Query params with nullable types
        handlerSource.Should().Contain("bool?");
        handlerSource.Should().Contain("string?");

        // SetDependencies call for DI
        handlerSource.Should().Contain("endpoint.SetDependencies(");

        // Parameter ordering: required before optional
        var requiredHeaderIndex = handlerSource.IndexOf("string idempotencyKey");
        var optionalHeaderIndex = handlerSource.IndexOf("string? correlationId");
        requiredHeaderIndex.Should().BeLessThan(optionalHeaderIndex,
            because: "required parameters must come before optional ones");
    }

    // =========================================================================
    // Diagnostic Tests
    // =========================================================================

    [Fact]
    public void Endpoint_NonPartialClass_NoGeneratedFiles()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record Response(int Value);

            // Missing 'partial' keyword
            [Endpoint(HttpVerb.Get, "/test")]
            public class NonPartialEndpoint : Endpoint<Response>
            {
                public override Task<Result<Response>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<Response>>(new Response(1));
                }
            }
            """;

        var result = RunGenerator(source);

        // Generator filters non-partial classes in predicate
        var sources = GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Any(k => k.Contains("NonPartialEndpoint")).Should().BeFalse();
    }

    [Fact]
    public void Endpoint_NotInheritingBase_EmitsError()
    {
        var source = """
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;

            namespace TestApp;

            // Does not inherit from Endpoint<T> or DomainAction
            [Endpoint(HttpVerb.Get, "/test")]
            public partial class InvalidEndpoint
            {
                public string Execute() => "invalid";
            }
            """;

        var result = RunGenerator(source);

        // Should emit PRAG0501 (NotEndpoint) error
        HasDiagnostic(result, "PRAG0501").Should().BeTrue();
    }

    // =========================================================================
    // Registration Tests
    // =========================================================================

    [Fact]
    public void MultipleEndpoints_GeneratesRegistration()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Api;

            public record UserResponse(int Id);
            public record OrderResponse(int Id);

            [Endpoint(HttpVerb.Get, "/users/{id}")]
            public partial class GetUserEndpoint : Endpoint<UserResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<UserResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<UserResponse>>(new UserResponse(Id));
            }

            [Endpoint(HttpVerb.Get, "/orders/{id}")]
            public partial class GetOrderEndpoint : Endpoint<OrderResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<Result<OrderResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderResponse>>(new OrderResponse(Id));
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);

        // Should generate registration extension (file name: {Namespace}.Pragmatic.EndpointsRegistration.g.cs)
        sources.Keys.Should().Contain(k => k.Contains("Endpoints.Registration"));

        var registrationSource = sources.First(s => s.Key.Contains("Endpoints.Registration")).Value;
        registrationSource.Should().Contain("MapPragmaticEndpoints");
        registrationSource.Should().Contain("GetUserEndpoint.MapEndpoint");
        registrationSource.Should().Contain("GetOrderEndpoint.MapEndpoint");
    }

    // =========================================================================
    // Error Status Code Resolution Tests
    // =========================================================================

    [Fact]
    public void CustomErrorType_WithStatusCodeLiteral_ResolvesFromSyntax()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Payments;

            public record PaymentResponse(string TransactionId);

            /// Custom error with non-standard 429 status code (Too Many Requests).
            public record RateLimitedError : IError
            {
                public string Code => "RATE_LIMITED";
                public int StatusCode => 429;
                public string? Title => "Too Many Requests";
                public LocalizationKey TitleKey => default;
                public LocalizationKey DescriptionKey => default;
            }

            [Endpoint(HttpVerb.Post, "/payments")]
            [ApiSummary("Process Payment")]
            public partial class ProcessPaymentEndpoint : Endpoint<PaymentResponse, RateLimitedError>
            {
                public required decimal Amount { get; init; }

                public override Task<Result<PaymentResponse, RateLimitedError>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<PaymentResponse, RateLimitedError>>(new PaymentResponse("txn_123"));
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();

        // Should resolve 429 from the literal expression, not fallback to 400
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(429, typeof(global::TestApp.Payments.RateLimitedError))");
    }
}
