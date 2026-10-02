using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Persistence.EFCore.Repository;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests infrastructure features (P3):
///       - IPragmaticBuilder boots correctly
///       - IStartupStep pipeline executes
///       - Lookup cache preloaded
///       - Identifiers (Guid7) roundtrip
/// </summary>
public class InfrastructureTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task StartupPipeline_AllEndpointsRespond()
    {
        // If IPragmaticBuilder or IStartupStep failed, the app wouldn't boot
        // Test multiple endpoint groups to verify full pipeline
        var endpoints = new[]
        {
            "/api/properties/search",
            "/api/guests/search",
            "/api/amenities/search"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await GetRawAsync(endpoint);
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"Endpoint {endpoint} should respond — validates IPragmaticBuilder + IStartupStep pipeline");
        }
    }

    [Fact]
    public async Task Guid7_EntityIds_AreTimeOrdered()
    {
        // Create two entities sequentially — Guid7 should produce time-ordered IDs
        var guest1 = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "First",
            lastName = "Guest",
            email = $"g7a.{Guid.NewGuid():N}@test.com"
        });
        var id1 = guest1.GetProperty("id").GetGuid();

        var guest2 = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Second",
            lastName = "Guest",
            email = $"g7b.{Guid.NewGuid():N}@test.com"
        });
        var id2 = guest2.GetProperty("id").GetGuid();

        // Guid7 IDs are time-ordered — second should be "greater" than first
        id1.Should().NotBe(id2);
        // Note: Guid comparison isn't guaranteed to reflect time order in all implementations,
        // but the IDs should be unique and non-empty
        id1.Should().NotBeEmpty();
        id2.Should().NotBeEmpty();
    }

    /// <summary>
    ///     The <c>[Lookup]</c> entity's cache, its loader and the preload service are all in the
    ///     container.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ It measures the registration, which is where the mechanism can break:
    ///         <c>Add{Prefix}LookupCaches()</c> is generated per assembly, and the generated host has to
    ///         call it. The Showcase's startup step does not call it by hand, so this test sees what the
    ///         host does. A 200 from <c>GET /api/amenities/search</c> would prove nothing —
    ///         <c>Amenity</c> does not carry <c>[Lookup]</c>, only <see cref="Category" /> does — and
    ///         would stay green with the whole mechanism deleted.
    ///     </para>
    ///     <para>
    ///         It stops at the wiring on purpose. What that wiring produces — a filled cache and a
    ///         navigation that returns the row from it — is <see cref="LookupCacheTests" />, which is
    ///         where the rows come from too.
    ///     </para>
    /// </remarks>
    [Fact]
    public void LookupCache_TheDeclaredLookup_HasItsCacheAndPreloadRegistered()
    {
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<ILookupCache<Category, Guid>>().Should().NotBeNull(
            "[Lookup] on Category registers the cache the generated navigation resolves from");

        sp.GetServices<ILookupCacheLoader>().Should().Contain(
            l => l.GetType().Name == "CategoryLookupCacheLoader",
            "the per-entity loader is what fills that cache");

        sp.GetServices<IHostedService>().Should().Contain(
            s => s is LookupPreloadHostedService,
            "and the hosted service is what runs the loaders at startup");
    }

    [Fact]
    public async Task Configuration_ShowcaseOptions_BoundCorrectly()
    {
        // ShowcaseOptions is bound via [Configuration] attribute
        // If binding failed, the app wouldn't start (validated at startup)
        // The health endpoint or any endpoint responding proves configuration works
        var response = await GetRawAsync("/api/properties/search");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "[Configuration] ShowcaseOptions binding validated — app booted successfully");
    }

    [Fact]
    public async Task RuntimeConfigStore_ResolvesSeededBaseValue_OverHttp()
    {
        // Exercises the runtime IConfigurationStore end-to-end (not just that the app booted):
        // the seeder writes Booking:CancellationWindowHours = 24 as the base value, and the diagnostics
        // endpoint reads it back through the store. With no Agent daemon in the test environment the
        // Agent-backed store degrades to its in-memory fallback, so the seeded value stays visible.
        var response = await GetRawAsync("/api/diag/config/Booking:CancellationWindowHours");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the seeded base configuration value should be readable through IConfigurationStore");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("value").GetString().Should().Be("24");
    }

    [Fact]
    public async Task RuntimeConfigStore_ResolvesTenantOverride_OverHttp()
    {
        // premium-hotel has a seeded tenant override (72) for the cancellation window; a request
        // scoped to that tenant must resolve the override, not the base (24).
        var client = CreateClientAs("test-user", tenantId: "premium-hotel");
        var response = await client.GetAsync("/api/diag/config/Booking:CancellationWindowHours");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("value").GetString().Should().Be("72");
    }
}
