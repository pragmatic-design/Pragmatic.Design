using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Saga;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;
using Warehouse.Orders.Contracts.Events;
using Warehouse.Orders.Events;
using Warehouse.Orders.Infrastructure.Sagas;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.IntegrationTests;

/// <summary>
///     A message handler that throws, and a saga step that fails, each leave an error in the host's log
///     naming what failed.
/// </summary>
/// <remarks>
///     <para>
///         The lines come from the generated handler pipeline and saga orchestrator. Both used to declare
///         their log methods as <c>[LoggerMessage] partial void</c> and leave the body to the logging
///         generator, which does not see another generator's output: the methods stayed bodiless, the
///         compiler removed every call, and a failure surfaced only as the exception the transport saw.
///     </para>
///     <para>
///         A second Orders instance on the broker's silent virtual host, so nothing else consumes what it
///         is given and its log holds only what this test caused. The handlers are called as the transport
///         calls them: resolved from the container, which hands out the generated pipeline.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class AFailureInTheProcessIsLogged(WarehouseFixture warehouse)
{
    [Fact]
    public async Task AHandlerThatThrows_LeavesAnErrorNamingTheHandlerAndTheMessage()
    {
        await using var orders = warehouse.OrdersWithNobodyToAnswer();
        var unknownOrder = Guid.NewGuid();

        using (var scope = orders.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<RecordOrderPicked>>();

            // No such order: the handler cannot mark it picked, and says so by throwing.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(new RecordOrderPicked(unknownOrder), MessageContext.New()));
        }

        orders.Errors.Entries.Should().Contain(
            entry => entry.Contains("Handler RecordThePickWhenTheProcessAsks failed for message RecordOrderPicked", StringComparison.Ordinal)
                     && entry.Contains(unknownOrder.ToString(), StringComparison.Ordinal),
            "the pipeline logs the failure with the exception that caused it");
    }

    [Fact]
    public async Task ASagaStepThatFails_LeavesAnErrorNamingTheSagaAndTheStep()
    {
        const string failingStep = nameof(OrderFulfilmentSaga.WhenTheOrderIsPicked);
        await using var orders = warehouse.OrdersWithNobodyToAnswer(services =>
        {
            var registered = services.Last(d => d.ServiceType == typeof(ISagaRepository<OrderFulfilmentSaga>));
            services.Remove(registered);
            services.AddScoped<ISagaRepository<OrderFulfilmentSaga>>(sp => new RepositoryThatFailsOneStep<OrderFulfilmentSaga>(
                (ISagaRepository<OrderFulfilmentSaga>)registered.ImplementationFactory!(sp), failingStep));
        });
        var order = Guid.NewGuid();

        using (var scope = orders.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageHandler<OrderReadyToPick>>()
                .HandleAsync(new OrderReadyToPick(order, DateTimeOffset.UtcNow), MessageContext.New());
        }

        using (var scope = orders.Services.CreateScope())
        {
            var picked = scope.ServiceProvider.GetRequiredService<IMessageHandler<OrderPicked>>();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => picked.HandleAsync(new OrderPicked(order, [], DateTimeOffset.UtcNow), MessageContext.New()));
        }

        orders.Errors.Entries.Should().Contain(
            entry => entry.Contains($"Saga OrderFulfilmentSaga step {failingStep} failed", StringComparison.Ordinal)
                     && entry.Contains("Injected by the suite", StringComparison.Ordinal),
            "the orchestrator logs the failed step with its exception");
    }
}
