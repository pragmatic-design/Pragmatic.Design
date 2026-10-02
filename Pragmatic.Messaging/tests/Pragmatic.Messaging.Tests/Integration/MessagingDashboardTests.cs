#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Dashboard;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Dashboard ops API over a real TestServer: status counters, dead-letter list,
///     replay (repubblish + remove), delete, saga registry, auth.
/// </summary>
public sealed class MessagingDashboardTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly InMemoryDeadLetterStore _deadLetters = new();
    private readonly RecordingBus _bus = new();

    public MessagingDashboardTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticMessaging();
        builder.Services.AddSingleton<IDeadLetterStore>(_deadLetters);
        builder.Services.AddSingleton<IMessageBus>(_bus);
        builder.Services.AddSingleton<IMessageTypeRegistry>(new StubTypeRegistry());
        builder.Services.AddSingleton(new SagaDescriptor(
            typeof(object), "FakeSaga", "FakeState",
            static (_, _) => Task.FromResult<IReadOnlyList<SagaInstanceInfo>>(
                [new SagaInstanceInfo(Guid.NewGuid(), "corr-1", "Running", DateTimeOffset.UtcNow, null)])));

        _app = builder.Build();
        // TestServer leaves RemoteIpAddress null; a real localhost request has a loopback IP. Stamp it
        // so the default (no-ApiKey) loopback-only guard admits these requests as a genuine localhost
        // caller would — the null/forwarded denial paths are covered by dedicated tests below.
        _app.Use(async (ctx, next) =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            await next(ctx);
        });
        MessagingDashboardEndpoints.Map(_app, new MessagingDashboardOptions());
        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
        // Loopback mode requires the CSRF header on mutating calls; the panel sends it, so the shared
        // client mirrors that. A dedicated test covers the denial when it is absent.
        _client.DefaultRequestHeaders.Add(MessagingDashboardOptions.CsrfHeader, "1");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<Guid> SeedDeadLetterAsync(string type = "Pragmatic.Messaging.Tests.Integration.DashboardProbe")
    {
        var message = new DeadLetterMessage(
            type, """{"text":"boom"}""", "handler exploded", 3, MessageContext.New(), DateTimeOffset.UtcNow);
        await _deadLetters.StoreAsync(message);
        return message.Id;
    }

    [Fact]
    public async Task Status_WithSeededData_ReportsCounters()
    {
        await SeedDeadLetterAsync();

        var response = await _client.GetAsync("/_messaging/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("deadLetters").GetInt32().Should().BeGreaterThan(0);
        json.RootElement.GetProperty("activeSagas").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task DeadLetters_List_ReturnsSeededEntry()
    {
        var id = await SeedDeadLetterAsync();

        var json = JsonDocument.Parse(await _client.GetStringAsync("/_messaging/dead-letters"));

        json.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("id").GetGuid() == id)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Replay_KnownType_RepublishesAndRemoves()
    {
        var id = await SeedDeadLetterAsync();

        var response = await _client.PostAsync($"/_messaging/dead-letters/{id}/replay", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _bus.Published.Should().ContainSingle()
            .Which.Should().BeOfType<DashboardProbe>()
            .Which.Text.Should().Be("boom");
        (await _deadLetters.GetAsync(id)).Should().BeNull("replay must remove the dead letter");
    }

    [Fact]
    public async Task Replay_UnknownType_Returns422AndKeepsEntry()
    {
        var id = await SeedDeadLetterAsync(type: "Some.Unknown.Type");

        var response = await _client.PostAsync($"/_messaging/dead-letters/{id}/replay", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _deadLetters.GetAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_ExistingEntry_Removes()
    {
        var id = await SeedDeadLetterAsync();

        var response = await _client.DeleteAsync($"/_messaging/dead-letters/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _deadLetters.GetAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_UnknownEntry_Returns404()
        => (await _client.DeleteAsync($"/_messaging/dead-letters/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task Sagas_ReturnsDescriptorGroups()
    {
        var json = JsonDocument.Parse(await _client.GetStringAsync("/_messaging/sagas"));

        var group = json.RootElement.EnumerateArray().Single();
        group.GetProperty("name").GetString().Should().Be("FakeSaga");
        group.GetProperty("active").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Panel_ReturnsHtml()
    {
        var response = await _client.GetAsync("/_messaging/panel");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Pragmatic Messaging");
    }

    [Fact]
    public async Task ApiKey_Configured_MissingHeaderIs401_CorrectHeaderPasses()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticMessaging();
        var app = builder.Build();
        MessagingDashboardEndpoints.Map(app, new MessagingDashboardOptions { ApiKey = "s3cret" });
        await app.StartAsync();
        await using (app.ConfigureAwait(false))
        {
            using var client = app.GetTestClient();

            (await client.GetAsync("/_messaging/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/_messaging/status");
            request.Headers.Add("X-Messaging-Key", "s3cret");
            (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

            await app.StopAsync();
        }
    }

    // Loopback-only mode must not admit a request whose remote IP cannot be positively
    // confirmed as loopback. TestServer leaves RemoteIpAddress null (as some socket/proxy setups do).
    [Fact]
    public async Task LoopbackMode_NullRemoteIp_Denied()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticMessaging();
        var app = builder.Build();
        MessagingDashboardEndpoints.Map(app, new MessagingDashboardOptions());
        await app.StartAsync();
        await using (app.ConfigureAwait(false))
        {
            using var client = app.GetTestClient();
            (await client.GetAsync("/_messaging/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await app.StopAsync();
        }
    }

    // Behind a reverse proxy, Connection.RemoteIpAddress can reflect a client-controlled
    // X-Forwarded-For, so a loopback IP with a forwarding header present must be denied.
    [Fact]
    public async Task LoopbackMode_ForwardedHeaderPresent_Denied()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticMessaging();
        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            await next(ctx);
        });
        MessagingDashboardEndpoints.Map(app, new MessagingDashboardOptions());
        await app.StartAsync();
        await using (app.ConfigureAwait(false))
        {
            using var client = app.GetTestClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/_messaging/status");
            request.Headers.Add("X-Forwarded-For", "127.0.0.1");
            (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await app.StopAsync();
        }
    }

    // The panel must HTML-escape externally-influenced fields (error, correlationId) it
    // interpolates into innerHTML, else a crafted value is stored XSS.
    [Fact]
    public async Task Panel_EscapesDynamicFields()
    {
        var panel = await _client.GetStringAsync("/_messaging/panel");
        panel.Should().Contain("function esc(");
        panel.Should().Contain("esc(m.error)");
        panel.Should().Contain("esc(s.correlationId)");
    }

    // In loopback (no-key) mode a mutating request without the non-simple CSRF header must be
    // denied (a cross-site drive-by POST cannot set that header without a blocked preflight). GET is fine.
    [Fact]
    public async Task LoopbackMode_MutatingWithoutCsrfHeader_Denied()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticMessaging();
        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            await next(ctx);
        });
        MessagingDashboardEndpoints.Map(app, new MessagingDashboardOptions());
        await app.StartAsync();
        await using (app.ConfigureAwait(false))
        {
            using var client = app.GetTestClient();
            // Safe method: unaffected.
            (await client.GetAsync("/_messaging/status")).StatusCode.Should().Be(HttpStatusCode.OK);
            // Mutating without the CSRF header: denied by the auth filter (before the handler).
            (await client.PostAsync($"/_messaging/dead-letters/{Guid.NewGuid()}/replay", content: null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await app.StopAsync();
        }
    }

    // Any of the labelled keys is accepted; an unknown key is rejected.
    [Fact]
    public async Task MultiKey_LabelledKeyAccepted_WrongKeyRejected()
    {
        var options = new MessagingDashboardOptions();
        options.ApiKeys["ops"] = "ops-key";
        options.ApiKeys["ci"] = "ci-key";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPragmaticMessaging();
        var app = builder.Build();
        MessagingDashboardEndpoints.Map(app, options);
        await app.StartAsync();
        await using (app.ConfigureAwait(false))
        {
            using var client = app.GetTestClient();

            using var ok = new HttpRequestMessage(HttpMethod.Get, "/_messaging/status");
            ok.Headers.Add("X-Messaging-Key", "ci-key");
            (await client.SendAsync(ok)).StatusCode.Should().Be(HttpStatusCode.OK);

            using var bad = new HttpRequestMessage(HttpMethod.Get, "/_messaging/status");
            bad.Headers.Add("X-Messaging-Key", "nope");
            (await client.SendAsync(bad)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            await app.StopAsync();
        }
    }

    private sealed class StubTypeRegistry : IMessageTypeRegistry
    {
        public object? Deserialize(string fullyQualifiedTypeName, string json)
            => fullyQualifiedTypeName == "Pragmatic.Messaging.Tests.Integration.DashboardProbe"
                ? System.Text.Json.JsonSerializer.Deserialize<DashboardProbe>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
    }

    private sealed class RecordingBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull
            where TResponse : notnull
            => throw new NotSupportedException();
    }
}

public sealed record DashboardProbe(string Text);
