using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.RabbitMQ;

namespace Pragmatic.Messaging.Tests.Unit;

public class RabbitMqOptionsTests
{
    [Fact]
    public void Defaults_ShouldBeReasonable()
    {
        var options = new RabbitMqOptions { ConnectionString = "amqp://localhost" };

        options.ConsumerPrefetchCount.Should().Be(10);
        options.AutoReconnect.Should().BeTrue();
        options.ReconnectBaseDelayMs.Should().Be(1000);
        options.MaxReconnectAttempts.Should().Be(0); // infinite
        options.DurableQueues.Should().BeTrue();
        options.PersistentMessages.Should().BeTrue();
        options.ExchangeType.Should().Be("topic");
    }

    [Fact]
    public void Defaults_ShouldBeSafeAgainstMessageLoss()
    {
        var options = new RabbitMqOptions { ConnectionString = "amqp://localhost" };

        // Failed handlers must dead-letter, not discard: DLX is on by default (opt-out).
        options.DeadLetterExchange.Should().Be("pragmatic.dlx");
        // Publish must be broker-confirmed: no silent loss between outbox and broker.
        options.PublisherConfirms.Should().BeTrue();
    }

    [Fact]
    public void UseRabbitMq_WithoutConnectionString_ShouldThrow()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new Pragmatic.Messaging.Configuration.MessagingBuilder(services);

        builder.Invoking(b => RabbitMqMessagingExtensions.UseRabbitMq(b, rmq => { }))
            .Should().Throw<ArgumentException>()
            .WithMessage("*ConnectionString*");
    }

    [Fact]
    public async Task UseRabbitMq_WithConnectionString_ShouldRegisterTransport()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        var builder = new Pragmatic.Messaging.Configuration.MessagingBuilder(services);

        RabbitMqMessagingExtensions.UseRabbitMq(builder, rmq =>
        {
            rmq.ConnectionString = "amqp://guest:guest@localhost:5672";
        });

        var sp = services.BuildServiceProvider();
        sp.GetService<IMessageTransport>().Should().NotBeNull();
        sp.GetService<IMessageTransport>().Should().BeOfType<RabbitMqTransport>();
        sp.GetService<RabbitMqTransport>()!.Name.Should().Be("RabbitMQ");
        await sp.DisposeAsync();
    }

    [Fact]
    public void RabbitMqTransport_BeforeConnect_ShouldBeDisconnected()
    {
        var options = new RabbitMqOptions { ConnectionString = "amqp://localhost" };
        var transport = new RabbitMqTransport(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMqTransport>.Instance);

        transport.Status.Should().Be(TransportStatus.Disconnected);
        transport.Name.Should().Be("RabbitMQ");
    }
}
