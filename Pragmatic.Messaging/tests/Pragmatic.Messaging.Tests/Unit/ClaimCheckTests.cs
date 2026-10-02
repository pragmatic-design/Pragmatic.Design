using System.Collections.Concurrent;
using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.ClaimCheck;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Tests.Unit;

public class ClaimCheckTests
{
    [Fact]
    public void EnableClaimCheck_RegistersStoreAndOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Storage.IFileStorage>(new NullFileStorage());
        new MessagingBuilder(services).EnableClaimCheck(o => o.Threshold = 1024);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<ClaimCheckOptions>().Threshold.Should().Be(1024);
        sp.GetRequiredService<IClaimCheckStore>().Should().BeOfType<FileStorageClaimCheckStore>();
    }

    // ── Tenant isolation ──

    [Fact]
    public async Task Store_WithTenant_PrefixesBlobPathWithTenant()
    {
        var storage = new RecordingFileStorage();
        var store = new FileStorageClaimCheckStore(storage, NullLogger<FileStorageClaimCheckStore>.Instance,
            new StubTenant("tenant-a"));

        var reference = await store.StoreAsync(AsStream(Encoding.UTF8.GetBytes("secret")));

        reference.Should().Contain($"/{FileStorageClaimCheckStore.Container}/tenant-a/");
    }

    [Fact]
    public async Task Retrieve_SameTenant_ReturnsPayload()
    {
        var storage = new RecordingFileStorage();
        var store = new FileStorageClaimCheckStore(storage, NullLogger<FileStorageClaimCheckStore>.Instance,
            new StubTenant("tenant-a"));

        var reference = await store.StoreAsync(AsStream(Encoding.UTF8.GetBytes("secret")));

        (await ReadStringAsync(await store.RetrieveAsync(reference))).Should().Be("secret");
    }

    [Fact]
    public async Task Retrieve_CrossTenantReference_IsRejected()
    {
        var storage = new RecordingFileStorage();
        var reference = await new FileStorageClaimCheckStore(storage,
            NullLogger<FileStorageClaimCheckStore>.Instance, new StubTenant("tenant-a"))
            .StoreAsync(AsStream(Encoding.UTF8.GetBytes("secret")));

        // Tenant B replays tenant A's reference on the wire — must be denied.
        var storeB = new FileStorageClaimCheckStore(storage,
            NullLogger<FileStorageClaimCheckStore>.Instance, new StubTenant("tenant-b"));
        Func<Task> act = () => storeB.RetrieveAsync(reference);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Store_NoTenant_UsesFlatPath_AndRoundTrips()
    {
        var storage = new RecordingFileStorage();
        var store = new FileStorageClaimCheckStore(storage, NullLogger<FileStorageClaimCheckStore>.Instance,
            tenantContext: null);

        var reference = await store.StoreAsync(AsStream(Encoding.UTF8.GetBytes("plain")));
        reference.Should().Contain($"/{FileStorageClaimCheckStore.Container}/");
        reference.Should().NotContain("tenant");
        (await ReadStringAsync(await store.RetrieveAsync(reference))).Should().Be("plain");
    }

    [Fact]
    public async Task Publish_PayloadAboveThreshold_OffloadsToStoreAndStampsHeader()
    {
        var (bus, transport, store) = CreateBus(threshold: 64);

        await bus.PublishAsync(new ClaimCheckProbe(new string('x', 500)));

        transport.LastPayload!.Value.Length.Should().Be(0, "the wire payload must be an empty stub");
        transport.LastContext!.Headers.Should().ContainKey(IClaimCheckStore.HeaderName);
        var reference = transport.LastContext.Headers![IClaimCheckStore.HeaderName];
        store.Blobs.Should().ContainKey(reference);
        Encoding.UTF8.GetString(store.Blobs[reference]).Should().Contain("xxx");
    }

    [Fact]
    public async Task Publish_PayloadBelowThreshold_PassesThroughUntouched()
    {
        var (bus, transport, store) = CreateBus(threshold: 64 * 1024);

        await bus.PublishAsync(new ClaimCheckProbe("small"));

        transport.LastPayload!.Value.Length.Should().BeGreaterThan(0);
        (transport.LastContext!.Headers?.ContainsKey(IClaimCheckStore.HeaderName) ?? false).Should().BeFalse();
        store.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_TransportFails_ReclaimsOrphanClaimCheckBlob()
    {
        // The payload is offloaded BEFORE the transport publish; a publish failure must not
        // leave the blob orphaned at rest (nothing consumes it, DeleteAfterConsume defaults to false).
        var services = new ServiceCollection();
        services.AddPragmaticMessaging();
        using var sp = services.BuildServiceProvider();
        var store = new FakeClaimCheckStore();
        var bus = new TransportAwareMessageBus(
            new ThrowingTransport(),
            new DefaultMessageRouter(),
            new JsonMessageSerializer(),
            new InMemoryMessageBus(sp, NullLogger<InMemoryMessageBus>.Instance, []),
            NullLogger<TransportAwareMessageBus>.Instance,
            claimCheckStore: store,
            claimCheckOptions: new ClaimCheckOptions { Threshold = 64 });

        Func<Task> act = () => bus.PublishAsync(new ClaimCheckProbe(new string('x', 500)));

        await act.Should().ThrowAsync<InvalidOperationException>();
        store.Blobs.Should().BeEmpty("a publish failure must reclaim the offloaded blob");
    }

    [Fact]
    public async Task Binder_ClaimCheckedMessage_RetrievesDispatchesAndDeletes()
    {
        var store = new FakeClaimCheckStore();
        var received = new List<ClaimCheckProbe>();
        var transport = await BindConsumerAsync(store, deleteAfterConsume: true,
            sp => sp.AddScoped<IMessageHandler<ClaimCheckProbe>>(_ => new RecordingProbeHandler(received)));

        var reference = await store.StoreAsync(AsStream(new JsonMessageSerializer().Serialize(new ClaimCheckProbe("offloaded")).ToArray()));
        var context = MessageContext.New() with { Headers = new Dictionary<string, string> { [IClaimCheckStore.HeaderName] = reference } };

        await transport.Handler!(ReadOnlyMemory<byte>.Empty, context, CancellationToken.None);

        received.Should().ContainSingle().Which.Text.Should().Be("offloaded");
        store.Blobs.Should().BeEmpty("DeleteAfterConsume must remove the blob after a successful dispatch");
    }

    [Fact]
    public async Task Binder_HandlerFails_DoesNotDeleteClaim()
    {
        var store = new FakeClaimCheckStore();
        var transport = await BindConsumerAsync(store, deleteAfterConsume: true,
            sp => sp.AddScoped<IMessageHandler<ClaimCheckProbe>, ThrowingClaimProbeHandler>());

        var reference = await store.StoreAsync(AsStream(new JsonMessageSerializer().Serialize(new ClaimCheckProbe("poison")).ToArray()));
        var context = MessageContext.New() with { Headers = new Dictionary<string, string> { [IClaimCheckStore.HeaderName] = reference } };

        var act = () => transport.Handler!(ReadOnlyMemory<byte>.Empty, context, CancellationToken.None);
        (await act.Should().ThrowAsync<Exception>()).WithMessage("*handler broken*");

        store.Blobs.Should().ContainKey(reference, "a redelivery must be able to re-read the payload");
    }

    [Fact]
    public async Task Binder_DeleteAfterConsumeFalse_KeepsClaim()
    {
        var store = new FakeClaimCheckStore();
        var received = new List<ClaimCheckProbe>();
        var transport = await BindConsumerAsync(store, deleteAfterConsume: false,
            sp => sp.AddScoped<IMessageHandler<ClaimCheckProbe>>(_ => new RecordingProbeHandler(received)));

        var reference = await store.StoreAsync(AsStream(new JsonMessageSerializer().Serialize(new ClaimCheckProbe("kept")).ToArray()));
        var context = MessageContext.New() with { Headers = new Dictionary<string, string> { [IClaimCheckStore.HeaderName] = reference } };

        await transport.Handler!(ReadOnlyMemory<byte>.Empty, context, CancellationToken.None);

        received.Should().ContainSingle();
        store.Blobs.Should().ContainKey(reference);
    }

    [Fact]
    public async Task Binder_RedeliveryAfterBlobDeleted_DropsWithoutPoison()
    {
        // With DeleteAfterConsume on, a redelivered duplicate whose blob is already gone must be
        // dropped (no throw → no nack/poison loop), because a missing claim-check blob unambiguously
        // means the message was already consumed. Idempotency cannot save it here — retrieval precedes dedup.
        var store = new FakeClaimCheckStore();
        var received = new List<ClaimCheckProbe>();
        var transport = await BindConsumerAsync(store, deleteAfterConsume: true,
            sp => sp.AddScoped<IMessageHandler<ClaimCheckProbe>>(_ => new RecordingProbeHandler(received)));

        var reference = await store.StoreAsync(AsStream(new JsonMessageSerializer().Serialize(new ClaimCheckProbe("once")).ToArray()));
        var context = MessageContext.New() with { Headers = new Dictionary<string, string> { [IClaimCheckStore.HeaderName] = reference } };

        // First delivery consumes and deletes the blob.
        await transport.Handler!(ReadOnlyMemory<byte>.Empty, context, CancellationToken.None);
        received.Should().ContainSingle();
        store.Blobs.Should().BeEmpty();

        // Redelivery of the same message: blob gone → must be dropped, not thrown.
        var act = () => transport.Handler!(ReadOnlyMemory<byte>.Empty, context, CancellationToken.None);
        await act.Should().NotThrowAsync();
        received.Should().ContainSingle("the duplicate must be dropped, not re-dispatched");
    }

    private static (TransportAwareMessageBus Bus, RecordingTransport Transport, FakeClaimCheckStore Store) CreateBus(int threshold)
    {
        var services = new ServiceCollection();
        services.AddPragmaticMessaging();
        using var sp = services.BuildServiceProvider();

        var transport = new RecordingTransport();
        var store = new FakeClaimCheckStore();
        var bus = new TransportAwareMessageBus(
            transport,
            new DefaultMessageRouter(),
            new JsonMessageSerializer(),
            new InMemoryMessageBus(sp, NullLogger<InMemoryMessageBus>.Instance, []),
            NullLogger<TransportAwareMessageBus>.Instance,
            claimCheckStore: store,
            claimCheckOptions: new ClaimCheckOptions { Threshold = threshold });
        return (bus, transport, store);
    }

    private static async Task<CapturingClaimTransport> BindConsumerAsync(
        FakeClaimCheckStore store, bool deleteAfterConsume, Action<IServiceCollection> registerHandler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IClaimCheckStore>(store);
        services.AddSingleton(new ClaimCheckOptions { DeleteAfterConsume = deleteAfterConsume });
        registerHandler(services);
        var sp = services.BuildServiceProvider();

        var transport = new CapturingClaimTransport();
        await TransportSubscriptionBinder.BindAsync(
            transport,
            new DefaultMessageRouter(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            [new MessageSubscription(typeof(ClaimCheckProbe), subscriber: "claim-check")]).ConfigureAwait(false);
        return transport;
    }

    private static MemoryStream AsStream(byte[] bytes) => new(bytes, writable: false);

    private static async Task<string> ReadStringAsync(Stream stream)
    {
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }

    private sealed class FakeClaimCheckStore : IClaimCheckStore
    {
        public ConcurrentDictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

        public async Task<string> StoreAsync(Stream payload, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await payload.CopyToAsync(ms, ct).ConfigureAwait(false);
            var reference = Guid.NewGuid().ToString("N");
            Blobs[reference] = ms.ToArray();
            return reference;
        }

        public Task<Stream> RetrieveAsync(string reference, CancellationToken ct = default)
            => Blobs.TryGetValue(reference, out var payload)
                ? Task.FromResult<Stream>(new MemoryStream(payload, writable: false))
                : throw new InvalidOperationException($"Blob {reference} not found.");

        public Task DeleteAsync(string reference, CancellationToken ct = default)
        {
            Blobs.TryRemove(reference, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class NullFileStorage : Storage.IFileStorage
    {
        public Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
            => Task.FromResult(new Uri($"/files/{container}/{fileName}", UriKind.Relative));

        public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteAsync(Uri fileUri, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>In-memory <see cref="Storage.IFileStorage"/> that round-trips blobs keyed by their URI.</summary>
    private sealed class RecordingFileStorage : Storage.IFileStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

        public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct).ConfigureAwait(false);
            var key = $"/files/{container}/{fileName}";
            _blobs[key] = ms.ToArray();
            return new Uri(key, UriKind.Relative);
        }

        public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
            => Task.FromResult<Stream?>(_blobs.TryGetValue(fileUri.ToString(), out var b) ? new MemoryStream(b) : null);

        public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
            => Task.FromResult(_blobs.ContainsKey(fileUri.ToString()));

        public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
        {
            _blobs.TryRemove(fileUri.ToString(), out _);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenant(string? tenantId) : Pragmatic.MultiTenancy.ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => null;
        public bool IsResolved => !string.IsNullOrEmpty(tenantId);
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        public ReadOnlyMemory<byte>? LastPayload { get; private set; }
        public MessageContext? LastContext { get; private set; }

        public string Name => "Recording";
        public TransportStatus Status => TransportStatus.Connected;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
        {
            LastPayload = payload;
            LastContext = context;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
        {
            LastPayload = payload;
            LastContext = context;
            return Task.CompletedTask;
        }

        public Task<IAsyncDisposable> SubscribeAsync(
            string topic,
            string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
            CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new Noop());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingTransport : IMessageTransport
    {
        public string Name => "Throwing";
        public TransportStatus Status => TransportStatus.Connected;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("transport down");

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("transport down");

        public Task<IAsyncDisposable> SubscribeAsync(
            string topic,
            string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
            CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new Noop());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingClaimTransport : IMessageTransport
    {
        public Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task>? Handler { get; private set; }

        public string Name => "CapturingClaim";
        public TransportStatus Status => TransportStatus.Connected;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IAsyncDisposable> SubscribeAsync(
            string topic,
            string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
            CancellationToken ct = default)
        {
            Handler = handler;
            return Task.FromResult<IAsyncDisposable>(new Noop());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProbeHandler(List<ClaimCheckProbe> received) : IMessageHandler<ClaimCheckProbe>
    {
        public Task HandleAsync(ClaimCheckProbe message, MessageContext context, CancellationToken ct)
        {
            received.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingClaimProbeHandler : IMessageHandler<ClaimCheckProbe>
    {
        public Task HandleAsync(ClaimCheckProbe message, MessageContext context, CancellationToken ct)
            => throw new InvalidOperationException("handler broken");
    }
}

public sealed record ClaimCheckProbe(string Text);
