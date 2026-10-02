# Typed test client (`Api.*`)

The Pragmatic.Testing source generator emits a **typed API client** into your test project,
with routes and verbs resolved **at compile time** — rename a route and every test that calls
it breaks at build, not at runtime.

## Two tiers

| Tier | Where | What |
|------|-------|------|
| 1 — `ApiRoutes` | app assembly (unified SG) | per-boundary route constants (`{Name}Method`, `{Name}Template`) + typed URL builders (`ApiRoutes.Orders.GetOrder(id)`) |
| 2 — `Api` | test project (Testing SG) | `Api.{Boundary}.{Name}Async(client, ...)` returning `ApiResponse` / `ApiResponse<T>` |

Tier 2 correlates the app's `ApiRoutes` builders (typed parameters) with its
`[assembly: PragmaticEndpointContract]` attributes (verb, body, response type) by
`(Boundary, OperationName)`.

## Setup

The test project references the app and both Testing pieces:

```xml
<ProjectReference Include="...\Pragmatic.Testing\src\Pragmatic.Testing\Pragmatic.Testing.csproj" />
<ProjectReference Include="...\Pragmatic.Testing.SourceGenerator.csproj"
    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Usage in hand-written tests

```csharp
using Pragmatic.Tests.Generated;   // generated Api client
using Pragmatic.Testing;           // ApiResponse, assertions, PragmaticJson

// POST with body (anonymous object or typed DTO — serialized with host conventions)
var created = await Api.Booking.CreateGuestAsync(Client, new
{
    firstName = "Ada", lastName = "Lovelace", email = "ada@example.com"
});
created.Raw.ShouldBeCreated();

// GET by id — URL built from typed parameters, response deserialized lazily
var fetched = await Api.Guests.GetGuestAsync(Client, guestId);
var dto = await fetched.ReadAsync();   // ApiResponse<GuestDto> → GuestDto

// Tier 1 directly, for raw HttpClient calls or link generation
var url = ApiRoutes.Guests.GetGuest(guestId);
```

## Notes and limits

- **Body parameter is `object`**: anonymous objects are idiomatic in tests; typed DTO
  instances work too (serialization is by runtime type, `PragmaticJson.Options`).
- **Entity-returning mutations**: generated entities have internal setters, so
  `ReadAsync<Entity>()` cannot populate them — read `ReadBodyAsync()` and parse the JSON
  (DTO-returning endpoints deserialize fine).
- **SSE endpoints are skipped**: streaming responses are consumed with
  `System.Net.ServerSentEvents.SseParser` over the raw stream.
- **Name collisions**: two endpoints collapsing to the same member name warn (PRAG0526);
  set a distinct `Name` on `[Endpoint]`.
