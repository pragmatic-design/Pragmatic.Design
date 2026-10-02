using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.ControlPlane;
using Pragmatic.Messaging.AzureServiceBus;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Kafka;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Messaging.Sql;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     Every transport must contribute to host health.
///     <para>
///         This is the test whose absence let the defect live. Each transport shipped a hand-written
///         <c>…HealthContributor</c> and <c>…HealthCheck</c> pair — ten classes running the same switch on
///         <c>transport.Status</c> — and Kafka's even had unit tests. But nothing ever registered any of
///         them, so <c>HostHealthAggregator</c>, which the generated host does register, resolved an empty
///         set: the messaging half of host health reported nothing at all.
///     </para>
///     <para>
///         Testing a health class in isolation cannot catch that, no matter how thoroughly: the question is
///         not "does this class compute the right report" but "does anything ask it to". Only resolving it
///         from the container answers that, which is what every case here does. Covering all five transports
///         rather than one keeps the defect from returning through whichever transport was left out.
///     </para>
/// </summary>
public class TransportHealthRegistrationTests
{
    public static TheoryData<string, string, Action<MessagingBuilder>> Transports => new()
    {
        { "Kafka", "Messaging.Kafka", b => b.UseKafka(o => o.BootstrapServers = "localhost:9092") },
        { "RabbitMQ", "Messaging.RabbitMQ", b => b.UseRabbitMq(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/") },
        { "Channels", "Messaging.Channels", b => b.UseChannels() },
        {
            "AzureServiceBus", "Messaging.AzureServiceBus",
            b => b.UseAzureServiceBus(o => o.ConnectionString =
                "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==")
        },
        {
            "Sql", "Messaging.Sql",
            b => b.UseSqlTransport(o => o.ConfigureDbContext = db => db.UseSqlite("DataSource=:memory:"))
        },
    };

    /// <param name="transport">Transport under test, for the failure message.</param>
    /// <param name="expectedName">The contributor name the host health report is expected to carry.</param>
    /// <param name="configure">Applies the transport's own <c>Use…</c> extension.</param>
    [Theory]
    [MemberData(nameof(Transports))]
    public async Task UseTransport_RegistersAHostHealthContributor(
        string transport, string expectedName, Action<MessagingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        configure(new MessagingBuilder(services));

        var sp = services.BuildServiceProvider();

        var contributor = sp.GetServices<IHostHealthContributor>()
            .Should().ContainSingle(c => c.Name == expectedName,
                $"{transport} must contribute to host health, or the aggregator reports nothing for it")
            .Subject;

        contributor.Category.Should().Be("Messaging");

        // A contributor that throws is as useless as one that is missing: the aggregator would report the
        // transport as failed for the wrong reason. No broker is running, so any status is acceptable.
        var report = await contributor.CheckAsync();
        report.Should().NotBeNull();
        report.Message.Should().NotBeNullOrWhiteSpace();

        await sp.DisposeAsync();
    }
}
