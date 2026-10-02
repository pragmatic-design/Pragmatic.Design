using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Integration.Tests.Domain.Models;
using Pragmatic.Integration.Tests.Infrastructure;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     End-to-end integration tests for the full Pragmatic pipeline:
///     HTTP Request -> Endpoint -> DomainAction -> Validation -> Persistence -> Response.
///     Each test gets its own factory/DB instance for full isolation.
/// </summary>
/// <remarks>
///     ⚠️ A validator that refuses a request answers 422, not 400: 400 is for a request that could not
///     be read at all — malformed JSON, a string where a number goes — and collapsing the two leaves
///     every client unable to tell its own bug from a message it should show the person. Binding
///     failures answer 400.
/// </remarks>
public sealed class OrderPipelineTests : IAsyncLifetime
{
    private IntegrationTestFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task InitializeAsync()
    {
        _factory = new IntegrationTestFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // =========================================================================
    // POST /api/orders — Happy Path
    // =========================================================================

    [Fact]
    public async Task PostOrder_WithValidData_Returns201WithOrderResponse()
    {
        // Arrange: valid name and positive amount via query parameters
        var response = await _client.PostAsync("/api/orders?name=TestOrder&amount=99.99", null);

        // Assert: 201 Created with typed response
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        order.Should().NotBeNull();
        order!.Name.Should().Be("TestOrder");
        order.Amount.Should().Be(99.99m);
        order.Status.Should().Be("Created");
        order.OrderId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task PostOrder_WithValidData_PersistsToDatabase()
    {
        // Arrange & Act
        var response = await _client.PostAsync("/api/orders?name=PersistedOrder&amount=50.00", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        order.Should().NotBeNull();

        // Verify persistence: read directly from the DbContext
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var saved = await db.Orders.FirstOrDefaultAsync(o => o.Id == order!.OrderId);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("PersistedOrder");
        saved.Amount.Should().Be(50.00m);
        saved.Status.Should().Be("Created");
    }

    // =========================================================================
    // POST /api/orders — Validation Failures
    // =========================================================================

    [Fact]
    public async Task PostOrder_WithMissingName_Returns422ValidationError()
    {
        // Arrange: no name parameter, amount is valid
        var response = await _client.PostAsync("/api/orders?amount=10.00", null);

        // Assert: 422 — the request was understood and the rules refuse it
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
    }

    [Fact]
    public async Task PostOrder_WithNegativeAmount_Returns422ValidationError()
    {
        // Arrange: valid name but negative amount
        var response = await _client.PostAsync("/api/orders?name=ValidName&amount=-5.00", null);

        // Assert: 422 — the request was understood and the rules refuse it
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
    }

    [Fact]
    public async Task PostOrder_WithZeroAmount_Returns422ValidationError()
    {
        // Arrange: valid name but zero amount (not positive)
        var response = await _client.PostAsync("/api/orders?name=ValidName&amount=0", null);

        // Assert: 422 — the request was understood and the rules refuse it
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
    }

    // =========================================================================
    // GET /api/orders/{id} — Happy Path
    // =========================================================================

    [Fact]
    public async Task GetOrder_WithExistingId_Returns200WithOrderDetail()
    {
        // Arrange: create an order first
        var createResponse = await _client.PostAsync("/api/orders?name=GetTestOrder&amount=75.00", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        created.Should().NotBeNull();

        // Act: retrieve the created order
        var getResponse = await _client.GetAsync($"/api/orders/{created!.OrderId}");

        // Assert: 200 OK with full detail
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResponse.Content.ReadFromJsonAsync<OrderDetailResponse>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.OrderId.Should().Be(created.OrderId);
        detail.Name.Should().Be("GetTestOrder");
        detail.Amount.Should().Be(75.00m);
        detail.Status.Should().Be("Created");
        detail.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    // =========================================================================
    // GET /api/orders/{id} — Not Found
    // =========================================================================

    [Fact]
    public async Task GetOrder_WithNonExistentId_Returns404NotFoundError()
    {
        // Arrange: random GUID that doesn't exist
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/orders/{nonExistentId}");

        // Assert: 404 Not Found with ProblemDetails
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(404);
        problem.Title.Should().Be("Resource Not Found");
    }

    // =========================================================================
    // DI Resolution
    // =========================================================================

    [Fact]
    public void DiResolution_AllServicesResolve_NoExceptions()
    {
        // Verify that the full DI container can resolve all registered services
        // without throwing. This catches any wiring issues at startup.
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        // DbContext resolves
        var db = provider.GetService<TestDbContext>();
        db.Should().NotBeNull();

        // DomainActionInvoker for CreateOrderAction resolves
        var createInvoker = provider.GetService<
            Pragmatic.Actions.Invoker.IDomainActionInvoker<
                Domain.Actions.CreateOrderAction,
                Domain.Models.OrderResponse>>();
        createInvoker.Should().NotBeNull();

        // DomainActionInvoker for GetOrderAction resolves
        var getInvoker = provider.GetService<
            Pragmatic.Actions.Invoker.IDomainActionInvoker<
                Domain.Actions.GetOrderAction,
                Domain.Models.OrderDetailResponse>>();
        getInvoker.Should().NotBeNull();
    }

    // =========================================================================
    // Validation Runs Before Execution
    // =========================================================================

    [Fact]
    public async Task PostOrder_WithInvalidData_DoesNotPersist()
    {
        // Arrange: send invalid data (no name, no amount)
        var response = await _client.PostAsync("/api/orders", null);

        // Assert: should be a validation error, NOT a successful creation
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Verify nothing was persisted for this specific invalid request
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // The order with empty name should not exist in the database
        // (validation should block before Execute runs)
        var emptyNameOrders = await db.Orders.Where(o => o.Name == string.Empty || o.Name == null!).ToListAsync();
        emptyNameOrders.Should().BeEmpty("validation should prevent persistence of invalid orders");
    }
}
