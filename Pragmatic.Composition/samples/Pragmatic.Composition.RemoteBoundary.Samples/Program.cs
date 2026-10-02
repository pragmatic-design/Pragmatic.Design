using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Composition.Remote;
using Pragmatic.Result;

// ============================================================================
// What this sample demonstrates
//
// The PragmaticInvokeRequest / PragmaticInvokeResponse wire format used by the
// SG-emitted /_pragmatic/invoke endpoint on a Pragmatic host, and the
// HttpActionInvoker<TAction, TReturn> client runtime that [RemoteBoundary<T>]
// resolves to on the caller side. Everything runs in one process: a lean
// Kestrel host serves the endpoint with an inline handler, then we build the
// invoker and fire three invocations to exercise the happy / failure /
// unroutable paths.
//
// For the full attribute-driven pipeline (SG-generated HttpInvokers,
// PragmaticInvokeEndpoint, typed client lookup via DI), see the showcase
// distributed deployment — Showcase.Billing.Host paired with
// Showcase.Host.Distributed.
// ============================================================================

const string BaseUrl = "http://localhost:5390";
const string HttpClientName = "billing";

Console.WriteLine("=== Pragmatic.Composition RemoteBoundary sample ===\n");

using var cts = new CancellationTokenSource();

// ---------------------------------------------------------------------------
// Server: minimal WebApplication exposing /_pragmatic/invoke
// ---------------------------------------------------------------------------
var serverTask = StartServerAsync(cts.Token);
await WaitForServerAsync(BaseUrl, TimeSpan.FromSeconds(5));

// ---------------------------------------------------------------------------
// Client: HttpClientFactory + HttpActionInvoker
// ---------------------------------------------------------------------------
var clientServices = new ServiceCollection();
clientServices.AddLogging(b => b.ClearProviders());
clientServices.AddHttpClient(HttpClientName, c => c.BaseAddress = new Uri(BaseUrl));
// The analyzer flags BuildServiceProvider in ASP.NET apps, but here we
// intentionally keep the client DI separate from the server's WebApplication.
#pragma warning disable ASP0000
await using var clientSp = clientServices.BuildServiceProvider();
#pragma warning restore ASP0000
var httpFactory = clientSp.GetRequiredService<IHttpClientFactory>();

var invoker = new HttpActionInvoker<CalculateTaxAction, TaxQuote>(httpFactory, HttpClientName);

// ---- 1. Happy path --------------------------------------------------------
var success = await invoker.InvokeAsync(new CalculateTaxAction { Amount = 100m, Region = "IT" });
Print("happy path (IT, 100 €)", success);

// ---- 2. Server-side failure ----------------------------------------------
// The region "XX" is a sentinel the server treats as unknown → NotFoundError.
var notFound = await invoker.InvokeAsync(new CalculateTaxAction { Amount = 50m, Region = "XX" });
Print("failure (unknown region)", notFound);

// ---- 3. Unroutable action type -------------------------------------------
// The server only handles CalculateTaxAction. Sending a stranger action type
// lets the sample display the RemoteError fallback path on the client side.
var unknown = await InvokeRawAsync(httpFactory, "Sample.UnknownAction", JsonSerializer.SerializeToElement(new { Foo = 1 }));
Console.WriteLine($"  [3/3] unknown action-type : {unknown}");

Console.WriteLine("\n=== All samples completed. ===");

// Stop the server cleanly before exit.
await cts.CancelAsync();
try { await serverTask; } catch (OperationCanceledException) { /* expected */ }


// ===========================================================================
// Helpers
// ===========================================================================

static Task StartServerAsync(CancellationToken ct)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls(BaseUrl);

    var app = builder.Build();

    // Inline handler for /_pragmatic/invoke — dispatches on ActionType.
    app.MapPost("/_pragmatic/invoke", async (PragmaticInvokeRequest request, CancellationToken inner) =>
    {
        if (request.ActionType == typeof(CalculateTaxAction).FullName)
        {
            var action = request.Payload.Deserialize<CalculateTaxAction>()!;
            var result = await action.Execute(inner);
            return result.Match<IResult>(
                value => Results.Ok(new PragmaticInvokeResponse(true,
                    JsonSerializer.SerializeToElement(value), null) { StatusCode = 200 }),
                error => Results.Ok(new PragmaticInvokeResponse(false, null, new ProblemDetails
                {
                    Title = error.Title,
                    Detail = error.Description,
                    Status = error.StatusCode,
                    Extensions = { ["code"] = error.Code }
                }) { StatusCode = error.StatusCode }));
        }

        // Unroutable: echo back a ProblemDetails so the client sees a RemoteError.
        return Results.Ok(new PragmaticInvokeResponse(false, null, new ProblemDetails
        {
            Title = "Unknown action type",
            Detail = $"No handler for {request.ActionType}",
            Status = 404,
            Extensions = { ["code"] = "UNKNOWN_ACTION" }
        }) { StatusCode = 404 });
    });

    return app.RunAsync(ct);
}

static async Task WaitForServerAsync(string url, TimeSpan timeout)
{
    using var probe = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(1) };
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            // Any response (even a 404 from the default minimal API) proves the socket is up.
            var response = await probe.PostAsync("/_pragmatic/invoke",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            _ = response.StatusCode;
            return;
        }
        catch (HttpRequestException)
        {
            await Task.Delay(100);
        }
    }
    throw new TimeoutException($"Server did not come up within {timeout}.");
}

static async Task<string> InvokeRawAsync(IHttpClientFactory factory, string actionType, JsonElement payload)
{
    var client = factory.CreateClient(HttpClientName);
    var request = new PragmaticInvokeRequest(actionType, payload);
    using var response = await client.PostAsJsonAsync("/_pragmatic/invoke", request);
    var body = await response.Content.ReadFromJsonAsync<PragmaticInvokeResponse>();
    if (body is null) return "<no response>";
    var code = body.Error?.Extensions.TryGetValue("code", out var c) == true ? c?.ToString() : null;
    return $"IsSuccess={body.IsSuccess}, error-code={code ?? "<none>"}, detail=\"{body.Error?.Detail}\"";
}

static void Print(string label, Result<TaxQuote, IError> result)
{
    result.Match(
        value => Console.WriteLine($"  [{label,-28}]: {value.Gross:0.00} {value.Currency} (tax {value.Tax:0.00})"),
        error => Console.WriteLine($"  [{label,-28}]: FAILED — {error.Code}: {error.Description}"));
}


// ===========================================================================
// DomainAction + result types shared between server & client
//
// A real deployment would define these in a class library shared between the
// server and the caller's [RemoteBoundary] module. Keeping them in the same
// file here is a sample simplification.
// ===========================================================================

public sealed class CalculateTaxAction : DomainAction<TaxQuote>
{
    public decimal Amount { get; init; }
    public string Region { get; init; } = "IT";

    public override Task<Result<TaxQuote, IError>> Execute(CancellationToken ct = default)
    {
        var rate = Region switch
        {
            "IT" => 0.22m,
            "US" => 0.07m,
            "DE" => 0.19m,
            _ => -1m
        };

        if (rate < 0)
            return Task.FromResult(Result<TaxQuote, IError>.Failure(
                new NotFoundError { Code = "REGION_NOT_FOUND", Title = $"Unknown region '{Region}'" }));

        var tax = Math.Round(Amount * rate, 2);
        return Task.FromResult(Result<TaxQuote, IError>.Success(
            new TaxQuote(Amount + tax, tax, Region == "US" ? "USD" : "EUR")));
    }
}

public sealed record TaxQuote(decimal Gross, decimal Tax, string Currency);

public sealed class NotFoundError : IError
{
    public string Code { get; init; } = "NOT_FOUND";
    public string Title { get; init; } = "Not found";
    public string? Description { get; init; }
    public int StatusCode => 404;
}
