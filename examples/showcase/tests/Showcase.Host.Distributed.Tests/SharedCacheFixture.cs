using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     Three instances of <c>Showcase.Host.Distributed</c> on one database: two that declare the
///     Redis invalidation broadcast and one that does not.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Cacheable]</c> keeps its answer <b>in process</b>, one copy per host, so an
///         invalidation run on one host reaches its own copy and nobody else's. That is what
///         <c>AddRedisCacheInvalidationBroadcast</c> is for, and this topology — more than one host
///         serving the same module — is the only place in the repository where it can be shown.
///     </para>
///     <para>
///         ⚠️ <b>The third host is the control and it is not optional.</b> It runs the same code with
///         no <c>ConnectionStrings:Redis</c>, so the broadcast is not registered. Without it, "the
///         other host serves the new answer" would also be satisfied by a cache that never held
///         anything — by a `[Cacheable]` that does not cache, by a TTL that expired, by a key that
///         differs per instance. The control shows the stale answer that the broadcast removes.
///     </para>
///     <para>
///         ⚠️ The hosts are started <b>one at a time</b>: each runs <c>UseDatabaseEnsureCreated()</c>
///         in Development, and three of those racing on one empty database is a race this fixture has
///         no reason to run.
///     </para>
/// </remarks>
public sealed class SharedCacheFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("showcase_shared_cache")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WebApplicationFactory<Program>? _first;
    private WebApplicationFactory<Program>? _second;
    private WebApplicationFactory<Program>? _alone;

    /// <summary>The host that caches first, and whose copy the other one has to be able to drop.</summary>
    public WebApplicationFactory<Program> First => Started(_first);

    /// <summary>The host that writes, and whose invalidation has to travel.</summary>
    public WebApplicationFactory<Program> Second => Started(_second);

    /// <summary>The control: same code, no broadcast declared.</summary>
    public WebApplicationFactory<Program> Alone => Started(_alone);

    public async Task InitializeAsync()
    {
        await _database.StartAsync().ConfigureAwait(false);
        await _redis.StartAsync().ConfigureAwait(false);

        var connectionString = _database.GetConnectionString();
        var redis = _redis.GetConnectionString();

        _first = await StartedAsync(new CacheNodeFactory(connectionString, redis)).ConfigureAwait(false);
        _second = await StartedAsync(new CacheNodeFactory(connectionString, redis)).ConfigureAwait(false);
        _alone = await StartedAsync(new CacheNodeFactory(connectionString, redis: null)).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        foreach (var host in new[] { _first, _second, _alone })
            if (host is not null)
                await host.DisposeAsync().ConfigureAwait(false);

        await _redis.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>A client carrying what the host asks for before an endpoint runs: tenant and caller.</summary>
    public static HttpClient ClientOf(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "shared-cache-test");
        client.DefaultRequestHeaders.Add("X-User-Name", "Shared cache test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*");
        return client;
    }

    /// <summary>
    ///     Builds the host and waits for it to answer, which is what makes the next one's
    ///     <c>EnsureCreated</c> a no-op instead of a second writer on an empty database.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Deliberately not a <c>[Cacheable]</c> route.</b> The first version warmed up on
    ///     <c>api/properties/search</c> and cached the empty answer on all three hosts before any test
    ///     had created a row — so the case's first assertion failed on a cache the fixture itself had
    ///     filled. A warm-up must prove the host answers without deciding what its cache holds.
    /// </remarks>
    private static async Task<WebApplicationFactory<Program>> StartedAsync(WebApplicationFactory<Program> host)
    {
        using var client = ClientOf(host);
        using var response = await client.GetAsync(new Uri("api/guests/search", UriKind.Relative))
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"the host did not start: api/guests/search answered {(int)response.StatusCode}");

        return host;
    }

    private static WebApplicationFactory<Program> Started(WebApplicationFactory<Program>? host)
        => host ?? throw new InvalidOperationException("The fixture has not been initialised.");

    private sealed class CacheNodeFactory(string connectionString, string? redis)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = connectionString,
                ["DetailedErrors"] = "true",
                ["Pragmatic:RemoteBoundaries:Showcase.Billing:BaseUrl"] = "http://billing.invalid",
                ["Pragmatic:MaintenanceMode:EnableOnStartupFailure"] = "false"
            };

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

            // The whole difference between a node and the control — and it goes through UseSetting,
            // not through the collection above.
            //
            // ⚠️ `ConfigureAppConfiguration` is applied when the host is built, and `Program.cs` reads
            // this key **eagerly**, while composing, to decide whether to register the broadcast at
            // all. Through the collection the key is not there yet: the decorator is never
            // registered, and the case fails as if the channel did not carry. `UseSetting` lands in
            // the builder's configuration straight away, which is what a setting that chooses a
            // service needs.
            if (redis is not null)
                builder.UseSetting("ConnectionStrings:Redis", redis);

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
        }
    }
}
