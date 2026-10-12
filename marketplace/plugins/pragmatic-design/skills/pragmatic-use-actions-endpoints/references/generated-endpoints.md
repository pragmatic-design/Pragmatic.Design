# Endpoints you do not write, and the published contract

Part of `pragmatic-use-actions-endpoints`, which keeps what most tasks need. This file holds
what a task reads only when it reaches it.

### 9. Three endpoints you do not write

An endpoint does not always come from a class of yours. Three attributes generate the whole thing,
and reaching for them is almost always better than hand-rolling the equivalent.

**`[Autocomplete]`: search-as-you-type, from one property.**

```csharp
public partial class Guest : Entity
{
    [Autocomplete] public string Email { get; private set; } = "";
    [Autocomplete(Route = "api/guests/by-code", DefaultLimit = 20)] public string Code { get; private set; } = "";
}
```

Generates `GET {entity}/autocomplete/{property}` returning `AutocompleteItem<TKey>` (the typed id
plus the display text, serialized as **`value`**, not `label`), capped at `DefaultLimit` (10).
`[Autocomplete<TDto>]` projects into your own DTO instead, provided it carries `[MapFrom<TEntity>]`;
the response is then the list of DTOs, not `AutocompleteItem`. Nothing else to write: no query, no
endpoint, no DTO.

⚠️ **The route is gated on a derived read permission** (`{boundary}.{entity}.read`), which you never
declared and cannot turn off; the attribute has no knob for it. So it needs a host where somebody can
hold a permission: a host that declares `[AnonymousHost]` publishes this route and answers **403** to
everyone, and the generator says so with **PRAG1692**. Either give the host an authentication method,
or write the search endpoint by hand. A host that has none at all is refused earlier, by **PRAG1695**.

ℹ️ The boundary library itself needs nothing for this: not `Identity.AspNetCore`, not anything else.
Whether anyone can authenticate is the host's fact, and the host is where it is checked.

**`[Sse]`: a feed the server pushes.**

```csharp
[Endpoint(HttpVerb.Get, "api/ticks")]
[Sse(HeartbeatSeconds = 15)]
public partial class TickStream : StreamingEndpoint<Tick> { /* ... */ }
```

Turns the endpoint into `text/event-stream`. The heartbeat keeps proxies from closing an idle
connection. Use it when the client must learn about a change it did not ask for (a board that
refreshes itself) rather than polling.

**`[McpTool]`: the same operation, callable by a model.**

```csharp
[Endpoint(HttpVerb.Post, "api/booking-notes")]
[McpTool(Description = "Stores a short booking note (max 256-byte request).")]
public partial class CreateBookingNoteEndpoint : Endpoint<NoteDto> { /* ... */ }
```

Publishes the endpoint as an MCP tool. `Name` overrides the derived one; `Description` is what the
model reads to decide whether to call it, so write it for that reader.

Binding also has two attributes the list above does not mention: `[FromCookie]`, and
`[RequireAntiforgery]` on an endpoint that accepts a browser form post.

### The published contract

A host that references `Pragmatic.Endpoints.OpenApi` publishes it at `/openapi/v1.json` in Development without being asked (with Scalar at `/scalar` when `Scalar.AspNetCore` is referenced); `app.UseApiDocumentation()` publishes it in every environment. Do not write a startup step for it. It is an OpenAPI 3.1 document built at compile time from the same attributes the endpoints come from: operation ids, tags, parameters, request and response schemas, the error codes each operation can actually answer, and `summary` taken from `[ApiSummary]` or, failing that, from the XML doc comment above the class.

`components.schemas` holds only what an operation can reach: the manifest carries entities for other consumers, and publishing them all put internal shapes in the public contract.

**Authentication is the one thing the generator does not decide.** Which operations require it is known at compile time (`[AllowAnonymous]`); what the requirement looks like on the wire is chosen in `Program.cs`, so whoever configures it describes it:

```csharp
app.DescribeSecurityScheme(new OpenApiSecurityScheme
{
    Name = "apiKey",
    Type = "apiKey",
    ParameterName = "X-Api-Key",
    In = "header",
});
```

The three JWT entry points (`UseJwtAuthentication`, `UseKeycloakAuthentication`, `UseOidcAuthentication`) already do it, with `OpenApiSecurityScheme.Bearer()`, and so does `UseDevelopmentIdentity`. An application that installs an authentication of its own calls `DescribeSecurityScheme` once per way in; one that must compute the scheme registers an `IOpenApiSecuritySchemeContributor`.

⚠️ If nothing is registered and operations do require authentication, the document **says nothing** and the application logs it. That is deliberate: a guessed scheme published as a contract fails in the integrator's application, not in yours.

ASP.NET's own document (`AddOpenApi()` + `MapOpenApi()`) lists the generated endpoints too, beside the ones the application maps by hand: the generated registration adds the description provider, nothing to call. `EnableOpenApi = false` takes them out of both documents. ⚠️ That document cannot hold a schema nesting past 64 levels (an operation answering with an **entity** reaches it through the navigations), so such a response is published there as JSON without a schema, with a warning naming it. Answer with a DTO.

⚠️ `servers` is absent by design: a document without it means "relative to where you fetched it", which is the right answer behind a gateway or a CDN, where the application cannot see its own public address.
