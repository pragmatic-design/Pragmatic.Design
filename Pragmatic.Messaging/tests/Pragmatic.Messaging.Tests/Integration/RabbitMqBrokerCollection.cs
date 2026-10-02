namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>xUnit collection sharing one RabbitMQ container across the broker tests.</summary>
[CollectionDefinition("RabbitMqBroker")]
public sealed class RabbitMqBrokerCollection : ICollectionFixture<RabbitMqContainerFixture>;
