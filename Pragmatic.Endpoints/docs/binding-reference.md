# Binding Reference

Complete guide to request binding in Pragmatic.Endpoints.

## Route Parameters

Route parameters are extracted from the URL path. The SG matches properties to route segments **by name** (case-insensitive). If your route has `{id}` and your class has a property named `Id`, the binding happens automatically — `[FromRoute]` is optional:

### Basic Route Parameters

```csharp
[Endpoint(HttpVerb.Get, "/users/{id}")]
public partial class GetUserEndpoint : Endpoint<UserDto>
{
    public Guid Id { get; set; }  // auto-bound: matches {id} by name

    // [FromRoute] is optional — only needed when the property name
    // doesn't match the route parameter name
}
```

### Multiple Route Parameters

```csharp
[Endpoint(HttpVerb.Get, "/customers/{customerId}/orders/{orderId}")]
public partial class GetOrderEndpoint : Endpoint<OrderDto>
{
    [FromRoute]
    public Guid CustomerId { get; set; }

    [FromRoute]
    public Guid OrderId { get; set; }
}
```

### Route Constraints

Route constraints are specified in the route template:

```csharp
[Endpoint(HttpVerb.Get, "/products/{id:int}")]
public partial class GetProductEndpoint : Endpoint<ProductDto>
{
    [FromRoute]
    public int Id { get; set; }
}

[Endpoint(HttpVerb.Get, "/files/{**path}")]  // Catch-all
public partial class GetFileEndpoint : Endpoint<FileResponse>
{
    [FromRoute]
    public string Path { get; set; } = null!;
}
```

## Query Parameters

Query parameters come from the URL query string.

### Required Query Parameters

```csharp
[Endpoint(HttpVerb.Get, "/search")]
public partial class SearchEndpoint : Endpoint<SearchResults>
{
    // Required - uses 'required' keyword
    [FromQuery]
    public required string Query { get; set; }
}
// GET /search?query=hello
```

The `required` keyword makes the **binding** refuse a request without the value, with 400. A query value,
header or form field that carries Pragmatic.Validation's `[Required]` instead is bound as optional and
refused by validation, with 422. Either way a request without it does not get through, so it is published
as required: a query value or header in both documents, a form field in the runtime one (the manifest
lists no form fields).

### Optional Query Parameters

```csharp
[Endpoint(HttpVerb.Get, "/products")]
public partial class ListProductsEndpoint : Endpoint<ProductList>
{
    // Optional - nullable type
    [FromQuery]
    public string? Category { get; set; }

    // Optional - value type with nullable
    [FromQuery]
    public int? Page { get; set; }

    // Optional - with default value
    [FromQuery]
    public int PageSize { get; set; } = 20;
}
// GET /products?category=electronics&page=2
```

### Custom Query Parameter Names

```csharp
[Endpoint(HttpVerb.Get, "/search")]
public partial class SearchEndpoint : Endpoint<SearchResults>
{
    [FromQuery(Name = "q")]
    public required string Query { get; set; }

    [FromQuery(Name = "max_results")]
    public int MaxResults { get; set; } = 10;
}
// GET /search?q=hello&max_results=50
```

### Array Query Parameters

```csharp
[Endpoint(HttpVerb.Get, "/products")]
public partial class FilterProductsEndpoint : Endpoint<ProductList>
{
    [FromQuery]
    public List<string>? Tags { get; set; }

    [FromQuery]
    public int[]? Ids { get; set; }
}
// GET /products?tags=electronics&tags=sale&ids=1&ids=2&ids=3
```

## Header Parameters

Header parameters come from HTTP request headers.

### Required Headers

```csharp
[Endpoint(HttpVerb.Post, "/api/data")]
public partial class DataEndpoint : Endpoint<DataResponse>
{
    // Required - uses 'required' keyword
    [FromHeader(Name = "X-Api-Key")]
    public required string ApiKey { get; set; }
}
```

### Optional Headers

```csharp
[Endpoint(HttpVerb.Post, "/api/data")]
public partial class DataEndpoint : Endpoint<DataResponse>
{
    // Optional - nullable type
    [FromHeader(Name = "X-Correlation-Id")]
    public string? CorrelationId { get; set; }

    // Optional - with default
    [FromHeader(Name = "X-Request-Timeout")]
    public int RequestTimeout { get; set; } = 30;
}
```

### Standard Headers

```csharp
[Endpoint(HttpVerb.Put, "/resources/{id}")]
public partial class UpdateResourceEndpoint : Endpoint<ResourceDto>
{
    [FromRoute]
    public Guid Id { get; set; }

    // ETag for optimistic concurrency
    [FromHeader(Name = "If-Match")]
    public string? IfMatch { get; set; }

    // Accept-Language for localization
    [FromHeader(Name = "Accept-Language")]
    public string? AcceptLanguage { get; set; }
}
```

## Cookie Parameters

`[FromCookie]` binds from `HttpContext.Request.Cookies` (minimal APIs have no native cookie
binding — the generated handler reads it in the body). Required missing → **400** (a cookie
is request input, not auth). Supported typed values: `string`, `Guid`, `int`, `long`, `bool`,
`DateTimeOffset` (TryParse; malformed required values → 400). Values are used as-is — no URL
decoding, consistent with `[FromHeader]`.

```csharp
[Endpoint(HttpVerb.Get, "/api/preferences")]
public partial class GetPreferencesEndpoint : Endpoint<PreferencesDto>
{
    [FromCookie("session-hint", IsRequired = false)]
    public string? SessionHint { get; set; }
}
```

Cookie parameters are excluded from the body DTO and documented as `in: cookie` in the
manifest and OpenAPI.

## The Caller: `[FromClaim]` and `[FromCurrentUser]`

Two attributes take a value from who is calling rather than from what they sent. They are not two
spellings of one thing.

| | `[FromClaim("sub")]` | `[FromCurrentUser]` / `[FromCurrentUser(nameof(Employee.Id))]` |
|---|---|---|
| Where | any endpoint | a `[Query]` |
| Who writes it | the generated **endpoint**, from `HttpContext.User` | the generated **invoker**, after validation and the permission check |
| What | one raw claim, parsed to the property's type | `ICurrentUser.Id`, or a member of the `[PragmaticUser]` entity through its generated resolver |
| Reached in process | ❌ — an in-process caller sets the property, and can set anyone's | ✅ — the boundary member runs the same invoker |
| The property | public, settable | `{ get; private set; }` — `PRAG0730` otherwise |
| Request parameter / OpenAPI | no | **never** — not query string, not route, not body, not the boundary interface |

`[FromClaim]` is binding: the claim is one more source the handler reads, like a header. Its value
stays a public property, so the HTTP door is the only one that fills it.

`[FromCurrentUser]` is not binding at all. The endpoint does not see the property — a route placeholder
that names it matches nothing and is reported by `PRAG0504` — and the value is written by the
operation's invoker (a query's, an action's or a mutation's), the one pipeline every caller goes through. A caller who is not authenticated gets 401; an
authenticated one with no user entity gets 404. Use it for "my …" reads; the form and its diagnostics
(`PRAG0730`, `PRAG0731`) are in
[Filtering by the caller](../../Pragmatic.Persistence/docs/09-query-system.md#filtering-by-the-caller-fromcurrentuser).

## Default Values

Property initializers on optional query/header parameters are the effective defaults —
absent parameters never overwrite the constructed instance:

```csharp
[FromQuery] public int PageSize { get; set; } = 20;      // documented as default: 20
[FromQuery] public string Currency { get; set; } = "eur";
```

Literal initializers (numbers, strings, booleans, enum members) also flow into the manifest
(`defaultValue`) and the OpenAPI parameter schema. Non-constant initializers
(`Guid.NewGuid()`, computed values) still work at runtime on a `set` property, but are not documented.

On an `init` property the same holds, spelled differently: an `init` property can only be set in the
object initializer, so the generated endpoint writes `PageSize = pageSize ?? 20` there, repeating the
declared default. That requires the default to be a constant (a literal, a negative literal, an enum
member, `null` or `default`), or no initializer at all. A non-constant initializer on an optional `init`
property is **PRAG0536**: give it a constant default, or a `set` accessor.

```csharp
[FromHeader(Name = "X-Source")] public string? Source { get; init; }  // absent → null
[FromQuery] public int PageSize { get; init; } = 20;                  // absent → 20
[FromQuery] public string Tag { get; init; } = NewTag();              // PRAG0536
```

Claims and cookies follow the same rule. The generated endpoint reads them — and refuses a missing or
malformed required one — before it builds the operation, so a `[FromClaim]` or `[FromCookie]`
property can be `required` or `init`: it is set in the object initializer like any other, and an
optional `init` one keeps its declared default when the value is absent.

## Body Parameters

Properties without binding attributes become the request body.

### Simple Body

```csharp
[Endpoint(HttpVerb.Post, "/users")]
public partial class CreateUserEndpoint : Endpoint<UserDto>
{
    // These properties become the request body DTO
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
}

// Generated body DTO:
// public sealed record CreateUserEndpointBody
// {
//     public required string Name { get; init; }
//     public required string Email { get; init; }
//     public string? Phone { get; init; }
// }
```

### Complex Body Types

```csharp
[Endpoint(HttpVerb.Post, "/orders")]
public partial class CreateOrderEndpoint : Endpoint<OrderDto>
{
    public required Guid CustomerId { get; set; }
    public required List<OrderItem> Items { get; set; }
    public Address? ShippingAddress { get; set; }
    public PaymentInfo? Payment { get; set; }
}

public record OrderItem(Guid ProductId, int Quantity);
public record Address(string Street, string City, string Country);
public record PaymentInfo(string CardNumber, string Expiry);
```

### Body with Documentation

```csharp
[Endpoint(HttpVerb.Post, "/articles")]
public partial class CreateArticleEndpoint : Endpoint<ArticleDto>
{
    /// <summary>
    /// The article title. Must be unique.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The article content in Markdown format.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Tags for categorization. Maximum 5 tags allowed.
    /// </summary>
    public List<string>? Tags { get; set; }
}
```

### Custom Body Property Names

A query parameter can be renamed with `[FromQuery(Name = ...)]`. A body property is renamed with the
standard `[JsonPropertyName]`, and the generated request body carries it:

```csharp
[Endpoint(HttpVerb.Post, "/members/import")]
public partial class ImportMembersAction : DomainAction<ImportMembersResult>
{
    public required Guid WorkspaceId { get; init; }

    [JsonPropertyName("people")]
    public required IReadOnlyList<MemberRow> Rows { get; init; }
}
// POST { "workspaceId": "...", "people": [ ... ] }
```

The case this exists for is an **import**: the file arrives from a system whose vocabulary you do not
choose, and it has to land unedited. Renaming the property to match would carry the exporter's word
through the loop, the result and the log line for the sake of one boundary; the attribute keeps the
disagreement where it belongs, at the edge.

The published OpenAPI schema uses the wire name too — a document that advertised the property name
would describe a request the endpoint rejects, and the client generated from it would be broken
against the very application it was generated from.

> ⚠️ **A body with exactly one property is not wrapped.** The endpoint binds that property's type
> straight from the request, so `POST` takes a bare JSON array (or scalar), there is no object with a
> field name in it, and `[JsonPropertyName]` has nothing to rename — silently. Add a second body
> property, or accept the bare shape.

### What the published contract says about a type

The compile-time OpenAPI document (`/openapi/v1.json`) describes every property from the CLR type
declared for it. What it can say depends on what the manifest carries:

| Declared as | Published as |
|---|---|
| `string`, `int`, `Guid`, `DateTime`, … | `type` + `format` |
| `byte[]`, `Stream` | `"type": "string"` with `format` `byte` / `binary` — both are strings on the wire |
| `List<T>`, `T[]`, `IReadOnlyList<T>`, … | `"type": "array"` with an `items` schema for `T` |
| `IReadOnlyDictionary<K,V>`, `Dictionary<K,V>` | `"type": "object"` with `additionalProperties` for `V` |
| a type the manifest describes | `$ref` to its schema |
| an enum the manifest does not describe | `"type": "string"` — what `JsonStringEnumConverter` writes |
| anything else | an **empty schema**, which means "any value" |

The empty schema is deliberate, and the alternative is worse than it looks: publishing a type the
document cannot describe as `"type": "string"` is a statement a client generator believes, so it emits
a client that sends a string where an array is required — against the very application the document
came from. An unconstrained schema says the document does not know, which is the true statement.

**Nullability** is a type union, not a keyword: the document is OpenAPI **3.1**, where `nullable`
belongs to 3.0 and is a member every reader drops. An optional `string` is `"type": ["string","null"]`,
and an optional reference is an `anyOf` of the `$ref` and `{"type": "null"}` — a `$ref` cannot be
qualified by a sibling keyword.

**Validation attributes** on a body property are published under their JSON Schema keywords, so a
caller reads the bound instead of discovering it by being refused: `[MinLength]`/`[MaxLength]`/
`[Length]`/`[StringLength]` as `minLength`/`maxLength` on a string and as `minItems`/`maxItems` on a
collection, `[MinCount]`/`[MaxCount]`/`[Count]` as `minItems`/`maxItems`, `[Range]` as
`minimum`/`maximum`, `[GreaterThan]`/`[LessThan]`/`[Positive]`/`[Negative]` as the exclusive bounds,
`[GreaterThanOrEqual]`/`[LessThanOrEqual]` as the inclusive ones, `[Regex]` as `pattern`, and
`[Email]`/`[Url]`/`[Guid]` on a string as `format`. Rules JSON Schema cannot spell — a phone number,
a credit card, a date in the future, a comparison with another property — stay with the server.

## Mixed Bindings

Combine multiple binding sources in one endpoint:

```csharp
[Endpoint(HttpVerb.Put, "/customers/{customerId}/orders/{orderId}")]
[ApiSummary("Update Order")]
public partial class UpdateOrderEndpoint : Endpoint<OrderDto>
{
    // Route parameters
    [FromRoute]
    public Guid CustomerId { get; set; }

    [FromRoute]
    public Guid OrderId { get; set; }

    // Required header
    [FromHeader(Name = "X-Idempotency-Key")]
    public required string IdempotencyKey { get; set; }

    // Optional header
    [FromHeader(Name = "X-Correlation-Id")]
    public string? CorrelationId { get; set; }

    // Query parameter
    [FromQuery]
    public bool? Notify { get; set; }

    // Body properties
    public required List<OrderItem> Items { get; set; }
    public string? Notes { get; set; }
}

// Request:
// PUT /customers/123/orders/456?notify=true
// Headers:
//   X-Idempotency-Key: abc123
//   X-Correlation-Id: corr-789
// Body:
// { "items": [...], "notes": "Rush order" }
```

## Parameter Ordering

The generator automatically orders parameters correctly:

1. **Route parameters** - First (positional in URL)
2. **Required headers** - Next (mandatory)
3. **Required body** - If using body DTO
4. **Required query** - If any required
5. **Dependencies** - DI injected services
6. **HttpContext** - If needed
7. **CancellationToken** - Always last required
8. **Optional headers** - With `= default`
9. **Optional query** - With `= default`

This ensures C# parameter ordering rules (required before optional) are satisfied.

## Validation Integration

Combine with Pragmatic.Validation:

```csharp
[Endpoint(HttpVerb.Post, "/users")]
public partial class CreateUserEndpoint : Endpoint<UserDto>
{
    [Required]
    [Email]
    public required string Email { get; set; }

    [Required]
    [MinLength(2)]
    [MaxLength(50)]
    public required string Name { get; set; }

    [Phone]
    public string? Phone { get; set; }
}
```

Validation runs automatically before `HandleAsync` via the DomainAction pipeline.

## File Uploads

```csharp
[Endpoint(HttpVerb.Post, "/files")]
public partial class UploadFileEndpoint : Endpoint<FileInfo>
{
    [FromForm]
    public required IFormFile File { get; set; }

    [FromForm]
    public string? Description { get; set; }
}
```

A form field is required unless its type is nullable: a form without `Description` reaches the
endpoint, one without `File` answers 400. Nullability is read from the declaration, so `string?` and
`IFormFile?` are optional like `int?`. A field is read under its property name unless
`[FromForm(Name = "…")]` gives it another, and the runtime document publishes the same key.

⚠️ **Once one property carries a file or a `[FromForm]`, the request is `multipart/form-data` and
*every* value comes from the form** — including the properties nobody marked. There is no JSON body
left for them to arrive in, so `[FromForm]` on the rest is explicit rather than load-bearing, and the
wire key is the same either way (the property name).

What a form field cannot carry is a **nested object** — it is a string on the wire — and that is
`PRAG0552`, which names the property. The three ways out are the same as for a `GET` (`PRAG0532`): a
scalar, a single value holding JSON the operation parses itself, or an operation without the file.

## Best Practices

1. **Use `required` for mandatory parameters** - Clear intent and compile-time safety
2. **Use nullable types for optional** - `string?`, `int?` instead of defaults
3. **Name headers explicitly** - `[FromHeader(Name = "X-...")]`
4. **Document body properties** - XML comments become OpenAPI descriptions
5. **Group related endpoints** - Use `[EndpointGroup]` for common prefixes
6. **Keep endpoints focused** - One responsibility per endpoint class
