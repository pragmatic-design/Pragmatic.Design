using System.Diagnostics;
using Pragmatic.Client.Cli;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Client.Tests.Cli;

/// <summary>
///     An <c>[Sse]</c> endpoint is a method of the TypeScript client returning an async iterable of its event
///     type (#746), and the generated code, run, reads the events a real stream sends.
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>client.ts</c> is executed by Node (<c>--experimental-transform-types</c>, Node 22, the
///         version CI installs) against <see cref="SseLoopbackEndpoint" />, which writes the frames
///         <c>SseStreamResult</c> writes: <c>data:</c> JSON events, a <c>: hb</c> keep-alive, a terminal
///         <c>event: error</c>. Asserting on the text of the file alone would show the method exists, not
///         that it reads a stream.
///     </para>
///     <para>
///         The event type is checked on the signature (<c>AsyncGenerator&lt;T.LinkDto, …&gt;</c>): Node strips
///         types without checking them.
///     </para>
/// </remarks>
public class AnSseEndpointIsAStreamOfTypedEventsTests
{
    private const string Manifest = """
        {
          "assembly": "Acme.Catalog",
          "endpoints": [{ "operationId": "Catalog.WatchLinks", "httpMethod": "GET", "fullRoute": "/links/watch",
                          "isVoid": false,
                          "parameters": [{ "name": "tag", "in": "query", "type": "string" }],
                          "response": { "type": "global::System.Collections.Generic.IAsyncEnumerable<global::Pragmatic.Result.Result<global::Acme.Catalog.LinkDto, global::Pragmatic.Result.IError>>", "isStream": true } }],
          "types": [{ "simpleName": "LinkDto", "kind": "dto",
                      "properties": [{ "name": "Url", "type": "string", "isRequired": true },
                                     { "name": "Title", "type": "string", "isRequired": true }] }]
        }
        """;

    [Fact]
    public void TheStreamMethod_YieldsTheEventType()
    {
        var client = Emit()["client.ts"];

        client.Should().Contain("watchLinks(query?: { tag?: string }): AsyncGenerator<T.LinkDto, void, undefined> {");
        client.Should().Contain("return this.stream<T.LinkDto>('GET', url);");
        client.Should().Contain("export class ApiStreamError extends Error {");
    }

    [Fact]
    public async Task TheClient_ReadsTheEventsTheStreamSends()
    {
        await using var endpoint = SseLoopbackEndpoint.Streaming(
            """data: {"url":"https://a.example","title":"First"}""",
            ": hb",
            """data: {"url":"https://b.example","title":"Second"}""");

        var output = await RunAsync(endpoint.BaseUrl, """
            const titles: string[] = [];
            for await (const link of client.watchLinks({ tag: 'news' })) titles.push(link.title);
            console.log(JSON.stringify(titles));
            """);

        output.Should().Be("""["First","Second"]""", "the keep-alive is not an event");
        endpoint.RequestLines.Single().Should().Be("GET /links/watch?tag=news HTTP/1.1");
    }

    /// <summary>The server's terminal <c>event: error</c> ends the iteration with an <c>ApiStreamError</c>, after the events before it.</summary>
    [Fact]
    public async Task ATerminalErrorEvent_IsThrownAsAnApiStreamError()
    {
        await using var endpoint = SseLoopbackEndpoint.Streaming(
            """data: {"url":"https://a.example","title":"First"}""",
            "event: error\ndata: {\"code\":\"links.gone\",\"title\":\"Gone\",\"detail\":null,\"status\":410}");

        var output = await RunAsync(endpoint.BaseUrl, """
            const titles: string[] = [];
            try {
              for await (const link of client.watchLinks()) titles.push(link.title);
              console.log('no error');
            } catch (e) {
              console.log(JSON.stringify({ titles, code: e.error.code, status: e.error.status, isStreamError: e instanceof ApiStreamError }));
            }
            """);

        output.Should().Be("""{"titles":["First"],"code":"links.gone","status":410,"isStreamError":true}""");
    }

    private static IReadOnlyDictionary<string, string> Emit() => TsEmitter.Emit(ManifestJson.Read(Manifest)[0], Manifest);

    /// <summary>Writes the generated files and a script beside them, runs the script with Node, returns its output.</summary>
    private static async Task<string> RunAsync(string baseUrl, string body)
    {
        var folder = Directory.CreateTempSubdirectory("pragmatic-ts-client-");
        try
        {
            foreach (var (file, content) in Emit())
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, file), content).ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "run.ts"), $$"""
                import { CatalogClient, ApiStreamError } from './client.ts';
                const client = new CatalogClient('{{baseUrl}}');
                {{body}}
                """).ConfigureAwait(false);

            var start = new ProcessStartInfo("node", ["--experimental-transform-types", "--no-warnings", "run.ts"])
            {
                WorkingDirectory = folder.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var node = Process.Start(start)!;
            var stdout = node.StandardOutput.ReadToEndAsync();
            var stderr = node.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await node.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            node.ExitCode.Should().Be(0, await stderr.ConfigureAwait(false));
            return (await stdout.ConfigureAwait(false)).Trim();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
