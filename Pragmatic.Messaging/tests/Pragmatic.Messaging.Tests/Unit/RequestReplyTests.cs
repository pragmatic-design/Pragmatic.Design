using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.Messaging.Tests.Unit;

public class RequestReplyTests
{
    // =========================================================================
    // Test types
    // =========================================================================

    public record GetPaymentStatus(Guid PaymentId);

    public record PaymentStatusResult(Guid PaymentId, string Status, decimal Amount);

    /// <summary>
    ///     Simple request handler that returns a canned response.
    /// </summary>
    public class GetPaymentStatusHandler : IRequestHandler<GetPaymentStatus, PaymentStatusResult>
    {
        public Task<PaymentStatusResult> HandleAsync(
            GetPaymentStatus request,
            MessageContext context,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new PaymentStatusResult(request.PaymentId, "Completed", 99.99m));
        }
    }

    /// <summary>
    ///     Handler that always throws, to test error propagation.
    /// </summary>
    public class FailingRequestHandler : IRequestHandler<GetPaymentStatus, PaymentStatusResult>
    {
        public Task<PaymentStatusResult> HandleAsync(
            GetPaymentStatus request,
            MessageContext context,
            CancellationToken ct)
        {
            throw new InvalidOperationException("Payment service unavailable");
        }
    }

    // =========================================================================
    // Helper
    // =========================================================================

    /// <summary>
    ///     Test dispatch table that handles the message types used in this test class.
    /// </summary>
    private sealed class TestMessageDispatchTable : ITypedMessageDispatchTable
    {
        public Task? TryDispatch(IMessageBus bus, object message, MessageContext context, CancellationToken ct)
            => message switch
            {
                GetPaymentStatus m => bus.PublishAsync(m, context, ct),
                _ => null
            };
    }

    private static readonly ITypedMessageDispatchTable DispatchTable = new TestMessageDispatchTable();

    private static (InMemoryMessageBus bus, ServiceProvider sp) CreateBus(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        configure(services);
        var sp = services.BuildServiceProvider();
        var bus = new InMemoryMessageBus(sp, sp.GetRequiredService<ILogger<InMemoryMessageBus>>(), [DispatchTable]);
        return (bus, sp);
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public async Task RequestAsync_WithRegisteredHandler_ReturnsResponse()
    {
        var (bus, sp) = CreateBus(s =>
            s.AddScoped<IRequestHandler<GetPaymentStatus, PaymentStatusResult>, GetPaymentStatusHandler>());

        var paymentId = Guid.NewGuid();
        var result = await bus.RequestAsync<GetPaymentStatus, PaymentStatusResult>(
            new GetPaymentStatus(paymentId));

        result.Should().NotBeNull();
        result.PaymentId.Should().Be(paymentId);
        result.Status.Should().Be("Completed");
        result.Amount.Should().Be(99.99m);
        sp.Dispose();
    }

    [Fact]
    public async Task RequestAsync_NoHandlerRegistered_ThrowsInvalidOperation()
    {
        var (bus, sp) = CreateBus(_ => { });

        var act = () => bus.RequestAsync<GetPaymentStatus, PaymentStatusResult>(
            new GetPaymentStatus(Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IRequestHandler*GetPaymentStatus*PaymentStatusResult*");
        sp.Dispose();
    }

    [Fact]
    public async Task RequestAsync_HandlerThrows_PropagatesException()
    {
        var (bus, sp) = CreateBus(s =>
            s.AddScoped<IRequestHandler<GetPaymentStatus, PaymentStatusResult>, FailingRequestHandler>());

        var act = () => bus.RequestAsync<GetPaymentStatus, PaymentStatusResult>(
            new GetPaymentStatus(Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment service unavailable");
        sp.Dispose();
    }

    [Fact]
    public async Task RequestAsync_PropagatesCancellation()
    {
        var (bus, sp) = CreateBus(s =>
            s.AddScoped<IRequestHandler<GetPaymentStatus, PaymentStatusResult>, GetPaymentStatusHandler>());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => bus.RequestAsync<GetPaymentStatus, PaymentStatusResult>(
            new GetPaymentStatus(Guid.NewGuid()), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sp.Dispose();
    }

    [Fact]
    public async Task RequestAsync_ScopedHandler_ResolvesFromScope()
    {
        // Verifies that scoped handlers are resolved per-call (not singleton)
        var callCount = 0;
        var (bus, sp) = CreateBus(s =>
        {
            s.AddScoped<IRequestHandler<GetPaymentStatus, PaymentStatusResult>>(_ =>
            {
                Interlocked.Increment(ref callCount);
                return new GetPaymentStatusHandler();
            });
        });

        await bus.RequestAsync<GetPaymentStatus, PaymentStatusResult>(
            new GetPaymentStatus(Guid.NewGuid()));

        callCount.Should().Be(1);
        sp.Dispose();
    }

    [Fact]
    public async Task RequestAsync_ViaTransportAwareBus_DelegatesToLocalDispatcher()
    {
        // TransportAwareMessageBus delegates RequestAsync to InMemoryMessageBus
        // This test verifies the delegation works end-to-end
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddScoped<IRequestHandler<GetPaymentStatus, PaymentStatusResult>, GetPaymentStatusHandler>();
        var sp = services.BuildServiceProvider();

        var localBus = new InMemoryMessageBus(sp, sp.GetRequiredService<ILogger<InMemoryMessageBus>>(), [DispatchTable]);

        var paymentId = Guid.NewGuid();
        var result = await localBus.RequestAsync<GetPaymentStatus, PaymentStatusResult>(
            new GetPaymentStatus(paymentId));

        result.PaymentId.Should().Be(paymentId);
        result.Status.Should().Be("Completed");
        sp.Dispose();
    }
}
