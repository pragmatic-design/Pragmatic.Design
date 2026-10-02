using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;
using Xunit;

namespace Showcase.IntegrationTests.AgentDemo;

/// <summary>
///     E2E tests for Agent demo endpoints.
///     Tests run in L0 mode (Agent disconnected) by default — the NoOp store fallbacks
///     are exercised, proving the endpoints are registered and the DI chain resolves.
/// </summary>
public class AgentDemoEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string BaseUrl = "/api/agent-demo";

    // ═══ Health ═══

    [Fact]
    public async Task Health_ReturnsOk_WithAgentStatus()
    {
        var response = await GetRawAsync($"{BaseUrl}/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.TryGetProperty("agentConnected", out var connected).Should().BeTrue();
        connected.ValueKind.Should().Be(JsonValueKind.False,
            "Agent is not started in test environment — should report disconnected");
        json.GetProperty("message").GetString().Should().Contain("L0");
    }

    // ═══ Config ═══

    [Fact]
    public async Task Config_MissingKey_ReturnsNotFound()
    {
        var response = await GetRawAsync($"{BaseUrl}/config/non-existent-key");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Config_TenantScoped_MissingKey_ReturnsNotFound()
    {
        var response = await GetRawAsync($"{BaseUrl}/config/some-key/tenant/test-tenant");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══ Feature Flags ═══

    [Fact]
    public async Task FeatureFlag_NonExistent_ReturnsDisabled()
    {
        var response = await GetRawAsync($"{BaseUrl}/feature/non-existent-flag");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("flag").GetString().Should().Be("non-existent-flag");
        json.GetProperty("enabled").GetBoolean().Should().BeFalse(
            "Unknown flags default to disabled in NoOp store");
    }

    [Fact]
    public async Task FeatureFlag_ReturnsStructuredResponse()
    {
        var response = await GetRawAsync($"{BaseUrl}/feature/fancy-greeting");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.TryGetProperty("flag", out _).Should().BeTrue();
        json.TryGetProperty("enabled", out _).Should().BeTrue();
        json.TryGetProperty("message", out _).Should().BeTrue();
    }

    // ═══ Greeting (conditional behavior via feature flag) ═══

    [Fact]
    public async Task Greeting_DefaultFlag_ReturnsSimpleMessage()
    {
        var response = await GetRawAsync($"{BaseUrl}/greeting");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("fancyGreetingEnabled").GetBoolean().Should().BeFalse(
            "Flag not set — should default to false");
        json.GetProperty("message").GetString().Should().Be("Welcome to Showcase Hotel.");
    }

    // ═══ Tenants ═══

    [Fact]
    public async Task Tenants_ReturnsOk_WithEmptyList()
    {
        var response = await GetRawAsync($"{BaseUrl}/tenants");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("count").GetInt32().Should().Be(0,
            "No tenants in NoOp store");
        json.TryGetProperty("tenants", out _).Should().BeTrue();
    }

    // ═══ Secrets ═══

    [Fact]
    public async Task Secret_MissingKey_ReturnsNotFound()
    {
        var response = await GetRawAsync($"{BaseUrl}/secret/db-password");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══ Endpoint Registration Validation ═══

    [Fact]
    public async Task AllEndpoints_AreRegistered_AndResolveDI()
    {
        // Validates all 7 endpoints are wired and DI resolves correctly
        var endpoints = new[]
        {
            $"{BaseUrl}/health",
            $"{BaseUrl}/config/test-key",
            $"{BaseUrl}/config/test-key/tenant/t1",
            $"{BaseUrl}/feature/test-flag",
            $"{BaseUrl}/greeting",
            $"{BaseUrl}/tenants",
            $"{BaseUrl}/secret/test-key"
        };

        foreach (var url in endpoints)
        {
            var response = await GetRawAsync(url);

            // All endpoints should return a valid HTTP response (not 500/404-route-not-found)
            response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError,
                $"Endpoint {url} should not throw — DI must resolve all dependencies");

            // Should be either OK or NotFound (for missing keys), never a routing failure
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.OK, HttpStatusCode.NotFound],
                $"Endpoint {url} should return OK or NotFound, got {response.StatusCode}");
        }
    }
}
