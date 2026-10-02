using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.ControlPlane;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Diagnostics;
using Pragmatic.Messaging.Kafka;

namespace Pragmatic.Messaging.Tests.Unit;

public class KafkaOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };

        options.EnableIdempotence.Should().BeTrue();
        options.EnableAutoCommit.Should().BeFalse();
        options.AutoOffsetReset.Should().Be("earliest");
        options.MaxPollIntervalMs.Should().Be(300_000);
        options.SessionTimeoutMs.Should().Be(45_000);
        options.GroupId.Should().BeNull();
    }

    [Fact]
    public void Defaults_ShouldBeSafeAgainstMessageLoss()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };

        // A failed handler's offset gets implicitly committed by the next successful message
        // on the partition — without a DLQ topic that message is silently lost.
        options.EnableDeadLetter.Should().BeTrue();
        options.DeadLetterTopicSuffix.Should().Be(".dlq");
    }

    [Fact]
    public void Defaults_TopologyIsExplicit()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };

        // Production brokers commonly disable auto.create.topics.enable: the transport
        // must create its topics explicitly via AdminClient.
        options.AutoCreateTopics.Should().BeTrue();
        options.DefaultPartitions.Should().Be(3);
        options.ReplicationFactor.Should().Be(-1); // broker default
    }
}

/// <summary>
///     Health reporting for the Kafka transport. The logic lives in the shared
///     <see cref="TransportHealthContributor" />, and the registration is asserted here: a contributor
///     tested in isolation stays green while, unregistered, the host reports no transport health at all.
/// </summary>
public class KafkaHealthContributorTests
{
    [Fact]
    public async Task CheckAsync_Disconnected_ReportsUnhealthy()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);
        var contributor = new TransportHealthContributor(transport);

        var report = await contributor.CheckAsync();

        report.Status.Should().Be(ContributorHealthStatus.Unhealthy);
        report.Message.Should().Contain("Kafka").And.Contain("Disconnected");
        contributor.Name.Should().Be("Messaging.Kafka");
        contributor.Category.Should().Be("Messaging");
    }

    [Fact]
    public async Task CheckAsync_Connected_ReportsHealthy()
    {
        // ConnectAsync creates a real Kafka producer (which succeeds without a broker
        // because librdkafka defers the actual TCP connection). This lets us test the
        // "Connected" status path without a running Kafka broker.
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        await transport.ConnectAsync();

        var report = await new TransportHealthContributor(transport).CheckAsync();

        report.Status.Should().Be(ContributorHealthStatus.Healthy);
        report.Message.Should().Contain("Kafka connected");

        // Cleanup: disconnect to dispose the producer
        await transport.DisconnectAsync();
    }
}

public class KafkaMessagingExtensionsTests
{
    [Fact]
    public void UseKafka_EmptyBootstrapServers_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        builder.Invoking(b => KafkaMessagingExtensions.UseKafka(b, _ => { }))
            .Should().Throw<ArgumentException>()
            .WithMessage("*BootstrapServers*");
    }

    [Fact]
    public async Task UseKafka_RegistersTransportAndBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new MessagingBuilder(services);

        KafkaMessagingExtensions.UseKafka(builder, opts =>
        {
            opts.BootstrapServers = "localhost:9092";
        });

        var sp = services.BuildServiceProvider();

        sp.GetService<IMessageTransport>().Should().NotBeNull();
        sp.GetService<IMessageTransport>().Should().BeOfType<KafkaTransport>();
        sp.GetService<KafkaTransport>()!.Name.Should().Be("Kafka");

        await sp.DisposeAsync();
    }

    /// <summary>
    ///     The transport must contribute to host health. This is the assertion whose absence let the defect
    ///     live: a per-transport health class existed and was unit-tested, but nothing ever registered it, so
    ///     <c>HostHealthAggregator</c> resolved an empty set and host health reported no transports.
    ///     Asserting the class in isolation could not catch that — only resolving it from the container can.
    /// </summary>
    [Fact]
    public async Task UseKafka_RegistersHostHealthContributor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new MessagingBuilder(services);

        KafkaMessagingExtensions.UseKafka(builder, opts => opts.BootstrapServers = "localhost:9092");

        var sp = services.BuildServiceProvider();

        var contributor = sp.GetServices<IHostHealthContributor>()
            .Should().ContainSingle(c => c.Name == "Messaging.Kafka").Subject;

        (await contributor.CheckAsync()).Should().NotBeNull();

        await sp.DisposeAsync();
    }

    [Fact]
    public void UseKafka_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new MessagingBuilder(services);

        KafkaMessagingExtensions.UseKafka(builder, opts =>
        {
            opts.BootstrapServers = "broker1:9092,broker2:9092";
            opts.GroupId = "my-consumer-group";
            opts.EnableIdempotence = false;
            opts.EnableAutoCommit = true;
            opts.AutoOffsetReset = "latest";
            opts.MaxPollIntervalMs = 600_000;
            opts.SessionTimeoutMs = 30_000;
        });

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<KafkaOptions>();

        resolved.BootstrapServers.Should().Be("broker1:9092,broker2:9092");
        resolved.GroupId.Should().Be("my-consumer-group");
        resolved.EnableIdempotence.Should().BeFalse();
        resolved.EnableAutoCommit.Should().BeTrue();
        resolved.AutoOffsetReset.Should().Be("latest");
        resolved.MaxPollIntervalMs.Should().Be(600_000);
        resolved.SessionTimeoutMs.Should().Be(30_000);
    }
}

public class KafkaTransportTests
{
    [Fact]
    public void Name_ReturnsKafka()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        transport.Name.Should().Be("Kafka");
    }

    [Fact]
    public void BeforeConnect_Status_IsDisconnected()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        transport.Status.Should().Be(TransportStatus.Disconnected);
    }

    [Fact]
    public async Task PublishAsync_BeforeConnect_ThrowsInvalidOperation()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);
        var context = new MessageContext(MessageId: "test-1");

        var act = () => transport.PublishAsync(
            new byte[] { 1, 2, 3 },
            "test-topic",
            context);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task SendAsync_BeforeConnect_ThrowsInvalidOperation()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);
        var context = new MessageContext(MessageId: "test-1");

        // SendAsync delegates to PublishAsync, so it should also throw
        var act = () => transport.SendAsync(
            new byte[] { 1, 2, 3 },
            "test-queue",
            context);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task SubscribeAsync_BeforeConnect_ThrowsInvalidOperation()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        var act = () => transport.SubscribeAsync(
            "test-topic",
            "test-sub",
            (_, _, _) => Task.CompletedTask);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task ConnectAsync_SetsStatusToConnected()
    {
        // librdkafka defers TCP connection, so ConnectAsync succeeds without a real broker
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        await transport.ConnectAsync();

        transport.Status.Should().Be(TransportStatus.Connected);

        await transport.DisconnectAsync();
    }

    [Fact]
    public async Task DisconnectAsync_SetsStatusToDisconnected()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        await transport.ConnectAsync();
        transport.Status.Should().Be(TransportStatus.Connected);

        await transport.DisconnectAsync();
        transport.Status.Should().Be(TransportStatus.Disconnected);
    }

    [Fact]
    public async Task DisposeAsync_SetsStatusToDisconnected()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };
        var transport = new KafkaTransport(options, NullLogger<KafkaTransport>.Instance);

        await transport.ConnectAsync();
        transport.Status.Should().Be(TransportStatus.Connected);

        await transport.DisposeAsync();
        transport.Status.Should().Be(TransportStatus.Disconnected);
    }
}
