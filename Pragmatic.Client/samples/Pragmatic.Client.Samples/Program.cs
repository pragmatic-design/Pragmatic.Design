using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Client.Samples;

// ======================================================================
// Pragmatic.Client walkthrough — manifest-driven typed client
// ======================================================================
//
// This sample is a single-process runnable demo:
//   1. A minimal API server is started in-process, with handlers that
//      match the routes declared in PragmaticManifest.json.
//   2. The source generator (Pragmatic.Client.SourceGenerator) has read
//      that same manifest at compile time and produced:
//        - interface IBookingClient
//        - class BookingHttpClient : IBookingClient
//        - sealed record CreateGuestRequest / GuestDto / ConflictError
//        - extension AddBookingClient(baseUrl) for DI registration
//   3. We register the client against the in-process server URL and
//      exercise every endpoint end-to-end. Results surface as
//      Result<T, IError> and VoidResult<IError> — same shape your
//      production code will see.
//
// Self-contained: no external API, no Docker, no config. Just `dotnet run`.

Console.WriteLine("=== Pragmatic.Client Samples ===\n");

// ---- 1. Start the in-process API server on an ephemeral port ----------

var serverBuilder = WebApplication.CreateBuilder();
serverBuilder.Logging.ClearProviders();          // keep sample output tidy
serverBuilder.WebHost.UseUrls("http://127.0.0.1:0");

var server = serverBuilder.Build();

var guestStore = new ConcurrentDictionary<Guid, GuestDto>();

server.MapPost("/api/v1/guests", (CreateGuestRequest req) =>
{
    // Demo conflict: reject a specific email so we can exercise the
    // Result<T, IError> error branch below.
    if (req.Email == "conflict@example.com")
        return Results.Conflict(new { code = "CONFLICT", title = "Email already in use" });

    var id = Guid.CreateVersion7();
    guestStore[id] = new GuestDto
    {
        Id = id,
        FirstName = req.FirstName,
        LastName = req.LastName,
        Email = req.Email,
    };
    return Results.Created($"/api/v1/guests/{id}", id);
});

server.MapGet("/api/v1/guests/{id:guid}", (Guid id) =>
    guestStore.TryGetValue(id, out var guest)
        ? Results.Ok(guest)
        : Results.NotFound(new { code = "NOT_FOUND", title = $"Guest {id} not found" }));

server.MapGet("/api/v1/guests", () =>
    Results.Ok(guestStore.Values.ToList()));

server.MapDelete("/api/v1/guests/{id:guid}", (Guid id) =>
    guestStore.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound());

await server.StartAsync();
var baseUrl = server.Urls.First();
Console.WriteLine($"In-process server: {baseUrl}\n");

// ---- 2. Build the client against that server URL ----------------------

var clientServices = new ServiceCollection();
clientServices.AddBookingClient(baseUrl);
var clientProvider = clientServices.BuildServiceProvider();
var client = clientProvider.GetRequiredService<IBookingClient>();

// ---- 3. Exercise every generated endpoint -----------------------------

Console.WriteLine("--- Create guests ---");
var alice = await client.CreateGuest(new CreateGuestRequest
{
    FirstName = "Alice",
    LastName = "Bianchi",
    Email = "alice@example.com",
});
var bob = await client.CreateGuest(new CreateGuestRequest
{
    FirstName = "Bob",
    LastName = "Costa",
    Email = "bob@example.com",
});
PrintResult("CreateGuest(alice)", alice);
PrintResult("CreateGuest(bob)", bob);

Console.WriteLine("\n--- Create guest that collides (Result error branch) ---");
var collision = await client.CreateGuest(new CreateGuestRequest
{
    FirstName = "Carol",
    LastName = "De Luca",
    Email = "conflict@example.com",
});
PrintResult("CreateGuest(conflict)", collision);

Console.WriteLine("\n--- Get guest ---");
if (alice.IsSuccess)
{
    var found = await client.GetGuest(alice.Value);
    PrintResult("GetGuest(alice.Id)", found);
}

Console.WriteLine("\n--- Delete guest (VoidResult) ---");
if (alice.IsSuccess)
{
    var deleted = await client.DeleteGuest(alice.Value);
    Console.WriteLine(deleted.IsSuccess
        ? "  delete succeeded       : 204 No Content"
        : $"  delete failed          : {deleted.Error.Title}");
}

// ---- 4. Shut down the in-process server -------------------------------

await server.StopAsync();
Console.WriteLine("\n=== Sample completed. ===");

static void PrintResult<T>(string label, Pragmatic.Result.Result<T, Pragmatic.Result.IError> result)
{
    if (result.IsSuccess)
        Console.WriteLine($"  {label,-26} : OK   {result.Value}");
    else
        Console.WriteLine($"  {label,-26} : FAIL code={result.Error!.Code}  title={result.Error!.Title}");
}

