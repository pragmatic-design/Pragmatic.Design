using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Audit;
using Pragmatic.Messaging.Auditing;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     <see cref="AuditMiddleware"/> against the framework audit trail: what it records on success and
///     failure, and the contract that an audit write never masks a handler exception.
/// </summary>
public class AuditMiddlewarePipelineTests
{
    public sealed record OrderPlaced(Guid OrderId);

    /// <summary>Captures what the middleware writes, without a database.</summary>
    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public Exception? ThrowOnRecord { get; set; }

        public ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
        {
            if (ThrowOnRecord is { } ex)
                throw ex;

            Entries.Add(entry);
            return default;
        }
    }

    private static AuditMiddleware Create(IAuditTrail trail)
        => new(trail, NullLogger<AuditMiddleware>.Instance);

    [Fact]
    public void Order_IsMinus100_SoItRunsOutermost()
        => Create(new RecordingTrail()).Order.Should().Be(-100);

    [Fact]
    public async Task WhenTheHandlerSucceeds_ItRecordsAHandledEntry()
    {
        var trail = new RecordingTrail();
        var context = MessageContext.New(correlationId: "corr-1", tenantId: "t1");

        await Create(trail).InvokeAsync(new OrderPlaced(Guid.NewGuid()), context, () => Task.CompletedTask);

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be(AuditCategory.Message);
        entry.Operation.Should().Be("Messaging.MessageHandled");
        entry.Outcome.Should().Be(AuditOutcome.Success);
        entry.CorrelationId.Should().Be("corr-1");
        entry.TenantId.Should().Be("t1");
        entry.TargetType.Should().Contain(nameof(OrderPlaced));
    }

    [Fact]
    public async Task WhenTheHandlerThrows_ItRecordsTheFailureAndRethrows()
    {
        var trail = new RecordingTrail();
        MessageHandlerDelegate next = () => throw new InvalidOperationException("boom");

        var act = () => Create(trail).InvokeAsync(new OrderPlaced(Guid.NewGuid()), MessageContext.New(), next);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be(AuditOutcome.Failed);
        entry.Operation.Should().Be("Messaging.MessageFailed");
        // Type plus first line only: a full exception message routinely echoes payload values.
        entry.Detail.Should().Be("InvalidOperationException: boom");
    }

    [Fact]
    public async Task AMultiLineExceptionMessage_IsReducedToItsFirstLine()
    {
        var trail = new RecordingTrail();
        MessageHandlerDelegate next = () => throw new InvalidOperationException("first line\nsecond line");

        var act = () => Create(trail).InvokeAsync(new OrderPlaced(Guid.NewGuid()), MessageContext.New(), next);
        await act.Should().ThrowAsync<InvalidOperationException>();

        trail.Entries[0].Detail.Should().Be("InvalidOperationException: first line");
    }

    [Fact]
    public async Task AFailingAuditWrite_DoesNotMaskTheHandlerException()
    {
        // A throwing finally replaces the in-flight exception per the C# spec, which would hide the
        // real failure from callers and logs behind a storage problem.
        var trail = new RecordingTrail { ThrowOnRecord = new IOException("trail unavailable") };
        MessageHandlerDelegate next = () => throw new InvalidOperationException("boom");

        var act = () => Create(trail).InvokeAsync(new OrderPlaced(Guid.NewGuid()), MessageContext.New(), next);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task AFailingAuditWrite_DoesNotFailASuccessfulHandler()
    {
        var trail = new RecordingTrail { ThrowOnRecord = new IOException("trail unavailable") };

        var act = () => Create(trail).InvokeAsync(
            new OrderPlaced(Guid.NewGuid()), MessageContext.New(), () => Task.CompletedTask);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NoEntryCarriesTheMessageItself()
    {
        // The reason this module stopped owning its own trail: the old entry had a PayloadJson field
        // holding the serialized message, personal data included. There is no field for it now, and
        // this pins that nothing reintroduces one through the detail.
        var trail = new RecordingTrail();
        var orderId = Guid.NewGuid();

        await Create(trail).InvokeAsync(new OrderPlaced(orderId), MessageContext.New(), () => Task.CompletedTask);

        trail.Entries[0].Detail.Should().BeNull();
        trail.Entries[0].TargetId.Should().NotBe(orderId.ToString());
    }

    [Fact]
    public async Task ItCallsNextExactlyOnce()
    {
        var calls = 0;

        await Create(new RecordingTrail()).InvokeAsync(
            new OrderPlaced(Guid.NewGuid()), MessageContext.New(), () =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            });

        calls.Should().Be(1);
    }
}
