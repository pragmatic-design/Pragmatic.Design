using System.Collections.Concurrent;
using System.Text;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.AzureServiceBus;

/// <summary>
///     <see cref="AzureServiceBusTransport"/> topology provisioning and header (de)serialization.
///     Entities are created via the management API when available; where management is not
///     supported (the local emulator provisions from Config.json; production namespaces are often
///     IaC-provisioned) the transport warns once and proceeds against pre-provisioned entities.
/// </summary>
public sealed partial class AzureServiceBusTransport
{
    private const string PragmaticHeaderPrefix = "X-Pragmatic-";

    private readonly ConcurrentDictionary<string, bool> _ensuredEntities = new();
    private ServiceBusAdministrationClient? _adminClient;
    private volatile bool _administrationUnavailable;

    private void InitializeAdministration()
    {
        if (!options.AutoCreateEntities)
            return;

        try
        {
            _adminClient = new ServiceBusAdministrationClient(options.ConnectionString);
        }
        catch (Exception ex)
        {
            // e.g. the emulator connection string is not accepted by the admin client.
            _administrationUnavailable = true;
            LogAdministrationUnavailable(ex);
        }
    }

    private async Task EnsureTopicAsync(string topic, CancellationToken ct)
    {
        if (SkipProvisioning($"t:{topic}"))
            return;

        await ProvisionAsync(async admin =>
        {
            if (!await admin.TopicExistsAsync(topic, ct).ConfigureAwait(false))
                await admin.CreateTopicAsync(topic, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task EnsureQueueAsync(string queue, CancellationToken ct)
    {
        if (SkipProvisioning($"q:{queue}"))
            return;

        await ProvisionAsync(async admin =>
        {
            if (!await admin.QueueExistsAsync(queue, ct).ConfigureAwait(false))
            {
                await admin.CreateQueueAsync(new CreateQueueOptions(queue)
                {
                    MaxDeliveryCount = options.MaxDeliveryCount,
                    DeadLetteringOnMessageExpiration = true,
                }, ct).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private async Task EnsureSubscriptionAsync(string topic, string subscriptionName, CancellationToken ct)
    {
        if (SkipProvisioning($"s:{topic}/{subscriptionName}"))
            return;

        await ProvisionAsync(async admin =>
        {
            if (!await admin.SubscriptionExistsAsync(topic, subscriptionName, ct).ConfigureAwait(false))
            {
                await admin.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, subscriptionName)
                {
                    MaxDeliveryCount = options.MaxDeliveryCount,
                    DeadLetteringOnMessageExpiration = true,
                }, ct).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>True when provisioning should be skipped (disabled, unavailable, or already done).</summary>
    private bool SkipProvisioning(string entityKey)
        => _adminClient is null
            || _administrationUnavailable
            || _ensuredEntities.ContainsKey(entityKey)
            || !_ensuredEntities.TryAdd(entityKey, true);

    /// <summary>
    ///     Runs a management operation; on "management not supported" (emulator, restricted SAS)
    ///     flips to pass-through mode with a single warning instead of failing every publish.
    /// </summary>
    private async Task ProvisionAsync(Func<ServiceBusAdministrationClient, Task> operation)
    {
        try
        {
            await operation(_adminClient!).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _administrationUnavailable = true;
            LogAdministrationUnavailable(ex);
        }
    }

    // =========================================================================
    // Header (de)serialization
    // =========================================================================

    private static void FillApplicationProperties(ServiceBusMessage message, MessageContext context)
    {
        if (context.TenantId is not null)
            message.ApplicationProperties["X-Pragmatic-TenantId"] = context.TenantId;
        if (context.UserId is not null)
            message.ApplicationProperties["X-Pragmatic-UserId"] = context.UserId;
        if (context.SourceBoundary is not null)
            message.ApplicationProperties["X-Pragmatic-SourceBoundary"] = context.SourceBoundary;
        if (context.BusName is not null)
            message.ApplicationProperties["X-Pragmatic-BusName"] = context.BusName;
        if (context.RetryCount > 0)
            message.ApplicationProperties["X-Pragmatic-RetryCount"] = context.RetryCount;

        // Custom application headers — reserved X-Pragmatic-* keys always win.
        if (context.Headers is not null)
        {
            foreach (var kv in context.Headers)
            {
                if (kv.Key.StartsWith(PragmaticHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                message.ApplicationProperties[kv.Key] = kv.Value;
            }
        }
    }

    private static MessageContext ExtractContext(ServiceBusReceivedMessage message)
    {
        var props = message.ApplicationProperties;

        return new MessageContext(
            MessageId: string.IsNullOrEmpty(message.MessageId) ? Guid.NewGuid().ToString("N") : message.MessageId,
            CorrelationId: string.IsNullOrEmpty(message.CorrelationId) ? null : message.CorrelationId,
            TenantId: GetProperty(props, "X-Pragmatic-TenantId"),
            UserId: GetProperty(props, "X-Pragmatic-UserId"),
            Headers: ExtractCustomHeaders(props),
            // Prefer the broker's own DeliveryCount (1 on first delivery): abandon-based
            // redeliveries are visible to handlers even when no header traveled.
            RetryCount: props.TryGetValue("X-Pragmatic-RetryCount", out var retry) && retry is int r
                ? r
                : (int)Math.Max(0, message.DeliveryCount - 1),
            SourceBoundary: GetProperty(props, "X-Pragmatic-SourceBoundary"),
            BusName: GetProperty(props, "X-Pragmatic-BusName"),
            EnqueuedAt: message.EnqueuedTime);
    }

    private static string? GetProperty(IReadOnlyDictionary<string, object> props, string key)
        => props.TryGetValue(key, out var value) ? value switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => value?.ToString(),
        } : null;

    private static IReadOnlyDictionary<string, string>? ExtractCustomHeaders(IReadOnlyDictionary<string, object> props)
    {
        Dictionary<string, string>? custom = null;
        foreach (var kv in props)
        {
            if (kv.Key.StartsWith(PragmaticHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            custom ??= new Dictionary<string, string>(StringComparer.Ordinal);
            custom[kv.Key] = kv.Value?.ToString() ?? "";
        }

        return custom;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Azure Service Bus management API unavailable — entity auto-creation disabled; entities must be pre-provisioned (emulator Config.json / IaC)")]
    private partial void LogAdministrationUnavailable(Exception ex);
}
