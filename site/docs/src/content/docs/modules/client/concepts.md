---
title: "Architecture and Concepts"
description: "Pragmatic.Client follows the Pragmatic ecosystem philosophy: **everything known at compile time should be resolved at compile time**. The source generator reads"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Client/docs/concepts.md
sidebar:
  order: 1
---
## Overview

Pragmatic.Client follows the Pragmatic ecosystem philosophy: **everything known at compile time should be resolved at compile time**. The source generator reads a JSON manifest describing the server API and produces a fully typed HTTP client with no runtime reflection.

## Manifest Schema

The `PragmaticManifest.json` (schema version `pragmatic-manifest/v1`) describes the full API surface:

```json
{
  "$schema": "pragmatic-manifest/v1",
  "version": "1.0.0",
  "assembly": "Showcase.Booking",
  "endpoints": [...],
  "types": [...]
}
```

### Endpoints

Each endpoint entry maps to one method on the generated client interface:

| Field | Type | Description |
|---|---|---|
| `operationId` | `string` | Boundary-qualified name, e.g. `"Booking.CreateGuest"` |
| `httpMethod` | `string` | HTTP verb: `Get`, `Post`, `Put`, `Patch`, `Delete` |
| `fullRoute` | `string` | Route template with path parameters, e.g. `"/api/v1/guests/{id}"` |
| `summary` | `string?` | XML doc summary for the generated method |
| `isVoid` | `bool` | `true` for endpoints that return no body (e.g. DELETE 204) |
| `response` | `{ type }` | Fully-qualified response type |
| `parameters` | `[{ name, in, type }]` | Path parameters (required arguments, URL-encoded) and query parameters (optional arguments appended to the query string) |
| `requestBody` | `{ properties: [...] }` | Request body properties |

### Types

The `types` array describes entities, DTOs, enums, and errors:

| Kind | What gets generated |
|---|---|
| `"entity"` | Response DTO record (suffixed with `Dto` if not already) |
| `"dto"` | Response DTO record (kept as-is if already suffixed) |
| `"enum"` | `enum` type with values from `values` array |
| `"error"` | `sealed record` implementing `IError` with `Code`, `StatusCode`, `Title` |

Error types can include `extensions` -- additional properties populated from ProblemDetails extension fields.

## Discovery Modes

### Mode A: AdditionalFiles

The manifest JSON is included in the project as an `AdditionalFiles` MSBuild item. The generator picks up any file ending in `manifest.json` or `PragmaticManifest.json`.

```xml
<ItemGroup>
  <AdditionalFiles Include="PragmaticManifest.json" />
</ItemGroup>
```

This is explicit and works in any project type. Use this when the client project does not reference the server assembly.

### Mode B: Assembly Attributes

When the client project references a Pragmatic domain assembly (e.g., `Showcase.Booking`), the generator scans `[PragmaticMetadata]` attributes on referenced assemblies. The manifest JSON is embedded in the attribute with `MetadataCategory = 18`.

```xml
<ProjectReference Include="..\Showcase.Booking\Showcase.Booking.csproj"
                  Private="false" ReferenceOutputAssembly="true" />
```

`Private="false"` means the domain DLL is **not** copied to the client output. The SG only needs it at compile time to extract the manifest. At runtime, the client depends only on `Pragmatic.Client` and `Pragmatic.Result`.

**Priority**: Mode A manifests are processed first. If a manifest from Mode A and Mode B share the same `assembly` name, the Mode B version is skipped (deduplication by assembly name).

## Generated Code Architecture

```
PragmaticManifest.json
        │
        ▼
┌──────────────────────────┐
│  PragmaticClientGenerator │  (compile-time)
└──────────────────────────┘
        │
        ├── I{Boundary}Client.g.cs          Interface
        ├── {Boundary}HttpClient.g.cs       Implementation
        ├── {Method}Request.g.cs            Request DTOs
        ├── {Entity}Dto.g.cs                Response DTOs
        ├── {Enum}.g.cs                     Enums
        ├── {Error}.g.cs                    Typed errors
        └── {Boundary}ClientExtensions.g.cs DI registration
```

### Interface Generation

Each endpoint becomes a method on `I{Boundary}Client`:
- Non-void endpoints return `Task<Result<TResponse, IError>>`
- Void endpoints (e.g., DELETE 204) return `Task<VoidResult<IError>>`
- Path parameters become method arguments
- Request bodies become a typed `{Method}Request` parameter

### HttpClient Implementation

The `{Boundary}HttpClient` class:
1. Takes `HttpClient` via constructor injection (from `IHttpClientFactory`)
2. Maps HTTP verbs: GET, POST, PUT, PATCH, DELETE
3. On success: deserializes the response body via `ReadFromJsonAsync<T>`
4. On failure: calls `MapErrorAsync` which reads ProblemDetails JSON and switches on the `code` field

### Error Mapping Pipeline

```
HTTP error response
        │
        ▼
Read response body as JSON
        │
        ▼
Extract "code" and "title" from ProblemDetails
        │
        ▼
Switch on "code":
  ├── Known code → Typed error record (e.g., ConflictError)
  │                 with extension properties populated
  └── Unknown    → ApiError { Code, Title, StatusCode }
```

The `ApiError` record serves as the catch-all for unrecognized error codes. It implements `IError` and carries the full ProblemDetails context including `Extensions`.

### Error Extension Properties

When errors in the manifest declare `extensions`, the generator:
1. Adds typed properties to the error record (e.g., `public string? ConflictingId { get; init; }`)
2. Populates them in `MapErrorAsync` by reading JSON properties from the ProblemDetails response

```json
{
  "simpleName": "DuplicateEmailError",
  "kind": "error",
  "errorCode": "DUPLICATE_EMAIL",
  "errorStatusCode": 409,
  "extensions": [
    { "name": "existingUserId", "type": "System.Guid" }
  ]
}
```

Generates:
```csharp
public sealed record DuplicateEmailError : IError
{
    public string Code => "DUPLICATE_EMAIL";
    public int StatusCode => 409;
    public string Title => "DuplicateEmailError";
    public System.Guid? ExistingUserId { get; init; }
}
```

## Namespace and Naming

- **Namespace**: the client assembly name becomes the namespace for all generated types
- **Boundary name**: derived from the last segment of the manifest `assembly` field (e.g., `Showcase.Booking` produces `Booking`)
- **DTO naming**: entity types without a `Dto` suffix get one appended; types already suffixed are kept as-is
- **Request DTO naming**: `{MethodName}Request` (e.g., `CreateGuestRequest`)

## Type Resolution

The generator resolves types from the manifest:
- Primitive CLR types (`string`, `int`, `Guid`, `DateTimeOffset`, etc.) map directly
- Entity/DTO types declared in the manifest `types` array resolve to the generated DTO records
- Generic types (e.g., `PagedResult<GuestDto>`) currently map to `object` -- this is a known limitation
- Unknown types fall back to `object`
