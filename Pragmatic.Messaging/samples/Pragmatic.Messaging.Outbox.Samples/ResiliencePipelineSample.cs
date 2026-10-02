using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     SG-generated resilience pipeline. A handler decorated with
///     <c>[MessageHandler]</c> plus <c>[Retry]</c> / <c>[Timeout]</c> /
///     <c>[CircuitBreaker]</c> triggers the source generator to emit a nested
///     <c>Pipeline</c> class (a partial member of the handler) whose
///     <c>ExecuteAsync</c> re-invokes the inner handler through the configured
///     policies. In a full Pragmatic host the generated registration swaps this
///     wrapper in for the raw handler; here we construct
///     <c>FlakyChargeHandler.Pipeline</c> directly to prove the retry loop is
///     real and re-invokes on failure.
///
///     The generated source lands in obj/.../Pragmatic.SourceGenerator/ —
///     EmitCompilerGeneratedFiles surfaces it next to this file for inspection.
///     The ctor is (handler, ILogger&lt;Pipeline&gt;,
///     IEnumerable&lt;IMessageMiddleware&gt;, IIdempotencyStore? = null); we pass
///     a null store + empty middleware so only the retry policy runs.
/// </summary>
public static class ResilienceSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- SG resilience pipeline ([Retry] re-invocation) ---");

        var inner = new FlakyChargeHandler { FailBeforeSuccess = 2 };

        // The generated ctor includes an ICallContext? parameter when the build
        // references Pragmatic.Authorization (auth bypass during handler calls).
        // Pass null + a null idempotency store + empty middleware so only the
        // retry policy runs.
        var pipeline = new FlakyChargeHandler.Pipeline(
            inner,
            NullLogger<FlakyChargeHandler.Pipeline>.Instance,
            idempotencyStore: null,
            middlewares: Array.Empty<IMessageMiddleware>(),
            callContext: null);

        await pipeline.ExecuteAsync(
            new ChargeCard(Guid.NewGuid(), 75.00m),
            MessageContext.New(correlationId: "pay-9"),
            CancellationToken.None);

        Console.WriteLine("  configured max attempts  : 4 (see [Retry] on FlakyChargeHandler)");
        Console.WriteLine($"  handler invocations      : {inner.Attempts} (2 failures + 1 success)");
        Console.WriteLine($"  charge succeeded         : {inner.Succeeded}");
        Console.WriteLine();
    }
}

/// <summary>
///     Carrier for the resilience demo. Implements <see cref="IDomainEvent" />
///     because the SG bridges every [MessageHandler] message type to an
///     IDomainEventHandler adapter (Events integration), which constrains the
///     message type to IDomainEvent.
/// </summary>
public sealed record ChargeCard(Guid CardId, decimal Amount) : IDomainEvent
{
    // IDomainEvent has default-implemented EventId/OccurredAt; expose OccurredAt
    // as a concrete record property so the message carries a stable timestamp.
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
///     Fails its first <see cref="FailBeforeSuccess" /> invocations, then
///     succeeds — stands in for a transient downstream fault that retry absorbs.
///     <c>partial</c> so the SG can add the nested <c>Pipeline</c>;
///     parameterless-constructible so the SG-generated DI registration works.
/// </summary>
[MessageHandler]
[Retry(MaxAttempts = 4, BaseDelayMs = 10, Strategy = BackoffStrategy.Exponential)]
public sealed partial class FlakyChargeHandler : IMessageHandler<ChargeCard>
{
    public int FailBeforeSuccess { get; init; }
    public int Attempts { get; private set; }
    public bool Succeeded { get; private set; }

    public Task HandleAsync(ChargeCard message, MessageContext context, CancellationToken ct)
    {
        Attempts++;
        if (Attempts <= FailBeforeSuccess)
            throw new InvalidOperationException($"transient gateway fault (attempt {Attempts})");

        Succeeded = true;
        return Task.CompletedTask;
    }
}
