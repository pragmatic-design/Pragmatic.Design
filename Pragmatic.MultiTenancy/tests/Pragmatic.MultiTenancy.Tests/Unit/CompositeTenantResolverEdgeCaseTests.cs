using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Edge-case coverage for <see cref="CompositeTenantResolver"/> beyond the scenarios
///     in <see cref="CompositeTenantResolverTests"/>: empty/whitespace skipping, single
///     resolver, cancellation propagation, and resolution ordering.
/// </summary>
public class CompositeTenantResolverEdgeCaseTests
{
    private static readonly NullLogger<CompositeTenantResolver> Logger = new();

    [Fact]
    public async Task EmptyStringResult_IsTreatedAsUnresolved_FallsThrough()
    {
        var resolvers = new ITenantResolver[]
        {
            new FixedResolver(""),
            new SingleTenantResolver("real")
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("real");
    }

    [Fact]
    public async Task SingleResolver_ReturnsItsValue()
    {
        var composite = new CompositeTenantResolver([new SingleTenantResolver("only")], Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("only");
    }

    [Fact]
    public async Task LaterResolversNotConsulted_AfterFirstSuccess()
    {
        var probe = new RecordingResolver("nope");
        var resolvers = new ITenantResolver[]
        {
            new SingleTenantResolver("winner"),
            probe
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("winner");
        probe.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task FailingResolversBeforeSuccess_AreAllSkipped()
    {
        var resolvers = new ITenantResolver[]
        {
            new ThrowingResolver(),
            new FixedResolver(null),
            new ThrowingResolver(),
            new SingleTenantResolver("survivor")
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("survivor");
    }

    [Fact]
    public async Task OperationCanceledException_PropagatesAndIsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        var resolvers = new ITenantResolver[]
        {
            new CancellationObservingResolver(),
            new SingleTenantResolver("should-not-reach")
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var act = async () => await composite.ResolveAsync(cts.Token).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CancellationToken_IsForwardedToInnerResolvers()
    {
        using var cts = new CancellationTokenSource();
        var probe = new TokenCapturingResolver();
        var composite = new CompositeTenantResolver([probe], Logger);

        await composite.ResolveAsync(cts.Token).ConfigureAwait(true);

        probe.ReceivedToken.Should().Be(cts.Token);
    }

    private sealed class FixedResolver(string? value) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(value);
    }

    private sealed class RecordingResolver(string? value) : ITenantResolver
    {
        public bool WasCalled { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.FromResult(value);
        }
    }

    private sealed class ThrowingResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Resolver failure");
    }

    private sealed class CancellationObservingResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>("unreachable");
        }
    }

    private sealed class TokenCapturingResolver : ITenantResolver
    {
        public CancellationToken ReceivedToken { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            return ValueTask.FromResult<string?>(null);
        }
    }
}
