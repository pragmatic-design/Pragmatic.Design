using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     The runtime half of the <c>[InvalidatesCache]</c> domain-event bridge: what the generated
///     handler delegates to once the dispatcher has invoked it.
/// </summary>
/// <remarks>
///     The generated handler carries only the declared data — category, tags, keys. Every decision
///     about where that lands, and what happens when it cannot, is asserted here rather than in the
///     generated code, so it is checked once instead of once per event.
/// </remarks>
public sealed class CacheInvalidationRunnerTests
{
    private sealed class Analytics;

    private readonly CacheStackMock _stack = new();

    private ServiceProvider BuildWithCaching(Action<CachingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        // Registered before AddPragmaticCaching: its TryAddSingleton then leaves this one in place,
        // so the resolver hands out the mock and HybridCache is not needed.
        services.AddSingleton<ICacheStack>(_stack);
        services.AddPragmaticCaching(configure ?? (_ => { }));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InvalidateAsync_WithNothingRegistered_DoesNotThrow()
    {
        // An application that references Pragmatic.Caching but never calls AddPragmaticCaching() must
        // still be able to dispatch an event that declares an invalidation.
        using var sp = new ServiceCollection().BuildServiceProvider();

        await CacheInvalidationRunner.InvalidateAsync(sp, null, ["users"], ["user:1"]);
    }

    [Fact]
    public async Task InvalidateAsync_WithAStackButNoResolver_StillInvalidates()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheStack>(_stack);
        using var sp = services.BuildServiceProvider();

        await CacheInvalidationRunner.InvalidateAsync(sp, null, ["users"], []);

        _stack.InvalidateByTagAsync.Received(1, "users", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_InvalidatesEveryTagAndRemovesEveryKey()
    {
        using var sp = BuildWithCaching();

        await CacheInvalidationRunner.InvalidateAsync(
            sp, null, ["users", "tenant:7"], ["user:1", "user:2"]);

        _stack.InvalidateByTagAsync.Received(1, "users", Arg.Any<CancellationToken>());
        _stack.InvalidateByTagAsync.Received(1, "tenant:7", Arg.Any<CancellationToken>());
        _stack.RemoveAsync.Received(1, "user:1", Arg.Any<CancellationToken>());
        _stack.RemoveAsync.Received(1, "user:2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_WithEventInvalidationDisabled_DoesNothing()
    {
        // CachingOptions.EnableEventInvalidation is the switch the attribute's documentation promises.
        // It must hold on the event path too, not only through the resolver for mutations.
        using var sp = BuildWithCaching(o => o.EnableEventInvalidation = false);

        await CacheInvalidationRunner.InvalidateAsync(sp, null, ["users"], ["user:1"]);

        _stack.InvalidateByTagAsync.DidNotReceive();
        _stack.RemoveAsync.DidNotReceive();
    }

    [Fact]
    public async Task InvalidateAsync_WithAnUnregisteredCategory_FallsBackToTheDefaultStack()
    {
        // Same fallback both sides use: entries for a category nobody called ForCategory<T>() for were
        // written to the default stack, so that is where they have to be invalidated.
        using var sp = BuildWithCaching();

        await CacheInvalidationRunner.InvalidateAsync(sp, typeof(Analytics), ["reports"], []);

        _stack.InvalidateByTagAsync.Received(1, "reports", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_OneFailingTag_StillAttemptsTheRest_ThenThrowsAggregate()
    {
        // Stopping at the first failure would leave entries the caller believes are gone — stale reads
        // until they expire, and nothing said about it.
        using var sp = BuildWithCaching();
        _stack.InvalidateByTagAsync
            .When("broken", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("backend down"));

        var act = () => CacheInvalidationRunner.InvalidateAsync(
            sp, null, ["broken", "users"], ["user:1"]);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Count.Should().Be(1);

        _stack.InvalidateByTagAsync.Received(1, "users", Arg.Any<CancellationToken>());
        _stack.RemoveAsync.Received(1, "user:1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_WhenCancelled_PropagatesInsteadOfAggregating()
    {
        // IDomainEventHandler documents cancellation as aborting the dispatch chain; wrapping it in an
        // AggregateException would turn an abort into a handler failure.
        using var sp = BuildWithCaching();
        _stack.InvalidateByTagAsync
            .When("users", Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        var act = () => CacheInvalidationRunner.InvalidateAsync(sp, null, ["users"], []);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
