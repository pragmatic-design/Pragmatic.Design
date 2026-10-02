# MCP tools (Model Context Protocol)

`[McpTool]` exposes an endpoint as an MCP tool — AI agents (Claude, IDE assistants, any MCP
client) can discover and invoke it. Schemas come from the **compile-time manifest**: zero
reflection.

```csharp
[Endpoint(HttpVerb.Post, "/api/booking-notes")]
[McpTool(Description = "Stores a short booking note.")]
public partial class CreateBookingNoteEndpoint : Endpoint<string>
{
    public required string Text { get; set; }
    // ...
}
```

Host opt-in (package `Pragmatic.Endpoints.Mcp`):

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseMcp();                        // MCP server (streamable HTTP) at /mcp — auth required by default
    // ...
});
```

`/mcp` **requires authorization by default** (`McpOptions.RequireAuthorization = true`). Behind a
trusted gateway you can opt out, and — if your app carries identity in headers rather than a bearer
token — opt in to forwarding those headers to the self-call:

```csharp
app.UseMcp(o =>
{
    o.RequireAuthorization = false;                 // e.g. gateway already authenticated the caller
    o.ForwardedHeaders.Add("X-User-Id");            // header-based identity → propagate to the self-call
    o.ForwardedHeaders.Add("X-User-Permissions");
});
```

## How tools are built

- **Name**: `[McpTool(Name = ...)]`, or the operation id sanitized — boundary + operation,
  lowercased, `Endpoint`/`Action`/`Mutation`/`Query` suffix stripped
  (`Booking.CreateBookingNoteEndpoint` → `booking_createbookingnote`). Collisions get a
  numeric suffix.
- **Description**: the attribute's, or the `[EndpointSummary]`.
- **Input schema**: route + query parameters and request-body properties from the manifest
  (types, `required`, `maxLength`).
- Streaming (SSE) and file-upload endpoints are not tool-shaped and are skipped.

## Execution model — self-HTTP by design

Tool calls are executed by **calling the endpoint over HTTP on the same host**, so the FULL
pipeline applies exactly as for any client: authorization, validation, tenancy, rate
limiting, idempotency (`[Idempotent]` endpoints get a per-invocation key automatically).
Identity travels via a forwarded-header allowlist from the incoming MCP request. The default
is `Authorization` **only** — a verified bearer token. Client-asserted identity headers
(`X-User-*`, `X-Tenant-Id`) are **not** forwarded by default because an MCP client can spoof
them; add them to `McpOptions.ForwardedHeaders` only when a trusted gateway sets them.

2xx responses become the tool result payload; ProblemDetails/errors surface as
`isError: true` results with the reason.

## Options and notes

- `McpOptions`: `Path` (default `/mcp`), `RequireAuthorization` (default `true`),
  `AuthorizationPolicy` (default policy when null), `ForwardedHeaders` (default `[Authorization]`),
  `SelfBaseAddress` (default: first bound server address).
- `/mcp` **requires authorization by default**. Both the *listing* (`ListTools`) and *execution*
  (`CallTool`) are gated: listing by the `/mcp` endpoint's own `RequireAuthorization`, execution
  additionally by each tool endpoint's own pipeline. Set `RequireAuthorization = false` to expose
  the listing anonymously (trusted-gateway deployments).
- The package sets `IsAotCompatible=false` (MCP SDK transport not fully trim-annotated);
  the limit is isolated here — the rest of the app keeps its AOT guarantees.
- Testing under WebApplicationFactory: route the self-call named client
  (`McpToolExecutor.HttpClientName`) through `TestServer.CreateHandler()` — see
  `ShowcaseWebFactory` in the Showcase.
