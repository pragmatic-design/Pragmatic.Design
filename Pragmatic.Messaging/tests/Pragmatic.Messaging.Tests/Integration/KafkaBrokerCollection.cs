namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>xUnit collection sharing one Kafka container across the broker tests.</summary>
[CollectionDefinition("KafkaBroker")]
public sealed class KafkaBrokerCollection : ICollectionFixture<KafkaContainerFixture>;
