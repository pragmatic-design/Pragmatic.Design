namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>xUnit collection sharing one Service Bus emulator across the broker tests.</summary>
[CollectionDefinition("AzureServiceBusBroker")]
public sealed class AzureServiceBusBrokerCollection : ICollectionFixture<AzureServiceBusContainerFixture>;
