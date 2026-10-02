using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Testcontainers.RabbitMq;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     The broker the two services talk over, started once per run.
/// </summary>
/// <remarks>
///     <para>
///         The image is pinned, as the PostgreSQL one is: a suite whose broker version moves under it
///         answers a different question every month. The management port is published because
///         <see cref="QueueExistsAsync" /> needs it — see there for why a test has to ask the broker
///         something.
///     </para>
///     <para>
///         ⚠️ It does <b>not</b> degrade when Docker is missing. <c>Pragmatic.Messaging.Tests</c>'s own
///         fixture leaves its connection string null in that case so its tests skip; here the suite is
///         <c>RequiresDocker</c> and its whole subject is that a message crosses a real broker, so a run
///         without one has to fail loudly rather than pass having proved nothing.
///     </para>
/// </remarks>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .WithPortBinding(15672, assignRandomHostPort: true)
        .Build();

    private HttpClient _management = null!;

    /// <summary>The AMQP connection string both hosts are configured with.</summary>
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        _management = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(15672)}"),
        };

        // ⚠️ The credentials come from the connection string, not from a constant: the container module
        // picks them, and a hardcoded guest/guest answers 401 — which reads exactly like "the queue is
        // not there", and cost a diagnosis before it was noticed.
        var amqp = new Uri(ConnectionString);
        _management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(amqp.UserInfo))));
    }

    /// <summary>
    ///     Whether a queue whose name contains <paramref name="fragment" /> is declared and bound.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A test has to ask this, and it is not ceremony: the exchange is a <b>topic</b> exchange, so
    ///     a message published while no queue is bound to it is **discarded** by the broker — not queued,
    ///     not dead-lettered, gone. The consumer binds its queue in a background service after the
    ///     connect, so a publish issued the instant both hosts start can beat it and the test would fail
    ///     for a reason no deployment has. Asking the broker is synchronisation; a sleep would be a guess.
    /// </remarks>
    public async Task<bool> QueueExistsAsync(string fragment)
    {
        using var response = await _management.GetAsync("/api/queues");
        if (!response.IsSuccessStatusCode)
            return false;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.EnumerateArray().Any(queue =>
            queue.TryGetProperty("name", out var name)
            && (name.GetString() ?? "").Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     How many messages are sitting in the queue whose name contains <paramref name="fragment" />.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Asked of the broker because that is where a dead letter <b>is</b>. A handler that keeps
    ///     throwing is nacked without requeue and the queue's <c>x-dead-letter-exchange</c> routes the
    ///     message to <c>pragmatic.dlx</c>, which the transport declares as a fanout with
    ///     <c>pragmatic.dlx.dlq</c> bound to it. The framework's <c>IDeadLetterStore</c> — the in-memory
    ///     one <c>UseMessaging</c> registers — is <b>not</b> on this path: it is written by the outbox
    ///     pump for a type it cannot resolve and by the channel transport, not by the RabbitMQ consumer.
    ///     Looking there first is what this test did, and it found nothing for twenty seconds.
    /// </remarks>
    public async Task<int> MessagesInAsync(string fragment)
    {
        using var response = await _management.GetAsync("/api/queues");
        if (!response.IsSuccessStatusCode)
            return 0;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.EnumerateArray()
            .Where(queue => queue.TryGetProperty("name", out var name)
                            && (name.GetString() ?? "").Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .Sum(queue => queue.TryGetProperty("messages", out var messages) ? messages.GetInt32() : 0);
    }

    /// <summary>
    ///     Binds a queue of the test's own to <paramref name="exchange" /> and returns its name, so a
    ///     test can read what actually crossed the broker.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>A header is only observable on the wire.</b> The consumer restores it into the
    ///         consume scope and the handler never looks at it, so asserting one from inside the
    ///         application would mean putting a line in a reference application that exists for a test.
    ///         A second queue bound to the same exchange gets its own copy — the services' queues are
    ///         untouched and receive theirs as always.
    ///     </para>
    ///     <para>
    ///         Declared through the management API rather than over AMQP: the suite already depends on
    ///         it for <see cref="QueueExistsAsync" />, and it avoids a second client library in a test
    ///         project. Bind before publishing — a topic exchange discards what no queue is bound to.
    ///     </para>
    /// </remarks>
    public async Task<string> ObserveAsync(string exchange)
    {
        var queue = $"test.observer.{exchange}";

        using var declared = await _management.PutAsJsonAsync(
            $"/api/queues/%2F/{Uri.EscapeDataString(queue)}",
            // ⚠️ Durable, although nothing here needs it to survive a restart: RabbitMQ 4 refuses a
            // transient non-exclusive queue outright — "Feature `transient_nonexclusive_queues` is
            // deprecated" — with a 400 that reads like a malformed request. The queue is deleted at
            // the end of the test instead.
            new { durable = true, auto_delete = false, arguments = new { } });
        await EnsureSucceededAsync(declared, $"declare the observer queue {queue}");

        using var bound = await _management.PostAsJsonAsync(
            $"/api/bindings/%2F/e/{Uri.EscapeDataString(exchange)}/q/{Uri.EscapeDataString(queue)}",
            new { routing_key = "", arguments = new { } });
        await EnsureSucceededAsync(bound, $"bind {queue} to {exchange}");

        return queue;
    }

    /// <summary>
    ///     Throws with the broker's own answer when a management call did not succeed.
    /// </summary>
    /// <remarks>
    ///     ⚠️ One line, and an exception rather than an assertion, because the gate records the first
    ///     line of a failure message and an assertion library puts the actual value on the second: the
    ///     status and the body would be exactly the part that never reaches the record.
    /// </remarks>
    private static async Task EnsureSucceededAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = (await response.Content.ReadAsStringAsync()).ReplaceLineEndings(" ");

        throw new InvalidOperationException(
            $"The broker refused to {what}: {(int)response.StatusCode} {body}");
    }

    /// <summary>
    ///     The headers of every message sitting in <paramref name="queue" />, newest reading first.
    /// </summary>
    /// <remarks>
    ///     Read with <c>ack_requeue_true</c>: the messages stay where they are, so a test that polls
    ///     until what it is waiting for arrives sees the earlier ones again instead of consuming them.
    /// </remarks>
    public async Task<IReadOnlyList<(string Body, IReadOnlyDictionary<string, string> Headers)>>
        MessagesOnAsync(string queue)
    {
        using var response = await _management.PostAsJsonAsync(
            $"/api/queues/%2F/{Uri.EscapeDataString(queue)}/get",
            new { count = 50, ackmode = "ack_requeue_true", encoding = "auto" });

        if (!response.IsSuccessStatusCode)
            return [];

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.EnumerateArray().Select(message =>
        {
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (message.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("headers", out var written))
            {
                foreach (var header in written.EnumerateObject())
                    headers[header.Name] = header.Value.ToString();
            }

            return (message.TryGetProperty("payload", out var payload) ? payload.GetString() ?? "" : "",
                (IReadOnlyDictionary<string, string>)headers);
        }).ToList();
    }

    /// <summary>Removes an observer queue, so it stops collecting for the rest of the run.</summary>
    public async Task StopObservingAsync(string queue)
    {
        using var response = await _management.DeleteAsync($"/api/queues/%2F/{Uri.EscapeDataString(queue)}");
        _ = response.StatusCode;
    }

    public async Task DisposeAsync()
    {
        _management?.Dispose();
        await _container.DisposeAsync();
    }
}
