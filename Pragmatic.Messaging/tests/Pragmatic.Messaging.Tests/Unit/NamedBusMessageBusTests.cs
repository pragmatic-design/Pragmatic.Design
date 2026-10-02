using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

#pragma warning disable PRAGMSG_MULTIBUS // Multi-bus is preview; exercised intentionally under test.

public class NamedBusMessageBusTests
{
    public sealed record Ping(string Value);

    /// <summary>Records which calls landed on this bus, for routing assertions.</summary>
    private sealed class RecordingBus(string name) : IMessageBus
    {
        public string Name { get; } = name;
        public List<string> PublishCalls { get; } = [];
        public List<string> SendCalls { get; } = [];
        public List<string> DispatchCalls { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
        {
            PublishCalls.Add(message.ToString() ?? "");
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        {
            PublishCalls.Add(message.ToString() ?? "");
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
        {
            SendCalls.Add(message.ToString() ?? "");
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        {
            SendCalls.Add(message.ToString() ?? "");
            return Task.CompletedTask;
        }

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull
            where TResponse : notnull
            => Task.FromResult(default(TResponse)!);

        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
        {
            DispatchCalls.Add(message.ToString() ?? "");
            return Task.CompletedTask;
        }

        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        {
            PublishCalls.Add(message.ToString() ?? "");
            return Task.CompletedTask;
        }
    }

    private static (NamedBusMessageBus composite, RecordingBus defaultBus, RecordingBus analytics) Create()
    {
        var defaultBus = new RecordingBus("default");
        var analytics = new RecordingBus("analytics");
        var composite = new NamedBusMessageBus(
            defaultBus,
            new Dictionary<string, IMessageBus> { ["analytics"] = analytics });
        return (composite, defaultBus, analytics);
    }

    /// <summary>
    ///     Builds a context carrying the <c>bus.name</c> routing header that
    ///     <see cref="NamedBusMessageBus"/> reads from <see cref="MessageContext.Headers"/>.
    /// </summary>
    private static MessageContext ContextForBus(string busName)
        => MessageContext.New() with { Headers = new Dictionary<string, string> { ["bus.name"] = busName } };

    [Fact]
    public async Task PublishAsync_WithBusNameHeader_RoutesToNamedBus()
    {
        var (composite, defaultBus, analytics) = Create();
        var context = ContextForBus("analytics");

        await composite.PublishAsync(new Ping("a"), context);

        analytics.PublishCalls.Should().ContainSingle();
        defaultBus.PublishCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WithoutHeader_RoutesToDefaultBus()
    {
        var (composite, defaultBus, analytics) = Create();

        await composite.PublishAsync(new Ping("a"), MessageContext.New());

        defaultBus.PublishCalls.Should().ContainSingle();
        analytics.PublishCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WithUnknownBusName_FallsBackToDefaultBus()
    {
        var (composite, defaultBus, analytics) = Create();
        var context = ContextForBus("does-not-exist");

        await composite.PublishAsync(new Ping("a"), context);

        defaultBus.PublishCalls.Should().ContainSingle();
        analytics.PublishCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WithBusNameProperty_RoutesToNamedBus()
    {
        // The documented MessageContext.BusName property must route through the composite, not just
        // the bus.name header; ignored, it would silently send to the default bus.
        var (composite, defaultBus, analytics) = Create();
        var context = MessageContext.New() with { BusName = "analytics" };

        await composite.PublishAsync(new Ping("a"), context);

        analytics.PublishCalls.Should().ContainSingle();
        defaultBus.PublishCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_HeaderWinsOverBusNameProperty()
    {
        // The explicit header is the primary knob; the property is only the fallback.
        var (composite, defaultBus, analytics) = Create();
        var context = MessageContext.New() with
        {
            BusName = "does-not-exist",
            Headers = new Dictionary<string, string> { ["bus.name"] = "analytics" },
        };

        await composite.PublishAsync(new Ping("a"), context);

        analytics.PublishCalls.Should().ContainSingle();
        defaultBus.PublishCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_WithBusNameHeader_RoutesToNamedBus()
    {
        var (composite, defaultBus, analytics) = Create();
        var context = ContextForBus("analytics");

        await composite.SendAsync(new Ping("a"), context);

        analytics.SendCalls.Should().ContainSingle();
        defaultBus.SendCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WithBusNameHeader_RoutesToNamedBus()
    {
        var (composite, defaultBus, analytics) = Create();
        var context = ContextForBus("analytics");

        await composite.DispatchAsync(new Ping("a"), context);

        analytics.DispatchCalls.Should().ContainSingle();
        defaultBus.DispatchCalls.Should().BeEmpty();
    }

    [Fact]
    public void GetBus_WithKnownName_ReturnsNamedBus()
    {
        var (composite, _, analytics) = Create();

        composite.GetBus("analytics").Should().BeSameAs(analytics);
    }

    [Fact]
    public void GetBus_WithNull_ReturnsDefaultBus()
    {
        var (composite, defaultBus, _) = Create();

        composite.GetBus(null).Should().BeSameAs(defaultBus);
    }

    [Fact]
    public void GetBus_WithUnknownName_ReturnsDefaultBus()
    {
        var (composite, defaultBus, _) = Create();

        composite.GetBus("nope").Should().BeSameAs(defaultBus);
    }

    [Fact]
    public void DefaultBusResolver_GetBusName_AlwaysRoutesToDefaultBus()
    {
        var resolver = DefaultBusResolver.Instance;

        // The default resolver maps every handler to the default bus (null bus name).
        resolver.GetBusName("Some.Handler.Type").Should().BeNull();
        resolver.GetBusName(typeof(NamedBusMessageBusTests).FullName!).Should().BeNull();
    }
}

#pragma warning restore PRAGMSG_MULTIBUS
