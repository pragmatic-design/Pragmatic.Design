using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Jobs;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A reserved order nobody confirms gives its stock back when its hold is due, and is
///     cancelled saying why; a confirmed one keeps it; and the expiry is a row, so it happens even when
///     every Stock instance was down at the moment it was due.
/// </summary>
/// <remarks>
///     <para>
///         The suite's holds last <see cref="WarehouseFixture.HoldFor" />. The waits below poll for what
///         each test is about — the order's state, the job's row — with a deadline; the one fixed wait is
///         in the restart test, where the time passing while Stock is down <em>is</em> the premise.
///     </para>
///     <para>
///         The levels are read from one Stock instance, once, after the expiry: the availability cache is
///         each instance's own and the other instance's drop arrives over Redis after the save, so a read
///         before it on the instance that did not run the job could still be served.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class AHoldNobodyConfirmsGivesItsStockBack(WarehouseFixture warehouse)
{
    private static readonly TimeSpan Deadline = WarehouseFixture.HoldFor + TimeSpan.FromSeconds(20);

    [Fact]
    public async Task AReservedOrderLeftAlone_IsCancelled_AndItsStockComesBack()
    {
        var sku = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await PlacedAsync(desk, sku.Sku, 3);

        var cancelled = await EventuallyAsync(async () => await OrderCalls.ReadAsync(desk, order),
            state => state.GetProperty("status").GetString() == "Cancelled");

        cancelled.GetProperty("cancellationReason").GetString().Should().Be("reservation expired");

        var level = (await LevelsAsync(sku.Id)).Should().ContainSingle().Subject;
        level.GetProperty("reserved").GetInt32().Should().Be(0, "the hold was given back");
        level.GetProperty("available").GetInt32().Should().Be(10, "availability is back to its level before the order");
    }

    /// <summary>
    ///     The control: a confirmed order keeps its stock, and its hold's job runs and gives back nothing.
    /// </summary>
    [Fact]
    public async Task AnOrderConfirmedBeforeItsHoldIsDue_KeepsItsStock()
    {
        var sku = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await PlacedAsync(desk, sku.Sku, 3);

        using var confirmed = await desk.PostAsync($"api/orders/{order}/confirm", null);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());

        // The job ran — its row says so — and only then is "nothing happened" an observation.
        await EventuallyAsync(() => JobOfAsync(order), job => job?.Status == JobStatus.Completed);

        (await OrderCalls.ReadAsync(desk, order)).GetProperty("status").GetString().Should().Be("ReadyToPick");
        (await LevelsAsync(sku.Id)).Single().GetProperty("reserved").GetInt32()
            .Should().Be(3, "a confirmed order's hold is not given back");
    }

    [Fact]
    public async Task TheExpiryIsARow_ItHappensAfterEveryStockInstanceWasDownWhenItWasDue()
    {
        var sku = await StockCalls.InStockAsync(warehouse.StockA, 10);
        using var desk = OrderCalls.ThroughTheGateway(warehouse);
        var order = await PlacedAsync(desk, sku.Sku, 4);

        var job = await JobOfAsync(order);
        job.Should().NotBeNull("the expiry is scheduled in the transaction that holds the stock");
        var due = job!.ScheduledFor;

        await warehouse.RestartStockAsync(async () =>
        {
            job.Status.Should().Be(JobStatus.Pending, "read before the stop: nothing had run it yet");

            // Down past the moment the hold was due: whatever held it in memory would be gone now.
            var untilDue = due - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
            if (untilDue > TimeSpan.Zero)
                await Task.Delay(untilDue);

            (await OrderCalls.ReadAsync(desk, order)).GetProperty("status").GetString()
                .Should().Be("Reserved", "with every Stock instance down, nothing gave the hold back");
        });

        await EventuallyAsync(async () => await OrderCalls.ReadAsync(desk, order),
            state => state.GetProperty("status").GetString() == "Cancelled");

        (await LevelsAsync(sku.Id)).Single().GetProperty("reserved").GetInt32().Should().Be(0);
    }

    private static async Task<Guid> PlacedAsync(HttpClient desk, string sku, int quantity)
    {
        using var created = await OrderCalls.DraftAsync(desk, (sku, quantity));
        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var order = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();

        using var placed = await desk.PostAsync($"api/orders/{order}/place", null);
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        return order;
    }

    /// <summary>The expiry job of an order, read from Stock's database: it is correlated by the order's id.</summary>
    private async Task<JobInstance?> JobOfAsync(Guid order)
    {
        await using var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var correlation = order.ToString();

        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<JobInstance>()
            .AsNoTracking()
            .SingleOrDefaultAsync(job => job.CorrelationId == correlation);
    }

    private async Task<JsonElement[]> LevelsAsync(Guid product)
    {
        using var manager = StockCalls.ToInstance(warehouse.StockA, StockCalls.Manager);
        return await StockCalls.LevelsOfAsync(manager, product);
    }

    /// <summary>Reads until <paramref name="done" /> holds, and fails naming the last reading at the deadline.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var value = await read();
            if (done(value))
                return value;

            if (clock.Elapsed > Deadline)
                throw new TimeoutException($"Not reached within {Deadline}; last reading: {Describe(value)}");

            await Task.Delay(250);
        }
    }

    private static string Describe<T>(T value)
        => value is JsonElement json ? json.GetRawText() : value?.ToString() ?? "null";
}
