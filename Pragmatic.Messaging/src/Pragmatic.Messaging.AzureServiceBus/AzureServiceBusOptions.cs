namespace Pragmatic.Messaging.AzureServiceBus;

/// <summary>
///     Configuration for the Azure Service Bus transport.
/// </summary>
public sealed class AzureServiceBusOptions
{
    /// <summary>
    ///     Service Bus connection string. For the local emulator:
    ///     <c>"Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true"</c>.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    ///     Creates queues/topics/subscriptions via the management API before first use.
    ///     Degrades gracefully (warn + continue) where management is not available —
    ///     the local emulator provisions entities from its Config.json, and production
    ///     namespaces often pre-provision via IaC. Default: true.
    /// </summary>
    public bool AutoCreateEntities { get; set; } = true;

    /// <summary>Max concurrent handler invocations per subscription processor. Default: 4.</summary>
    public int MaxConcurrentCalls { get; set; } = 4;

    /// <summary>Messages prefetched by each processor. Default: 0 (no prefetch).</summary>
    public int PrefetchCount { get; set; }

    /// <summary>
    ///     Max delivery attempts before Azure Service Bus dead-letters the message natively
    ///     (applied to auto-created entities; pre-provisioned entities keep their own).
    ///     Default: 5.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 5;
}
