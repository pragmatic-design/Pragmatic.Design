// Pragmatic.Endpoints.Tests - Handler Snapshot Tests
// Snapshot tests for EndpointsSourceGenerator handler generation.
// Each test runs the generator on a source string and verifies the exact generated output.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Snapshot-based tests for the EndpointsSourceGenerator handler generation.
///     These tests verify the exact generated output for all HTTP method handlers,
///     BodyDto generation, DomainAction endpoints, and processor pipelines.
/// </summary>
public class EndpointsHandlerSnapshotTests : EndpointsGeneratorTestBase
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

    #region GET Endpoint Handler Snapshots

    /// <summary>
    ///     Verifies the generated handler for a simple GET endpoint with query parameters.
    /// </summary>
    [Fact]
    public async Task GetEndpoint_WithQueryParameters_GeneratesCorrectHandler()
    {
        var source = CommonUsings + """

            namespace TestApp.Users;

            public record UserResponse(int Id, string Name, string Email);

            [Endpoint(HttpVerb.Get, "/users")]
            [ApiSummary("List Users")]
            [ApiTags("Users")]
            public partial class ListUsersEndpoint : Endpoint<UserResponse[]>
            {
                [Microsoft.AspNetCore.Mvc.FromQuery]
                public required string Query { get; set; }

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public int Page { get; set; } = 1;

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public int PageSize { get; set; } = 10;

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public string? SortBy { get; set; }

                public override Task<Result<UserResponse[]>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse[]>>(Array.Empty<UserResponse>());
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("ListUsersEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region POST Endpoint Handler Snapshots

    /// <summary>
    ///     Verifies the generated handler for a POST endpoint with request body.
    /// </summary>
    [Fact]
    public async Task PostEndpoint_WithRequestBody_GeneratesCorrectHandler()
    {
        var source = CommonUsings + """

            namespace TestApp.Users;

            public record UserResponse(int Id, string Name, string Email);

            [Endpoint(HttpVerb.Post, "/users")]
            [ApiSummary("Create User")]
            [ApiTags("Users")]
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

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateUserEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region PUT Endpoint Handler Snapshots

    /// <summary>
    ///     Verifies the generated handler for a PUT endpoint with route param, body, and DI.
    /// </summary>
    [Fact]
    public async Task PutEndpoint_WithRouteAndBody_GeneratesCorrectHandler()
    {
        var source = CommonUsings + """

            namespace TestApp.Users;

            public interface IUserRepository
            {
                Task<bool> ExistsAsync(int id, CancellationToken ct);
            }

            public record UserResponse(int Id, string Name, string Email);
            public record NotFoundError : IError
            {
                public string Message => "User not found";
                public int StatusCode => 404;
            }

            [Endpoint(HttpVerb.Put, "/users/{id}")]
            [ApiSummary("Update User")]
            [ApiTags("Users")]
            public partial class UpdateUserEndpoint : Endpoint<UserResponse, NotFoundError>
            {
                private IUserRepository _repository = null!;

                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public required string Name { get; set; }

                public required string Email { get; set; }

                public override async Task<Result<UserResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
                {
                    if (!await _repository.ExistsAsync(Id, ct))
                        return new NotFoundError();
                    return new UserResponse(Id, Name, Email);
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("UpdateUserEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");
        sources.Keys.Should().Contain(k => k.EndsWith("UpdateUserEndpoint.SetDependencies.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region DELETE Endpoint Handler Snapshots

    /// <summary>
    ///     Verifies the generated handler for a DELETE endpoint returning void (204 No Content).
    /// </summary>
    [Fact]
    public async Task DeleteEndpoint_VoidResponse_GeneratesCorrectHandler()
    {
        var source = CommonUsings + """

            namespace TestApp.Users;

            public record NotFoundError : IError
            {
                public string Message => "User not found";
                public int StatusCode => 404;
            }

            [Endpoint(HttpVerb.Delete, "/users/{id}")]
            [ApiSummary("Delete User")]
            [ApiTags("Users")]
            public partial class DeleteUserEndpoint : VoidEndpoint<NotFoundError>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<VoidResult<NotFoundError>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult(VoidResult<NotFoundError>.Ok());
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("DeleteUserEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region Multiple Error Types Handler Snapshots

    /// <summary>
    ///     Verifies the generated handler for an endpoint with multiple typed errors.
    ///     Each error type should produce a typed Produces call for OpenAPI discoverability.
    /// </summary>
    [Fact]
    public async Task Endpoint_WithMultipleErrorTypes_GeneratesTypedProduces()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderResponse(int Id, decimal Total);
            public record NotFoundError : IError
            {
                public string Message => "Order not found";
                public int StatusCode => 404;
            }
            public record ValidationError : IError
            {
                public string Message => "Validation failed";
                public int StatusCode => 422;
            }
            public record ConflictError : IError
            {
                public string Message => "Order conflict";
                public int StatusCode => 409;
            }

            [Endpoint(HttpVerb.Put, "/orders/{id}")]
            [ApiSummary("Update Order")]
            [ApiTags("Orders")]
            public partial class UpdateOrderEndpoint : Endpoint<OrderResponse, NotFoundError, ValidationError, ConflictError>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public required decimal Total { get; set; }

                public override Task<Result<OrderResponse, NotFoundError, ValidationError, ConflictError>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse, NotFoundError, ValidationError, ConflictError>>(new OrderResponse(Id, Total));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("UpdateOrderEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region BodyDto Generation Snapshots

    /// <summary>
    ///     Verifies the generated BodyDto record from endpoint properties.
    /// </summary>
    [Fact]
    public async Task BodyDto_WithMultipleProperties_GeneratesCorrectRecord()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderResponse(int Id, decimal Total);

            [Endpoint(HttpVerb.Post, "/orders")]
            [HttpStatus(201)]
            public partial class CreateOrderEndpoint : Endpoint<OrderResponse>
            {
                public required string CustomerId { get; set; }

                public required decimal Total { get; set; }

                public string? Notes { get; set; }

                public required int Quantity { get; set; }

                public override Task<Result<OrderResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse>>(new OrderResponse(1, Total));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateOrderEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region DomainAction Endpoint Handler Snapshots

    /// <summary>
    ///     Verifies the generated handler for a DomainAction-backed endpoint.
    ///     DomainAction endpoints use an invoker pattern instead of direct HandleAsync.
    /// </summary>
    [Fact]
    public async Task DomainActionEndpoint_WithInvoker_GeneratesCorrectHandler()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;

            namespace TestApp.Orders;

            public record OrderResponse(int OrderId, decimal Total);

            [Endpoint(HttpVerb.Post, "/customers/{customerId}/orders")]
            [ApiSummary("Create Order")]
            [ApiTags("Orders")]
            [HttpStatus(201)]
            public partial class CreateOrderEndpoint : DomainAction<OrderResponse>
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public string CustomerId { get; set; } = null!;

                [Microsoft.AspNetCore.Mvc.FromQuery]
                public bool ApplyDiscount { get; set; }

                public override Task<Result<OrderResponse, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse, IError>>(new OrderResponse(1, 99.99m));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateOrderEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated handler for a VoidDomainAction DELETE endpoint.
    /// </summary>
    [Fact]
    public async Task VoidDomainActionEndpoint_Delete_GeneratesNoContentHandler()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;

            namespace TestApp.Orders;

            [Endpoint(HttpVerb.Delete, "/orders/{id}")]
            [ApiSummary("Delete Order")]
            [ApiTags("Orders")]
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

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("DeleteOrderEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion

    #region PreProcessor and PostProcessor Snapshots

    /// <summary>
    ///     Verifies the generated handler for an endpoint with PreProcessor and PostProcessor.
    ///     The generated code should resolve processors from DI and call them in order.
    /// </summary>
    [Fact]
    public async Task Endpoint_WithPreAndPostProcessor_GeneratesProcessorPipeline()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderResponse(int Id, string Status);

            [Endpoint(HttpVerb.Post, "/orders")]
            [PreProcessor<TestApp.Orders.ValidateOrderProcessor>(Order = 0)]
            [PostProcessor<TestApp.Orders.AuditLogProcessor>(Order = 0)]
            [ApiSummary("Create Order")]
            [HttpStatus(201)]
            public partial class CreateOrderWithProcessorsEndpoint : Endpoint<OrderResponse>
            {
                public required string CustomerId { get; set; }

                public override Task<Result<OrderResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse>>(new OrderResponse(1, "created"));
                }
            }

            public class ValidateOrderProcessor : Pragmatic.Endpoints.Processors.IEndpointPreProcessor
            {
                public ValueTask<Pragmatic.Endpoints.Processors.PreProcessorResult> ProcessAsync(
                    Pragmatic.Endpoints.Context.EndpointContext context, CancellationToken ct = default)
                    => ValueTask.FromResult(Pragmatic.Endpoints.Processors.PreProcessorResult.Continue());
            }

            public class AuditLogProcessor : Pragmatic.Endpoints.Processors.IEndpointPostProcessor
            {
                public ValueTask ProcessAsync(
                    Pragmatic.Endpoints.Context.EndpointContext context, object? result, CancellationToken ct = default)
                    => ValueTask.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateOrderWithProcessorsEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        await Verify(sources);
    }

    #endregion
}
