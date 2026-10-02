using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Conformance.Sales;
using Conformance.Sales.Entities;
using Conformance.Sales.Infrastructure.Services;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.EFCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The database restarts under a running host, and the first request after the restart succeeds.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ After a restart the server has closed every idle connection in the pool. Without a retrying
///         execution strategy the first request on each would answer <b>500</b>; so the generated
///         registration passes the provider's options with retry on, and <c>UseDatabase</c> is the
///         application's place to change them.
///     </para>
///     <para>
///         A container of its own, on a fixed port: the restart closes every pooled connection from the
///         server side, as in production, and a restart in the shared container would make the other cases
///         order-dependent.
///     </para>
/// </remarks>
public class TheDatabaseRestarts(RestartablePostgresFixture database) : IClassFixture<RestartablePostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task AfterARestart_TheFirstReadAndTheFirstWriteSucceed()
    {
        await using var factory = new ConformanceWebFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var code = $"RST-{Guid.NewGuid():N}"[..16];
        var created = await client.PostAsJsonAsync("/api/shipment-ids", new { trackingCode = code, carrier = "ParcelCo" }, Json);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // The pool now holds an idle connection the restart will close from the server side.
        (await client.GetAsync($"/api/shipment-ids/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await database.RestartAsync();

        var firstRead = await client.GetAsync($"/api/shipment-ids/{id}");
        firstRead.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the first read after the restart retries the closed connection instead of answering 500: {await firstRead.Content.ReadAsStringAsync()}");

        await database.RestartAsync();

        var firstWrite = await client.PostAsJsonAsync("/api/shipment-ids",
            new { trackingCode = $"RSW-{Guid.NewGuid():N}"[..16], carrier = "ParcelCo" }, Json);
        firstWrite.StatusCode.Should().Be(HttpStatusCode.Created,
            $"and so does the first write: {await firstWrite.Content.ReadAsStringAsync()}");
    }

    /// <summary>The re-execution control: on the happy path the transactional body runs once.</summary>
    [Fact]
    public async Task ATransactionalBody_RunsItsExternalEffectOnce_OnTheHappyPath()
    {
        await using var factory = new ConformanceWebFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var code = $"TXN-{Guid.NewGuid():N}"[..16];
        var response = await client.PostAsJsonAsync("/api/shipments/transactional", new { trackingCode = code, carrier = "ParcelCo" }, Json);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"the transactional operation succeeds inside the strategy: {body}");
        factory.Services.GetRequiredService<ExternalEffectCounter>().Count.Should().Be(1,
            "without transient faults the strategy runs the unit once");

        var id = JsonDocument.Parse(body).RootElement.GetGuid();
        (await client.GetAsync($"/api/shipment-ids/{id}")).StatusCode.Should().Be(HttpStatusCode.OK,
            "and the row written inside the transaction is committed");
    }

    [Fact]
    public async Task TheGeneratedRegistration_RetriesOnFailure()
    {
        await using var factory = new ConformanceWebFactory(database.ConnectionString);

        RetriesOnFailure(factory).Should().BeTrue("retry is on by default for a server provider");
    }

    /// <summary><c>UseDatabase</c> is applied after the generated configuration: it can add and switch off.</summary>
    [Fact]
    public async Task UseDatabase_IsApplied_AfterTheGeneratedConfiguration()
    {
        await using var factory = new ConformanceWebFactory(database.ConnectionString, services =>
            services.AddBoundary<SalesBoundary>(cfg => cfg.UseLocal().UseDatabase(options =>
            {
                options.EnableSensitiveDataLogging();
                options.UseNpgsql(npgsql => npgsql.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)));
            })));

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<SalesDbContext>>();

        options.FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled.Should().BeTrue(
            "the option the application passes to UseDatabase reaches the DbContext");
        RetriesOnFailure(factory).Should().BeFalse(
            "and since it is applied after the generated one, the application can switch retry off");
    }

    private static bool RetriesOnFailure(ConformanceWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.CreateExecutionStrategy().RetriesOnFailure;
    }
}
