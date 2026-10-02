---
name: pragmatic-use-client
description: Use when a front end or another service calls a Pragmatic API, hand-written HttpClient code appears, or PRAG2300-2304 fire — the typed client Pragmatic.Client generates, or a TypeScript one. Same host is pragmatic-use-actions-endpoints.
---

# Pragmatic Use Client

**Covers:** Call a Pragmatic API from another .NET app (Blazor, MAUI, console, service) through the typed client Pragmatic.Client generates from the API manifest, or a TypeScript client from the pragmatic-client CLI.

A Pragmatic API already describes itself: every endpoint, its verb, route, body, response and errors
is in the **manifest** the server's generator writes. `Pragmatic.Client.SourceGenerator` reads that
manifest at compile time and writes the client — no hand-written `HttpClient` code, no drift: change the
server and the client stops compiling where it no longer matches.

Inside one host, modules call each other through the generated boundary interfaces
(`I{Boundary}Actions`), never over HTTP. This skill is for a **different process**.

## Packages

```xml
<PackageReference Include="Pragmatic.Client" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.Client.SourceGenerator" Version="1.0.0-alpha.*"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Where the manifest comes from

**Mode B — reference the module (recommended).** Reference the server's boundary library at compile
time only; the generator reads the `[PragmaticMetadata]` it carries:

```xml
<ProjectReference Include="..\Shop.Orders\Shop.Orders.csproj" Private="false" />
```

`Private="false"` keeps the server's DLL out of the client's output — for a Blazor WebAssembly client it
never reaches the browser. The client is always in step with the server it builds against.

**Mode A — a manifest file**, for a client that cannot reference the server's source:

```xml
<ItemGroup>
  <AdditionalFiles Include="PragmaticManifest.json" />   <!-- any file ending in manifest.json -->
</ItemGroup>
```

Mode A wins when both are present. ⚠️ Nothing in the framework writes the manifest to disk or serves it
over HTTP yet: the server holds it as the generated `PragmaticManifest.Json` constant. A Mode A client
needs a build step of yours that writes that constant to a file.

## Use

```csharp
// Program.cs of the client
builder.Services.AddOrdersClient("https://api.example.com", http =>
{
    http.Timeout = TimeSpan.FromSeconds(30);
});

public sealed class OrderPage(IOrdersClient orders)
{
    public async Task LoadAsync(Guid id)
    {
        var result = await orders.GetOrder(id);
        if (result.IsSuccess)
            Show(result.Value);
        else
            ShowError(result.Error);            // a typed error record, or ApiError for an unknown code
    }
}
```

What is generated, per boundary: `I{Boundary}Client` and `{Boundary}HttpClient` (through
`IHttpClientFactory`), request and response `sealed record`s, enums, `IError` records with the server's
error codes and their ProblemDetails extensions (`ConflictError.ConflictingId`), a `PagedResult<T>`, and
`Add{Boundary}Client(baseUrl, configure?)`.

- Every method returns `Result<T, IError>` or `VoidResult<IError>`: a 4xx/5xx is a value, not an
  exception. `PragmaticClientException` is only for a response that cannot be mapped at all.
- Path parameters are URL-encoded, query parameters are optional arguments, the verb is the manifest's.
- Authentication is yours: add a `DelegatingHandler` in `configure` (or on the named client) that sets
  the bearer token.
- `<PragmaticClientBoundaries>Orders;Billing</PragmaticClientBoundaries>` generates only those clients.

## Limits to know

- ⚠️ **One manifest per client project.** Two manifests into one namespace collide on shared type names
  (`PagedResult<T>`, DTOs) — **PRAG2304** when they describe a type differently. One project per API.
- ⚠️ The boundary name is the **last dot-separated segment** of the manifest's `assembly`
  (`Shop.Orders` → `Orders`): an undotted assembly name becomes the whole name.
- ⚠️ The generated code is not nullable-clean: a project with warnings as errors needs
  `<NoWarn>$(NoWarn);CS8618;CS8669;CS8604;CS8601</NoWarn>` for now.
- Diagnostics: **PRAG2300** unreadable manifest (error), **PRAG2301** a type degraded to `object`,
  **PRAG2302** a boundary filter that matched nothing, **PRAG2303** a response type missing from the
  manifest.
- Server-sent-event endpoints have no generated method: consume them with a stream reader.

## TypeScript

The `Pragmatic.Client.Cli` tool writes a fetch-based client from the same manifest:

```bash
dotnet tool install Pragmatic.Client.Cli
pragmatic-client ts --manifest PragmaticManifest.json --out src/api/
```

Per module: `types.ts` (DTOs, enum unions), `errors.ts` (`ApiError`, error-code union), `client.ts`
(`ApiResult<T> = { ok: true; value } | { ok: false; error }`), `paging.ts`. Each file carries the
manifest's hash, so a stale client is visible in review. It needs the manifest file (see Mode A).

## Testing the client

The generated client takes an `HttpClient` in its constructor, so a test builds it over the test
server: `new OrdersHttpClient(factory.CreateClient())`. Assert on the `Result`: a refused call is
`result.IsFailure` with the typed error, not an exception.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application — code
that compiles and that `Showcase.IntegrationTests` exercises — and kept identical to it by the gate: the
client project and the compile-only reference its manifest comes from, the registration, and the tests
that drive the generated client against the running application.
