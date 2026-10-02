# Pragmatic.Client

Compile-time typed API clients generated from Pragmatic manifests. No hand-written HTTP code, full `Result<T, IError>` integration, and a generated JSON context (`PragmaticJsonContext`, written out from the manifest) instead of reflection-based serialization — designed for AOT, not yet proven by a Native AOT smoke.

> ⚠️ **Known limitations**
>
> - **`manifest.assembly` requires a dot**: the boundary name is derived by splitting `assembly` on `.` and taking the last segment. `"Showcase.Booking"` → `Booking`, but `"SampleBookingApi"` → `SampleBookingApi` (whole string). Always author the manifest with a dotted assembly path.
> - **Generated code is not `#nullable enable`-clean**: consumer projects with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` need to `NoWarn` on `CS8618 / CS8669 / CS8604 / CS8601`: the SG does not emit explicit nullable directives.
> - **Multiple manifests per client project**: each client project consumes ONE manifest (Mode A file or Mode B module set producing one aggregated manifest); pointing two manifests at the same namespace collides on shared type names (`PagedResult<T>`, DTOs).
>
> A runnable end-to-end demo lives in [`samples/Pragmatic.Client.Samples`](samples/Pragmatic.Client.Samples/).

## The Problem

Consuming Pragmatic APIs from .NET clients (Blazor, MAUI, console apps, other services) requires:
- Writing boilerplate `HttpClient` calls for every endpoint
- Manually deserializing responses and mapping errors
- Keeping client code in sync when the server API changes
- Handling ProblemDetails (RFC 7807) error responses consistently

This leads to fragile, duplicated code that drifts from the actual API contract over time.

## The Solution

**Pragmatic.Client** uses a source generator that reads `PragmaticManifest.json` at compile time and generates:

| Generated artifact | Purpose |
|---|---|
| `I{Boundary}Client` | Typed interface with `Result<T, IError>` return types |
| `{Boundary}HttpClient` | Full `HttpClient` implementation with error mapping |
| Request DTOs | `sealed record` per endpoint with request body |
| Response DTOs | `sealed record` per entity/DTO in the manifest |
| Enums | Enum types from the manifest |
| Error types | `IError`-implementing records with typed error codes |
| DI registration | `Add{Boundary}Client(baseUrl)` extension method |

Two discovery modes ensure the manifest reaches the generator:
- **Mode A (AdditionalFiles)**: drop a `manifest.json` or `PragmaticManifest.json` in the project
- **Mode B (Assembly refs)**: reference the domain assembly with `Private="false"` and the SG reads `[PragmaticMetadata]` attributes automatically

Mode A takes priority. Mode B enables **zero-file-sync**: just reference the server project and the client is always up to date.

## Quick Start

### 1. Install packages

```xml
<PackageReference Include="Pragmatic.Client" />
<PackageReference Include="Pragmatic.Client.SourceGenerator"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### 2. Add the manifest (Mode A)

Place a `PragmaticManifest.json` in your client project and register it as an additional file:

```xml
<ItemGroup>
  <AdditionalFiles Include="PragmaticManifest.json" />
</ItemGroup>
```

Or use Mode B by referencing the domain assembly directly:

```xml
<ProjectReference Include="..\Showcase.Booking\Showcase.Booking.csproj"
                  Private="false" ReferenceOutputAssembly="true" />
```

### 3. Register and use

```csharp
// Program.cs
services.AddBookingClient("https://api.example.com");

// Inject and call
public class GuestPage(IBookingClient client)
{
    public async Task LoadGuest(Guid id)
    {
        var result = await client.GetGuest(id);

        if (result.IsSuccess)
            Console.WriteLine($"Guest: {result.Value.FirstName}");
        else
            Console.WriteLine($"Error: {result.Error.Title}");
    }
}
```

## Features

- **Compile-time generation** -- no runtime reflection, no dynamic proxies
- **Result-based error handling** -- every method returns `Result<T, IError>` or `VoidResult<IError>`, never throws
- **Full request shape** -- path parameters (URL-encoded), JSON bodies, and query parameters as optional
  arguments appended to the URL; the manifest's HTTP verb is used as declared
- **Diagnostics** -- `PRAG2300` unreadable manifest (Error); `PRAG2301` a type degraded to `object`,
  `PRAG2302` a boundary filter that matched nothing, `PRAG2303` an endpoint whose response type is
  missing from the manifest, `PRAG2304` a shared type two manifests describe differently (Warnings)
- **Collections and paging** -- `List<T>`/`IReadOnlyList<T>` responses map to `T[]`; `PagedResult<T>`
  responses map to a generated `PagedResult<T>` DTO
- **ProblemDetails error mapping** -- server errors are deserialized and matched to typed `IError` records via error code switch
- **Typed error records with extensions** -- error-specific context (e.g., `ConflictError.ConflictingId`) flows through ProblemDetails extensions
- **Named HttpClient** -- uses `IHttpClientFactory` pattern via `AddHttpClient<TInterface, TImpl>`
- **Boundary filtering** -- generate clients for specific boundaries via `PragmaticClientBoundaries` MSBuild property
- **Dual discovery** -- manifest from file (Mode A) or assembly attribute (Mode B)
- **DTO generation** -- entities, DTOs, and enums from the manifest become local types in the client namespace

## TypeScript client (`pragmatic-client` CLI)

The `Pragmatic.Client.Cli` dotnet tool emits a TypeScript client from the same manifest:

```bash
pragmatic-client ts --manifest PragmaticManifest.json --out src/api/
```

Per module it writes `types.ts` (DTO interfaces + enum unions), `errors.ts` (`ApiError` +
error-code union), `client.ts` (fetch-based class returning the discriminated
`ApiResult<T> = { ok: true; value } | { ok: false; error }`), and `paging.ts`
(`PagedResult<T>`). Every file carries the manifest SHA-256 header for drift detection.
Host-aggregated manifests emit one folder per module. SSE endpoints are skipped
(consume them with `EventSource`/stream readers).

## Configuration

### Boundary Filtering

Generate clients only for specific boundaries:

```xml
<PropertyGroup>
  <PragmaticClientBoundaries>Booking;Billing</PragmaticClientBoundaries>
</PropertyGroup>
```

This filters endpoints by `operationId` prefix, generating only matching clients.

### Custom HttpClient Configuration

```csharp
services.AddBookingClient("https://api.example.com", client =>
{
    client.DefaultRequestHeaders.Add("X-Api-Key", "my-key");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

## Packages

| Package | Target | Description |
|---|---|---|
| `Pragmatic.Client` | `net10.0` | Runtime: `ApiError`, `PragmaticClientException` |
| `Pragmatic.Client.SourceGenerator` | `netstandard2.0` | Source generator: reads manifests, generates typed clients |

### Dependencies

| Package | Depends on |
|---|---|
| `Pragmatic.Client` | `Pragmatic.Abstractions`, `Pragmatic.Result` |
| `Pragmatic.Client.SourceGenerator` | `System.Text.Json` (compile-time only) |

## Status

**Preview** within 1.0.0-alpha — the C# client generated from the endpoint manifest and the
`pragmatic-client` TypeScript CLI; the Showcase is the reference application that uses it. See the
[roadmap](../docs/ROADMAP.md).

## Requirements

- .NET 10 (runtime)
- C# 14 / Roslyn 4.12+ (source generator)
- A `PragmaticManifest.json` (Mode A) or domain assembly reference with `[PragmaticMetadata]` (Mode B)

## Project Structure

```
Pragmatic.Client/
├── src/
│   ├── Pragmatic.Client/                     # Runtime helpers
│   │   ├── ApiError.cs                       # Generic ProblemDetails → IError record
│   │   └── PragmaticClientException.cs       # Exception for unmappable responses
│   └── Pragmatic.Client.SourceGenerator/     # Compile-time generator
│       └── PragmaticClientGenerator.cs       # Manifest → typed client code
└── tests/
    └── Pragmatic.Client.Tests/
        └── Generator/                        # Snapshot tests for generated output
```

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Client is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
