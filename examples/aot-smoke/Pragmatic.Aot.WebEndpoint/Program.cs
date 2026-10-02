using System.Text.Json;
using Pragmatic;
using Pragmatic.Aot.WebEndpoint;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Serialization;

// Does a Pragmatic-GENERATED endpoint work under Native AOT?
//
// The other two smokes answer a serialization question with no web server in sight. This one is the
// HTTP path: the generator emits `endpoints.MapPost(route, async (...) => ...)`, and ASP.NET's AOT
// story for minimal APIs is the Request Delegate Generator, which rewrites those calls at compile
// time. RDG is itself a source generator, and generators do not see each other's output — so the
// question is whether the generated Map calls fall back to the reflection-based RequestDelegateFactory.
//
// The app starts, calls itself, and checks the answer came back right. Under `PublishAot` the
// reflection fallback is not there to save it.

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5199");
// Logging stays on: when this smoke fails, the exception is the whole point of running it.
builder.Logging.SetMinimumLevel(LogLevel.Error);

// The smoke has no authentication. Endpoints require authorization by default, and with no
// authorization middleware in the pipeline ASP.NET answers every such request with 500 — so an app
// without authentication says so, here in library mode, or with [AnonymousHost] on a composition host.
builder.Services.AddPragmaticEndpoints(o => o.RequireAuthorizationByDefault = false);
builder.Services.AddPragmaticActions();

// The generated context, with the reflection fallback off — the AOT configuration, not a lenient one.
builder.Services.AddSingleton(new PragmaticJsonOptions()
    .AddContext(Pragmatic.Aot.WebEndpoint.Generated.PragmaticJsonContext.Default)
    .DisableReflectionFallback());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // PRAGMATIC_AOT_LENIENT keeps the reflection fallback, to separate two questions: whether the
    // generated context is complete, and whether the ASP.NET mapping path needs reflection at all.
    var seam = Environment.GetEnvironmentVariable("PRAGMATIC_AOT_LENIENT") == "1"
        ? new PragmaticJsonOptions()
            .AddContext(Pragmatic.Aot.WebEndpoint.Generated.PragmaticJsonContext.Default)
            .Build()
        : new PragmaticJsonOptions()
            .AddContext(Pragmatic.Aot.WebEndpoint.Generated.PragmaticJsonContext.Default)
            .DisableReflectionFallback()
            .Build();

    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.TypeInfoResolver = seam.TypeInfoResolver;
});

var app = builder.Build();
app.MapPragmaticEndpoints();

// The probes that decided the design are gone: they answered their question (RequestDelegateFactory
// is unusable under AOT even on the simplest signature; RequestDelegate works and keeps endpoint
// filters), and one of them mapped a deliberately broken endpoint — which poisons the whole routing
// table, so it could not stay. The evidence lives in examples/aot-smoke/README.md.

// What the response type resolves to, before any request. An empty property list here and a full one
// under the JIT is the signature of the reflection resolver answering for a trimmed type — which
// produces `{}` rather than an error, so the request looks like it worked.
var httpJson = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
var storyInfo = httpJson.Value.SerializerOptions.GetTypeInfo(typeof(StoryDto));
Console.WriteLine($"resolver: {httpJson.Value.SerializerOptions.TypeInfoResolver?.GetType().Name}, StoryDto properties: {storyInfo.Properties.Count}");

// Serialized directly, and again through the `object` declared type the Results helpers box it into.
// The two disagreeing is what separates "the context is wrong" from "the response path loses the type".
var sample = new StoryDto { Title = "direct", Slots = 1 };
Console.WriteLine($"direct: {JsonSerializer.Serialize(sample, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<StoryDto>)storyInfo)}");
Console.WriteLine($"as object: {JsonSerializer.Serialize(sample, httpJson.Value.SerializerOptions.GetTypeInfo(typeof(object)))}");

await app.StartAsync().ConfigureAwait(false);

using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5199") };

// Hand-written JSON on the client side on purpose: the point is the server's binding, and going
// through a serializer here would only add a second thing that can fail.
using var payload = new StringContent("""{"title":"a story","slots":3}""", System.Text.Encoding.UTF8, "application/json");
var response = await client.PostAsync("/api/stories", payload).ConfigureAwait(false);

if (!response.IsSuccessStatusCode)
{
    Console.Error.WriteLine($"AOT-WEB-FAIL: POST returned {(int)response.StatusCode} {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    await app.StopAsync().ConfigureAwait(false);
    return 1;
}

var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
if (!body.Contains("\"a story\"", StringComparison.Ordinal) || !body.Contains("3", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"AOT-WEB-FAIL: unexpected response {(int)response.StatusCode} {response.Content.Headers.ContentType} {body}");
    await app.StopAsync().ConfigureAwait(false);
    return 1;
}

// Every binding source in one request: a route Guid, a query int, a query enum, and a header. These
// are what RequestDelegateFactory would read; a generated endpoint reads them itself, and each
// conversion is its own chance to be wrong.
var id = Guid.NewGuid();
using var echo = await client.GetAsync($"/api/echo/{id}?page=3&shade=bold").ConfigureAwait(false);
var echoBody = await echo.Content.ReadAsStringAsync().ConfigureAwait(false);

if (!echo.IsSuccessStatusCode
    || !echoBody.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)
    || !echoBody.Contains("\"page\":3", StringComparison.Ordinal)
    || !echoBody.Contains("Bold", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"AOT-WEB-FAIL: bound parameters came back wrong: {(int)echo.StatusCode} {echoBody}");
    await app.StopAsync().ConfigureAwait(false);
    return 1;
}

// The optional ones, omitted: the declared default must survive, and the absent header must be null
// rather than empty. "Absent" and "present but empty" are different answers.
using var defaults = await client.GetAsync($"/api/echo/{id}?shade=plain").ConfigureAwait(false);
var defaultsBody = await defaults.Content.ReadAsStringAsync().ConfigureAwait(false);

if (!defaultsBody.Contains("\"page\":7", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"AOT-WEB-FAIL: an omitted query value lost its declared default: {defaultsBody}");
    await app.StopAsync().ConfigureAwait(false);
    return 1;
}

// A malformed route value must be refused with a body that says which parameter, not a bare status.
using var malformed = await client.GetAsync("/api/echo/not-a-guid?shade=plain").ConfigureAwait(false);
var malformedBody = await malformed.Content.ReadAsStringAsync().ConfigureAwait(false);

if ((int)malformed.StatusCode != 400 || !malformedBody.Contains("id", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"AOT-WEB-FAIL: a malformed route value did not produce a described 400: {(int)malformed.StatusCode} {malformedBody}");
    await app.StopAsync().ConfigureAwait(false);
    return 1;
}

// The error path. ProblemDetails is what an endpoint answers when something has already gone wrong,
// so it is the last thing that may fail to serialize — and it needed a context of its own.
using var failing = await client.GetAsync("/api/missing").ConfigureAwait(false);
var failingBody = await failing.Content.ReadAsStringAsync().ConfigureAwait(false);

if ((int)failing.StatusCode != 404 || !failingBody.Contains("title", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"AOT-WEB-FAIL: the error path did not produce ProblemDetails: {(int)failing.StatusCode} {failingBody}");
    await app.StopAsync().ConfigureAwait(false);
    return 1;
}

Console.WriteLine($"AOT-WEB-OK: {body} | {echoBody} | {failingBody}");
await app.StopAsync().ConfigureAwait(false);
return 0;
