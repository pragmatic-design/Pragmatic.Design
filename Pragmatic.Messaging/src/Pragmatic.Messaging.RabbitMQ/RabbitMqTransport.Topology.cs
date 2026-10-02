using System.Text;
using RabbitMQ.Client;

namespace Pragmatic.Messaging.RabbitMQ;

/// <summary>
///     <see cref="RabbitMqTransport"/> topology declaration and header (de)serialization helpers.
/// </summary>
public sealed partial class RabbitMqTransport
{
    // =========================================================================
    // Topology Helpers
    // =========================================================================

    private Task EnsureExchangeAsync(string exchangeName, CancellationToken ct)
        => EnsureExchangeOnChannelAsync(_publishChannel!, exchangeName, ct);

    private async Task EnsureExchangeOnChannelAsync(IChannel channel, string exchangeName, CancellationToken ct)
    {
        // TryAdd returns false when already present — skip the declare without TOCTOU.
        if (!_declaredExchanges.TryAdd(exchangeName, true)) return;

        // ExchangeDeclare is idempotent on RabbitMQ: a second declaration with identical
        // parameters is a no-op, so a race that slips through is safe.
        await channel.ExchangeDeclareAsync(
            exchangeName,
            options.ExchangeType,
            durable: true,
            autoDelete: false,
            cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task EnsureQueueAsync(string queueName, CancellationToken ct)
    {
        if (_declaredQueues.ContainsKey(queueName)) return;

        var args = await BuildQueueArgumentsAsync(_publishChannel!, ct).ConfigureAwait(false);
        await _publishChannel!.QueueDeclareAsync(
            queueName,
            durable: options.DurableQueues,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: ct).ConfigureAwait(false);

        _declaredQueues.TryAdd(queueName, true);
    }

    private async Task EnsureQueueOnChannelAsync(IChannel channel, string queueName, CancellationToken ct)
    {
        var args = await BuildQueueArgumentsAsync(channel, ct).ConfigureAwait(false);
        await channel.QueueDeclareAsync(
            queueName,
            durable: options.DurableQueues,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: ct).ConfigureAwait(false);

        _declaredQueues.TryAdd(queueName, true);
    }

    /// <summary>
    ///     Builds the queue declaration arguments. When a dead-letter exchange is configured, ensures
    ///     the DLX topology exists and adds <c>x-dead-letter-exchange</c> so handler-failure Nacks are
    ///     routed there instead of dropped; when <see cref="RabbitMqOptions.QueueType"/> is not
    ///     classic, adds <c>x-queue-type</c> (e.g. quorum). Returns null when neither applies.
    /// </summary>
    private async Task<IDictionary<string, object?>?> BuildQueueArgumentsAsync(IChannel channel, CancellationToken ct)
    {
        var useQuorumType = !string.IsNullOrEmpty(options.QueueType)
            && !string.Equals(options.QueueType, "classic", StringComparison.OrdinalIgnoreCase);
        var hasDlx = !string.IsNullOrEmpty(options.DeadLetterExchange);

        if (!hasDlx && !useQuorumType)
            return null;

        var args = new Dictionary<string, object?>();
        if (hasDlx)
        {
            await EnsureDeadLetterTopologyAsync(channel, ct).ConfigureAwait(false);
            args["x-dead-letter-exchange"] = options.DeadLetterExchange;
        }

        if (useQuorumType)
            args["x-queue-type"] = options.QueueType;

        return args;
    }

    /// <summary>
    ///     Declares the dead-letter exchange (durable fanout) and a durable dead-letter queue bound to it,
    ///     so dead-lettered messages land in a durable queue rather than vanishing. Idempotent.
    /// </summary>
    private async Task EnsureDeadLetterTopologyAsync(IChannel channel, CancellationToken ct)
    {
        var dlx = options.DeadLetterExchange!;
        // The TryAdd guard also covers the bound DLQ declared below (one-time topology setup).
        if (!_declaredExchanges.TryAdd(dlx, true)) return;

        await channel.ExchangeDeclareAsync(
            dlx, "fanout", durable: true, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);

        var deadLetterQueue = $"{dlx}.dlq";
        // The DLQ inherits the configured queue type (a classic DLQ next to quorum work
        // queues would be the only unreplicated queue in the cluster).
        var dlqArgs = !string.IsNullOrEmpty(options.QueueType)
            && !string.Equals(options.QueueType, "classic", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, object?> { ["x-queue-type"] = options.QueueType }
                : null;
        await channel.QueueDeclareAsync(
            deadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlqArgs, cancellationToken: ct).ConfigureAwait(false);
        await channel.QueueBindAsync(deadLetterQueue, dlx, "", cancellationToken: ct).ConfigureAwait(false);
    }

    // =========================================================================
    // Header Serialization
    // =========================================================================

    private const string PragmaticHeaderPrefix = "X-Pragmatic-";

    private static Dictionary<string, object?> BuildHeaders(MessageContext context)
    {
        var headers = new Dictionary<string, object?>
        {
            ["X-Pragmatic-MessageId"] = context.MessageId,
        };

        if (context.TenantId is not null)
            headers["X-Pragmatic-TenantId"] = context.TenantId;
        if (context.UserId is not null)
            headers["X-Pragmatic-UserId"] = context.UserId;
        if (context.SourceBoundary is not null)
            headers["X-Pragmatic-SourceBoundary"] = context.SourceBoundary;
        // What this actually is. The exchange is the boundary's, so without this the consumer has only
        // the queue it came from to go on — and that queue is bound to every message of the boundary.
        if (context.MessageType is not null)
            headers["X-Pragmatic-MessageType"] = context.MessageType;
        if (context.BusName is not null)
            headers["X-Pragmatic-BusName"] = context.BusName;
        if (context.RetryCount > 0)
            headers["X-Pragmatic-RetryCount"] = context.RetryCount;

        // Propagate custom application headers — reserved X-Pragmatic-* keys
        // always win (they're authoritative and must not be shadowed).
        if (context.Headers is not null)
        {
            foreach (var kv in context.Headers)
            {
                if (kv.Key.StartsWith(PragmaticHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                headers[kv.Key] = kv.Value;
            }
        }

        return headers;
    }

    private static MessageContext ExtractContext(IReadOnlyBasicProperties props)
    {
        var headers = props.Headers;
        var custom = ExtractCustomHeaders(headers);

        return new MessageContext(
            MessageId: props.MessageId ?? Guid.NewGuid().ToString("N"),
            CorrelationId: props.CorrelationId,
            TenantId: GetHeaderString(headers, "X-Pragmatic-TenantId"),
            UserId: GetHeaderString(headers, "X-Pragmatic-UserId"),
            Headers: custom,
            RetryCount: GetHeaderInt(headers, "X-Pragmatic-RetryCount"),
            SourceBoundary: GetHeaderString(headers, "X-Pragmatic-SourceBoundary"),
            BusName: GetHeaderString(headers, "X-Pragmatic-BusName"),
            EnqueuedAt: DateTimeOffset.FromUnixTimeSeconds(props.Timestamp.UnixTime),
            MessageType: GetHeaderString(headers, "X-Pragmatic-MessageType"));
    }

    private static IReadOnlyDictionary<string, string>? ExtractCustomHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0) return null;
        Dictionary<string, string>? custom = null;
        foreach (var kv in headers)
        {
            if (kv.Key.StartsWith(PragmaticHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (kv.Value is null) continue;
            custom ??= new Dictionary<string, string>(StringComparer.Ordinal);
            custom[kv.Key] = kv.Value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : kv.Value.ToString() ?? "";
        }
        return custom;
    }

    private static string? GetHeaderString(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null) return null;
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value.ToString();
    }

    private static int GetHeaderInt(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null) return 0;
        return value is int i ? i : 0;
    }
}
