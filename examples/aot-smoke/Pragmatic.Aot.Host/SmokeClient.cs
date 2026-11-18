using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Pragmatic.Aot.Host;

/// <summary>
///     The smoke's caller: once the host has started, it calls the host over HTTP, prints one line per check, and stops the host.
/// </summary>
/// <remarks>
///     It runs inside the host it calls, as a hosted service, so the process under test is the one <c>PragmaticApp.RunAsync</c> built
///     and started: no second entry point that a real application would not have. The address is the server's own, read after start, so
///     the port is whatever the operating system gave.
/// </remarks>
public sealed class SmokeClient(IServer server, IHostApplicationLifetime lifetime) : BackgroundService
{
    private const string Allowed = "notes.note.create,notes.note.read";

    /// <summary>The number of checks that failed, or -1 while they have not run.</summary>
    public static int Failures { get; private set; } = -1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
            await started.Task.WaitAsync(stoppingToken);

        var failures = 0;
        void Check(string what, bool ok, string detail)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}: {detail}");
            failures += ok ? 0 : 1;
        }

        try
        {
            var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            // Hand-written JSON on the client side, as the other smokes do: the point is the server's binding.
            using var created = await Send(client, HttpMethod.Post, "/api/notes", "anna", Allowed, """{"title":"a note","pages":3,"kind":"Final"}""",
                stoppingToken);
            var createdBody = await created.Content.ReadAsStringAsync(stoppingToken);
            Check("POST /api/notes creates the note", created.StatusCode == HttpStatusCode.Created && createdBody.Contains("\"a note\"", StringComparison.Ordinal),
                $"{(int)created.StatusCode} {createdBody}");

            // An enum by its name, read from the request and written in the response: what the non-generic JsonStringEnumConverter did
            // by making a converter at run time, and what the host's enum converter does without dynamic code (#857).
            Check("an enum goes in and comes out by its name", createdBody.Contains("\"kind\":\"Final\"", StringComparison.Ordinal), createdBody);

            var id = created.StatusCode == HttpStatusCode.Created ? IdOf(createdBody) : null;
            using var read = await Send(client, HttpMethod.Get, $"/api/notes/{id}", "anna", Allowed, null, stoppingToken);
            var readBody = await read.Content.ReadAsStringAsync(stoppingToken);
            Check("GET /api/notes/{id} reads it back", read.StatusCode == HttpStatusCode.OK
                    && readBody.Contains("\"a note\"", StringComparison.Ordinal) && readBody.Contains("\"pages\":3", StringComparison.Ordinal)
                    && readBody.Contains("\"kind\":\"Final\"", StringComparison.Ordinal),
                $"{(int)read.StatusCode} {readBody}");

            using var anonymous = await Send(client, HttpMethod.Get, $"/api/notes/{id}", null, null, null, stoppingToken);
            Check("a request with no identity is refused", anonymous.StatusCode == HttpStatusCode.Unauthorized, $"{(int)anonymous.StatusCode}");

            using var forbidden = await Send(client, HttpMethod.Post, "/api/notes", "bruno", "notes.note.read", """{"title":"not mine","pages":1}""", stoppingToken);
            Check("a caller without the permission is refused", forbidden.StatusCode == HttpStatusCode.Forbidden, $"{(int)forbidden.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            Check("the checks ran", false, e.ToString());
        }

        Failures = failures;
        lifetime.StopApplication();
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string? user, string? permissions, string? json,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (user is not null)
        {
            request.Headers.Add("X-User-Id", user);
            request.Headers.Add("X-User-Permissions", permissions);
        }

        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return client.SendAsync(request, ct);
    }

    private static string? IdOf(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }
}
