---
title: "ASP.NET Core Integration"
description: "`Pragmatic.Temporal.AspNetCore` gives every HTTP request a `TemporalContext` (client + business timezone), detects the client's timezone from the request, and b"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/aspnetcore.md
sidebar:
  order: 3
---
`Pragmatic.Temporal.AspNetCore` gives every HTTP request a `TemporalContext` (client + business timezone), detects the client's timezone from the request, and binds temporal types from route/query values.

> Conversions are explicit by default (through `TemporalContext` methods in your handlers). Opt in per property with the [timezone conversion attributes](#timezone-conversion-attributes) (`[AsUtc]`, `[FromClientTimezone]`, `[ToClientTimezone]`, …) and the framework converts request/response values for you.

---

## Setup

**When you need it**: your API serves users in multiple timezones and handlers need to answer "what does *today* mean for this caller?" without parsing headers by hand.

**What you write**:

```csharp
builder.Services.AddPragmaticTemporalAspNetCore(
    configureTemporal: o =>
    {
        o.BusinessTimeZone = TimeZoneResolver.GetTimeZone("Europe/Rome");
    });

var app = builder.Build();

app.UseAuthentication();     // BEFORE UsePragmaticTemporal if you use claim-based detection
app.UsePragmaticTemporal();  // detects timezone, builds the per-request TemporalContext
```

**What you get**:

- core Temporal services (`IClock`, `ITemporalCalculator`, …) registered;
- a scoped `TemporalContext` resolvable from DI, built per request with the detected client timezone;
- temporal JSON converters and per-property timezone behaviors on **both** MVC's `JsonOptions` and Minimal API's `Http.Json.JsonOptions`;
- model binders for `LocalDate`, `LocalTime`, `LocalDateTime`, plus attribute-driven binding for `DateTimeOffset`/`DateTime` (inserted at the top, so they win over default binders).

Consume the context in any handler:

```csharp
public sealed class OrdersController(TemporalContext temporal, AppDbContext db) : ControllerBase
{
    [HttpGet("today")]
    public Task<List<Order>> Today()
        => db.Orders.WhereClientToday(o => o.CreatedAt, temporal).ToListAsync();
}
```

Middleware ordering: place `UsePragmaticTemporal()` **after** `UseAuthentication()` (the claims strategy reads `HttpContext.User`) and before anything that resolves `TemporalContext`.

---

## Timezone Detection

The middleware tries each strategy in ascending `Priority` order; the first non-null result wins, otherwise `TemporalOptions.DefaultTimeZone` is used.

| Priority | Strategy | Reads | Default key |
|----------|----------|-------|-------------|
| 50 | `QueryStringTimeZoneStrategy` | query string | `?tz=Europe/Rome` |
| 100 | `HeaderTimeZoneStrategy` | HTTP header | `X-Timezone: Europe/Rome` |
| 200 | `ClaimsTimeZoneStrategy` | authenticated user's claims | claim type `timezone` |
| 300 | `CookieTimeZoneStrategy` | cookie | cookie `tz` |

Query string has the highest priority by design: it makes per-request debugging trivial (`?tz=Asia/Tokyo`).

Values longer than 64 characters or not resolvable as IANA/Windows ids are **silently discarded** and the next strategy is tried. To fail loudly instead, enable:

```csharp
configureTemporal: o => o.ThrowOnInvalidTimeZone = true   // → TimeZoneNotFoundException (400-worthy)
```

This only applies when a value was *supplied but invalid* (distinguished via `IRawTimeZoneDetectionStrategy`); an absent timezone still falls back to the default.

### Customizing keys

```csharp
builder.Services.AddPragmaticTemporalAspNetCore(
    configureAspNetCore: o =>
    {
        o.TimezoneHeader = "X-User-Timezone";
        o.TimezoneClaim = "tz";
        o.TimezoneQueryParameter = "timezone";
        o.TimezoneCookie = "user_tz";
    });
```

These properties are propagated only to the **built-in** strategies; custom strategies you add to `o.DetectionStrategies` carry their own configuration.

### Custom strategies

Implement `ITimeZoneDetectionStrategy` (and `IRawTimeZoneDetectionStrategy` if you want `ThrowOnInvalidTimeZone` to see your raw values):

```csharp
public sealed class RouteTimeZoneStrategy : ITimeZoneDetectionStrategy, IRawTimeZoneDetectionStrategy
{
    public int Priority => 40; // before the built-ins

    public TimeZoneInfo? Detect(HttpContext context)
        => TimeZoneResolver.TryGetTimeZone(GetRawValue(context), out var tz) ? tz : null;

    public string? GetRawValue(HttpContext context)
        => context.GetRouteValue("tz") as string;
}
```

Add it to `o.DetectionStrategies` in `configureAspNetCore`.

---

## TemporalContext Outside a Request

`TemporalContext` is registered scoped and normally comes from `HttpContext.Items`. When resolved outside an HTTP request (background job, hosted service), `TemporalContextAccessor` falls back to a context built from `TemporalOptions` (including `DefaultTimeZone`/`BusinessTimeZone`, the DST policies, `FirstDayOfWeek`, and `DefaultCountryCode`), so background work behaves like request work.

---

## Model Binding

`LocalDate`, `LocalTime`, `LocalDateTime` (and their nullable forms) bind from route values and query strings:

```csharp
[HttpGet("bookings/{date}")]
public IActionResult ForDay(LocalDate date, LocalTime? after) { ... }
// GET /bookings/2026-06-01?after=14:30:00
```

Expected formats (mirrors each type's `TryParse`):

| Type | Format |
|------|--------|
| `LocalDate` | `yyyy-MM-dd` |
| `LocalTime` | `HH:mm:ss` (also `HH:mm`, `HH:mm:ss.fff`) |
| `LocalDateTime` | `yyyy-MM-ddTHH:mm:ss` |

An empty value binds `null` for nullable parameters; an unparsable value adds a `ModelState` error with the expected format in the message and fails the bind. Request **bodies** are handled by the JSON converters and the [conversion attributes](#timezone-conversion-attributes), not by these binders.

`DateTimeOffset`/`DateTime` parameters decorated with an input conversion attribute get the same treatment from route/query values:

```csharp
[HttpGet("orders")]
public IActionResult After([FromClientTimezone] DateTimeOffset since) { ... }
// GET /orders?since=2026-06-01T09:00:00       → 09:00 wall time in the CLIENT zone → UTC instant
// GET /orders?since=2026-06-01T09:00:00%2B02:00 → explicit offset wins, normalized to UTC
```

---

## Timezone Conversion Attributes

**When you need it**: your DTOs store instants in UTC (golden rule) but the API contract wants values expressed in the caller's (or the business's) timezone, and you don't want conversion boilerplate in every handler.

**What you write**: decorate the DTO property and register it (apps on the Pragmatic host get the registration generated from the attributes; standalone apps register once at startup).

```csharp
public sealed class OrderResponse
{
    [ToClientTimezone]                       // output: convert to the caller's zone
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateOrderRequest
{
    [FromClientTimezone]                     // input: wall time in the caller's zone → UTC
    public DateTime RequestedDelivery { get; set; }
}

// Standalone registration (startup, once):
TemporalJsonBehaviorRegistry.Register<OrderResponse>(
    nameof(OrderResponse.CreatedAt), TemporalJsonBehavior.ToClientTimezone);
TemporalJsonBehaviorRegistry.Register<CreateOrderRequest>(
    nameof(CreateOrderRequest.RequestedDelivery), TemporalJsonBehavior.FromClientTimezone);
```

**What you get**: serialization applies the conversion automatically using the per-request `TemporalContext`, with no reflection and no per-handler code.

| Attribute | Direction | Effect |
|-----------|-----------|--------|
| `[AsUtc]` | in + out | input without offset is read as UTC; output is always emitted in UTC |
| `[FromClientTimezone]` | in | wall time without offset is interpreted in the client zone (DST-safe) |
| `[FromBusinessTimezone]` | in | wall time without offset is interpreted in the business zone |
| `[ToClientTimezone]` | out | value is converted to the client zone |
| `[ToBusinessTimezone]` | out | value is converted to the business zone |
| `[KeepTimezone]` | out | explicit passthrough, no conversion ever |

Semantics worth knowing:

- **Explicit offsets always win on input**: `"2026-06-01T12:00:00+04:00"` is a known instant; `From*`/`AsUtc` merely normalize it to UTC. The interpretation attributes only matter for values *without* offset information.
- **Wall-time interpretation is DST-safe**: `From*` conversions go through the context's `NonExistentTimeHandling`/`AmbiguousTimeHandling` policies.
- **No ambient context → no conversion**: outside a request (or without the middleware) `To*`/`From*` behaviors pass values through unchanged rather than guessing. `AsUtc` needs no context and always applies.
- Registered names match the wire name case-insensitively, so CLR names work with camelCase policies. With more exotic naming policies (snake_case), register the serialized name.

---

## See Also

- [JSON Serialization](/modules/temporal/json/): wire formats and the Minimal API registration
- [EF Core Integration](/modules/temporal/efcore/): `WhereToday`/`WhereClientToday` consuming the request context
- [Concepts](/modules/temporal/concepts/): client vs business timezone, convert-at-boundaries rule
