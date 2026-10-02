using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Testcontainers.RabbitMq;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The broker the services talk over, a second, empty virtual host on it where nobody answers, and the
///     management API a test reads the broker through.
/// </summary>
/// <remarks>
///     <para>
///         The empty virtual host is how the suite has a Stock that does not answer without stopping the two
///         that do: a host connected there sends its request to a queue no instance consumes, which is what a
///         deployment with every Stock instance down looks like from Orders.
///     </para>
///     <para>
///         The management API is how a test knows what crossed the broker and whether a queue was drained:
///         a message that is published once, or a duplicate that was consumed and changed nothing, is only
///         observable there (Casework's fixture, the same way).
///     </para>
/// </remarks>
internal sealed class RabbitMqFixture : IAsyncDisposable
{
    private const string Silent = "silent";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .WithPortBinding(15672, assignRandomHostPort: true)
        .Build();

    private HttpClient _management = null!;

    /// <summary>The broker every service is connected to.</summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>The same broker, on a virtual host where no Stock instance consumes anything.</summary>
    public string NobodyAnswersConnectionString { get; private set; } = null!;

    public async Task StartAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        ConnectionString = _container.GetConnectionString();

        var credentials = Uri.UnescapeDataString(new Uri(ConnectionString).UserInfo);
        var user = credentials.Split(':')[0];
        await RunAsync(["rabbitmqctl", "add_vhost", Silent]).ConfigureAwait(false);
        await RunAsync(["rabbitmqctl", "set_permissions", "-p", Silent, user, ".*", ".*", ".*"]).ConfigureAwait(false);

        NobodyAnswersConnectionString = new UriBuilder(ConnectionString) { Path = "/" + Silent }.Uri.ToString();

        // The credentials come from the connection string, not from a constant: the container module picks
        // them, and a hardcoded guest/guest answers 401.
        _management = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(15672)}"),
        };
        _management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
    }

    /// <summary>
    ///     How many messages the queues whose name contains <paramref name="fragment" /> still have, ready or
    ///     being handled, all together; null when no such queue is declared yet.
    /// </summary>
    /// <remarks>
    ///     All of them, because one message type can have a queue per consuming service —
    ///     <c>shipping.order-picked</c> and <c>orders.order-picked</c> — and "drained" means every one.
    /// </remarks>
    public async Task<int?> PendingInAsync(string fragment)
    {
        using var response = await _management.GetAsync("/api/queues/%2F").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        int? pending = null;
        foreach (var queue in document.RootElement.EnumerateArray())
        {
            if (!(queue.GetProperty("name").GetString() ?? "").Contains(fragment, StringComparison.Ordinal)
                || (queue.GetProperty("name").GetString() ?? "").StartsWith("test.", StringComparison.Ordinal))
                continue;

            // A queue the broker has not sampled yet carries no count: nothing is known to be in it.
            pending = (pending ?? 0)
                + (queue.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Number
                    ? messages.GetInt32()
                    : 0);
        }

        return pending;
    }

    /// <summary>
    ///     How many messages the queue named exactly <paramref name="queue" /> has had acknowledged, ever.
    /// </summary>
    /// <remarks>
    ///     The way a test knows a <b>particular</b> delivery was consumed when consuming it changes nothing
    ///     observable: read the count, publish, and wait for it to go up by one. The broker samples these
    ///     statistics every few seconds, so the count is waited for, never read once.
    /// </remarks>
    public async Task<long> AcknowledgedOnAsync(string queue)
    {
        using var response = await _management.GetAsync($"/api/queues/%2F/{Uri.EscapeDataString(queue)}").ConfigureAwait(false);
        await EnsureAsync(response, $"read {queue}").ConfigureAwait(false);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.TryGetProperty("message_stats", out var stats)
               && stats.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.Number
            ? ack.GetInt64()
            : 0;
    }

    /// <summary>
    ///     Binds a queue of the test's own to <paramref name="exchange" /> and returns its name, so the test
    ///     reads what was published there without taking it from the services' queues.
    /// </summary>
    /// <remarks>
    ///     Durable, although nothing needs it to survive a restart: RabbitMQ 4 refuses a transient
    ///     non-exclusive queue. Bind before the publish — a topic exchange discards what no queue is bound to.
    /// </remarks>
    public async Task<string> ObserveAsync(string exchange)
    {
        var queue = $"test.observer.{exchange}.{Guid.NewGuid():N}";

        // Declared here with the transport's own parameters — topic, durable, not auto-deleted — so it
        // exists before anybody has published to it, and the transport's later declaration is a no-op.
        using var exchangeDeclared = await _management.PutAsJsonAsync(
            $"/api/exchanges/%2F/{Uri.EscapeDataString(exchange)}",
            new { type = "topic", durable = true, auto_delete = false, arguments = new { } }).ConfigureAwait(false);
        await EnsureAsync(exchangeDeclared, $"declare {exchange}").ConfigureAwait(false);

        using var declared = await _management.PutAsJsonAsync(
            $"/api/queues/%2F/{Uri.EscapeDataString(queue)}",
            new { durable = true, auto_delete = false, arguments = new { } }).ConfigureAwait(false);
        await EnsureAsync(declared, $"declare {queue}").ConfigureAwait(false);

        using var bound = await _management.PostAsJsonAsync(
            $"/api/bindings/%2F/e/{Uri.EscapeDataString(exchange)}/q/{Uri.EscapeDataString(queue)}",
            new { routing_key = "#", arguments = new { } }).ConfigureAwait(false);
        await EnsureAsync(bound, $"bind {queue} to {exchange}").ConfigureAwait(false);

        return queue;
    }

    /// <summary>The bodies of the messages sitting in <paramref name="queue" />, left where they are.</summary>
    public async Task<IReadOnlyList<string>> MessagesOnAsync(string queue)
        => [.. (await TypedMessagesOnAsync(queue).ConfigureAwait(false)).Select(m => m.Body)];

    /// <summary>
    ///     The bodies of the messages in <paramref name="queue" /> whose type — the transport's
    ///     <c>X-Pragmatic-MessageType</c> header — ends with <paramref name="typeName" />.
    /// </summary>
    /// <remarks>
    ///     By the header and not the body: two events can have the same fields — <c>OrderReadyToPick</c> and
    ///     <c>OrderCancelled</c> are both an order id and a moment — and a body cannot tell them apart.
    /// </remarks>
    public async Task<IReadOnlyList<string>> MessagesOfTypeOnAsync(string queue, string typeName)
        => [.. (await TypedMessagesOnAsync(queue).ConfigureAwait(false))
            .Where(m => m.Type.EndsWith(typeName, StringComparison.Ordinal))
            .Select(m => m.Body)];

    private async Task<IReadOnlyList<(string Body, string Type)>> TypedMessagesOnAsync(string queue)
    {
        using var response = await _management.PostAsJsonAsync(
            $"/api/queues/%2F/{Uri.EscapeDataString(queue)}/get",
            new { count = 50, ackmode = "ack_requeue_true", encoding = "auto" }).ConfigureAwait(false);
        await EnsureAsync(response, $"read {queue}").ConfigureAwait(false);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return [.. document.RootElement.EnumerateArray().Select(m =>
        {
            var type = m.TryGetProperty("properties", out var properties)
                       && properties.TryGetProperty("headers", out var headers)
                       && headers.TryGetProperty("X-Pragmatic-MessageType", out var header)
                ? header.ToString()
                : "";
            return (m.GetProperty("payload").GetString() ?? "", type);
        })];
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).ReplaceLineEndings(" ");
        throw new InvalidOperationException($"The broker refused to {what}: {(int)response.StatusCode} {body}");
    }

    private async Task RunAsync(IList<string> command)
    {
        var ran = await _container.ExecAsync(command).ConfigureAwait(false);
        if (ran.ExitCode != 0)
            throw new InvalidOperationException($"{string.Join(' ', command)} failed: {ran.Stderr}");
    }

    public async ValueTask DisposeAsync()
    {
        _management?.Dispose();
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
