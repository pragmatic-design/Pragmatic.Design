using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     Two Pragmatic hosts in one process each serve <b>their own</b> compile-time document.
/// </summary>
/// <remarks>
///     <para>
///         A document read only from a static of the process (<c>PragmaticOpenApiRegistry</c>, written
///         by a <c>[ModuleInitializer]</c> the generator emits per host) cannot do that: module
///         initializers run at assembly load in load order, and last writer wins — so with two hosts
///         the one loaded second would answer for both. On Casework, <c>GET /openapi/v1.json</c>
///         <b>against Intake</b> would return Verify's empty document.
///     </para>
///     <para>
///         <c>RequiresAuthentication</c> would be overwritten with it, so the security requirement
///         published would belong to the other host too — the half that ships a wrong contract to a
///         client generator rather than an empty one.
///     </para>
///     <para>
///         ⚠️ <b>Why it is easy to miss</b>: with one host per process, "the document of the process"
///         and "the document of this host" are the same string. An in-process suite of a two-service
///         application is where they part — and that is how anybody testing a Pragmatic system of
///         services meets it.
///     </para>
///     <para>
///         ⚠️ Two of the cases below write the process-wide registry, so the class is in
///         <see cref="TheProcessWideOpenApiRegistryCollection" />: another class doing the same in
///         parallel would overwrite the document between the registration and the read.
///     </para>
/// </remarks>
[Collection(TheProcessWideOpenApiRegistryCollection.Name)]
public sealed class TwoHostsInOneProcessEachPublishTheirOwnContractTests
{
    private const string Intake = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Intake", "version": "1.0.0" },
          "paths": { "/api/cases": { "post": { "operationId": "Cases.Open" } } }
        }
        """;

    /// <summary>Verify serves no endpoint: everything it does arrives over the bus.</summary>
    private const string Verify = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Verify", "version": "1.0.0" },
          "paths": { }
        }
        """;

    /// <summary>The setpoint: the document each host answers with is its own.</summary>
    [Fact]
    public async Task TwoHosts_EachAnswerWithItsOwnDocument()
    {
        var (fromIntake, fromVerify) = await BothDocumentsAsync(first: Intake, second: Verify);

        fromIntake.GetProperty("info").GetProperty("title").GetString().Should().Be("Intake",
            "the host that was asked is the host that answers");
        fromIntake.GetProperty("paths").EnumerateObject().Select(p => p.Name).Should().Contain("/api/cases");

        fromVerify.GetProperty("info").GetProperty("title").GetString().Should().Be("Verify");
        fromVerify.GetProperty("paths").EnumerateObject().Should().BeEmpty(
            "and a host that publishes no route still says so for itself, instead of borrowing the "
            + "other one's paths");
    }

    /// <summary>
    ///     The order is what decides the answer under a last-writer-wins static, so it is asserted both
    ///     ways: whichever host is built second, neither borrows the other's document.
    /// </summary>
    [Fact]
    public async Task TheSecondHostBuilt_DoesNotOverwriteTheFirst()
    {
        var (fromVerify, fromIntake) = await BothDocumentsAsync(first: Verify, second: Intake);

        fromVerify.GetProperty("info").GetProperty("title").GetString().Should().Be("Verify");
        fromIntake.GetProperty("info").GetProperty("title").GetString().Should().Be("Intake");
    }

    /// <summary>
    ///     A host that registers nothing in its container still serves the process-wide document, which
    ///     is what every single-host application has always done and must keep doing.
    /// </summary>
    [Fact]
    public async Task AHostThatRegistersNothing_StillServesTheProcessDocument()
    {
        PragmaticOpenApiRegistry.Register(Intake);

        var document = await DocumentOfAHostRegisteringNothingAsync();

        document.GetProperty("info").GetProperty("title").GetString().Should().Be("Intake",
            "the static registry stays the fallback: a single-host deployment behaves exactly as before");
    }

    private static async Task<JsonElement> DocumentOfAHostRegisteringNothingAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        try
        {
            app.MapPragmaticOpenApi();
            await app.StartAsync().ConfigureAwait(false);
            return await DocumentOf(app).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     A host whose container carries a document prefers it over whatever the process-wide registry
    ///     holds. Asserted with the registry holding the <b>other</b> host's document, because that is
    ///     the state a two-host process is always in.
    /// </summary>
    [Fact]
    public async Task TheContainersDocument_WinsOverTheProcessRegistry()
    {
        PragmaticOpenApiRegistry.Register(Verify);

        var (fromIntake, _) = await BothDocumentsAsync(first: Intake, second: null);

        fromIntake.GetProperty("info").GetProperty("title").GetString().Should().Be("Intake");
    }

    /// <summary>
    ///     Starts one or two hosts <b>in this process at the same time</b> — which is the whole
    ///     condition under test — and returns what each answers. The second document may be null for
    ///     the single-host case; the empty element it returns then is never read.
    /// </summary>
    private static async Task<(JsonElement First, JsonElement Second)> BothDocumentsAsync(
        string first, string? second)
    {
        var firstApp = Host(first);
        var secondApp = second is null ? null : Host(second);

        try
        {
            await firstApp.StartAsync().ConfigureAwait(false);
            if (secondApp is not null)
                await secondApp.StartAsync().ConfigureAwait(false);

            var firstDocument = await DocumentOf(firstApp).ConfigureAwait(false);
            var secondDocument = secondApp is null
                ? default
                : await DocumentOf(secondApp).ConfigureAwait(false);

            return (firstDocument, secondDocument);
        }
        finally
        {
            if (secondApp is not null)
                await secondApp.DisposeAsync().ConfigureAwait(false);

            await firstApp.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static WebApplication Host(string document)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // What the generated host registers: the document is a fact about this host, so it lives in
        // this host's container.
        builder.Services.AddSingleton(new HostOpenApiDocument(document));

        var app = builder.Build();
        app.MapPragmaticOpenApi();
        return app;
    }

    private static async Task<JsonElement> DocumentOf(WebApplication app)
    {
        using var client = app.GetTestClient();
        using var json = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false));
        return json.RootElement.Clone();
    }
}
