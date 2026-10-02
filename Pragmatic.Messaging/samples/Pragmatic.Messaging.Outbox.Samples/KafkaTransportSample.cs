namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Kafka transport — setup-only (broker-dependent, not executed here).
///     <c>builder.UseKafka(...)</c> registers a <c>KafkaTransport</c> as the
///     <c>IMessageTransport</c> and swaps the bus for a transport-aware variant
///     so published messages flow over a Kafka topic exchange and consumers pull
///     them back. It requires a running Kafka broker, so this sample only shows
///     the configuration shape and explains the wiring rather than running it.
///
///     Wiring (requires the Pragmatic.Messaging.Kafka package + a broker):
///
///         services.AddPragmaticMessaging(b => b.UseKafka(k =>
///         {
///             k.BootstrapServers  = "localhost:9092";  // required
///             k.GroupId           = "orders-service";  // consumer group
///             k.EnableIdempotence = true;              // exactly-once producer
///             k.AutoOffsetReset   = "earliest";        // cold-start position
///         }));
///
///     UseKafka throws if BootstrapServers is empty, and registers a
///     TransportAwareMessageBus (in-process fan-out + Kafka publish) plus a
///     TransportHealthContributor. To run it for real, start a broker (see
///     examples/showcase/docker-compose.messaging.yml for a RabbitMQ analogue)
///     and call the wiring above instead of describing it.
/// </summary>
public static class KafkaTransportSample
{
    public static void Describe()
    {
        Console.WriteLine("--- Kafka transport (setup-only, broker required) ---");
        Console.WriteLine("  status                   : not executed — needs a running Kafka broker");
        Console.WriteLine("  wiring                   : AddPragmaticMessaging(b => b.UseKafka(k => ...))");
        Console.WriteLine("  required option          : KafkaOptions.BootstrapServers (throws if empty)");
        Console.WriteLine("  registers                : KafkaTransport, TransportAwareMessageBus, TransportHealthContributor");
        Console.WriteLine("  see                      : src/Pragmatic.Messaging.Kafka/KafkaMessagingExtensions.cs");
        Console.WriteLine();
    }
}
