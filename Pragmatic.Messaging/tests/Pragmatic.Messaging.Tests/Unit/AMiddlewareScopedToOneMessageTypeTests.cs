using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     What <c>[MessageMiddleware(ForMessageType = typeof(T))]</c> means once the generator has written
///     the registration: the middleware runs for that message and steps aside for every other.
/// </summary>
/// <remarks>
///     ⚠️ "Steps aside" and not "is skipped": the wrapper still has to call <c>next</c>, or scoping a
///     middleware to one type would stop every other message from being handled at all. That is the
///     assertion below with a control, because a wrapper that swallowed the pipeline would satisfy
///     "the middleware did not run" perfectly.
/// </remarks>
public class AMiddlewareScopedToOneMessageTypeTests
{
    private static readonly MessageContext Context = MessageContext.New();

    [Fact]
    public async Task ForItsOwnMessage_ItRuns()
    {
        var inner = new RecordingMiddleware();
        var scoped = new MessageTypeScopedMiddleware(inner, typeof(OrderPlaced));
        var handled = false;

        await scoped.InvokeAsync(new OrderPlaced(), Context, () =>
        {
            handled = true;
            return Task.CompletedTask;
        });

        inner.Ran.Should().BeTrue();
        handled.Should().BeTrue("the middleware wraps the handler, it does not replace it");
    }

    /// <summary>
    ///     The control: another message reaches its handler, untouched.
    /// </summary>
    /// <remarks>
    ///     Without it, "the middleware did not run" is satisfied by a wrapper that swallows the
    ///     continuation — every other message silently unhandled, and nothing to see.
    /// </remarks>
    [Fact]
    public async Task ForAnotherMessage_ItStepsAsideAndTheHandlerStillRuns()
    {
        var inner = new RecordingMiddleware();
        var scoped = new MessageTypeScopedMiddleware(inner, typeof(OrderPlaced));
        var handled = false;

        await scoped.InvokeAsync(new OrderCancelled(), Context, () =>
        {
            handled = true;
            return Task.CompletedTask;
        });

        inner.Ran.Should().BeFalse();
        handled.Should().BeTrue("stepping aside means calling next, not dropping the message");
    }

    /// <summary>
    ///     ⚠️ The exact type, not an assignable one.
    /// </summary>
    /// <remarks>
    ///     A middleware declared for a base type would otherwise run for every derived message with no
    ///     way to scope it back down, and a message type in this framework is a contract rather than a
    ///     place in a hierarchy.
    /// </remarks>
    [Fact]
    public async Task ForADerivedMessage_ItDoesNotRun()
    {
        var inner = new RecordingMiddleware();
        var scoped = new MessageTypeScopedMiddleware(inner, typeof(OrderPlaced));

        await scoped.InvokeAsync(new UrgentOrderPlaced(), Context, () => Task.CompletedTask);

        inner.Ran.Should().BeFalse();
    }

    /// <summary>Scoping a middleware does not move it in the chain.</summary>
    [Fact]
    public void TheOrder_IsTheWrappedMiddlewares()
    {
        var scoped = new MessageTypeScopedMiddleware(new RecordingMiddleware { Order = 42 }, typeof(OrderPlaced));

        scoped.Order.Should().Be(42);
    }

    private sealed class RecordingMiddleware : IMessageMiddleware
    {
        public bool Ran { get; private set; }

        public int Order { get; init; }

        public Task InvokeAsync<T>(
            T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
            where T : notnull
        {
            Ran = true;
            return next();
        }
    }

    private record OrderPlaced;

    private sealed record UrgentOrderPlaced : OrderPlaced;

    private sealed record OrderCancelled;
}
