---
title: "JSON Serialization"
description: "`Pragmatic.Temporal.Json` provides System.Text.Json converters for every temporal type. Values serialize as compact ISO 8601 strings, so your APIs speak the sam"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/json.md
sidebar:
  order: 8
---
`Pragmatic.Temporal.Json` provides System.Text.Json converters for every temporal type. Values serialize as compact ISO 8601 strings, so your APIs speak the same format as every other modern system.

---

## Registering the Converters

**When you need it**: any time a temporal type crosses a JSON boundary, such as API responses, request bodies, message payloads, cached documents.

**What you write**:

```csharp
using Pragmatic.Temporal.Json;

var options = new JsonSerializerOptions();
options.AddPragmaticTemporal();
```

Or create pre-configured options (camelCase properties, not indented):

```csharp
var options = JsonSerializerOptionsExtensions.CreateTemporalOptions();
```

**What you get**: converters for `LocalDate`, `LocalTime`, `LocalDateTime`, `ZonedDateTime`, `Duration`, `Period`, `DateRange`, `CronExpression` and their nullable variants, with no attributes needed on your DTOs.

```csharp
public sealed record BookingDto(
    LocalDate CheckIn,          // "2026-06-01"
    LocalTime ArrivalTime,      // "14:30:00"
    ZonedDateTime CreatedAt,    // "2026-05-20T10:30:00+02:00[Europe/Rome]"
    Duration Stay);             // "P3D"

var json = JsonSerializer.Serialize(dto, options);
var back = JsonSerializer.Deserialize<BookingDto>(json, options);
```

### ASP.NET Core: MVC and Minimal API

`AddPragmaticTemporalAspNetCore()` (from `Pragmatic.Temporal.AspNetCore`) registers the converters (and the per-property [timezone behaviors](/modules/temporal/aspnetcore/#timezone-conversion-attributes)) on **both** pipelines: MVC controllers (`Microsoft.AspNetCore.Mvc.JsonOptions`) and Minimal APIs (`Microsoft.AspNetCore.Http.Json.JsonOptions`). No extra registration needed.

Apps that don't use that entry point (workers, non-Pragmatic hosts) register explicitly:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.AddPragmaticTemporal();
    o.SerializerOptions.AddPragmaticTemporalBehaviors();   // only if you use the conversion attributes
});
```

`AddPragmaticTemporalBehaviors` wraps the resolver already on those options, so it belongs where you own
them. Inside a Pragmatic host you do not: the host builds one resolver from the shared
`PragmaticJsonOptions` seam and assigns it, which would discard the wrap. A package contributing a
per-property behaviour there registers it on the seam instead, and every registered modifier is applied:

```csharp
builder.Services.AddPragmaticJsonModifier(TemporalJsonModifier.Apply);
```

---

## Wire Formats

| Type | Serialized as | Example |
|------|--------------|---------|
| `LocalDate` | ISO date string | `"2026-06-01"` |
| `LocalTime` | ISO time string | `"14:30:00"` |
| `LocalDateTime` | ISO datetime, **no timezone** | `"2026-06-01T14:30:00"` |
| `ZonedDateTime` | offset + zone id string | `"2026-06-01T14:30:00+02:00[Europe/Rome]"` |
| `Duration` | ISO 8601 duration | `"PT1H30M"` |
| `Period` | ISO 8601 period | `"P1Y2M3D"` |
| `DateRange` | object with start/end | `{"start":"2026-01-01","end":"2026-03-31"}` |
| `CronExpression` | raw cron string | `"0 9 * * MON-FRI"` |

Parsing failures throw `JsonException` with the offending value in the message.

### LocalDateTime: timezone information is discarded

`LocalDateTime` is a wall-clock value. If the incoming JSON carries an offset or `Z` suffix (`"2026-06-01T14:30:00Z"`), the suffix is **stripped and ignored**: you get the wall-clock part only. If you need the instant, use `ZonedDateTime` or `DateTimeOffset` in your DTO instead.

### ZonedDateTime: dual format

Reading accepts **both** forms:

```jsonc
"2026-06-01T14:30:00+02:00[Europe/Rome]"                          // string form
{ "utc": "2026-06-01T12:30:00Z", "zone": "Europe/Rome" }          // object form
```

In the object form, `zone`/`timezone`/`timezoneid` are accepted as the zone property name; a `local` property is ignored (it is derived from `utc` + `zone`). Both `utc` and `zone` are required.

Writing defaults to the string form. To emit the object form (`{"utc": ..., "zone": ..., "local": ...}`), register the converter manually:

```csharp
options.Converters.Add(new ZonedDateTimeConverter { WriteAsString = false });
```

Note: `AddPragmaticTemporal()` always registers the string-writing default; there is no option flag on the extension itself.

### DateRange: three accepted input forms

Writing always produces the object form. Reading accepts:

1. Object: `{"start":"2026-01-01","end":"2026-03-31"}` (property names case-insensitive; both required)
2. String: `"2026-01-01/2026-03-31"`
3. `null` → `DateRange.Empty` for non-nullable `DateRange`; `null` for `DateRange?`

Mind the asymmetry in form 3: a round-trip of `DateRange.Empty` does not produce `null`, and a `null` in the payload silently becomes `Empty` on a non-nullable property.

### CronExpression: empty string behavior

`CronExpressionConverter` throws `JsonException` on empty/whitespace strings and on invalid expressions: malformed payloads never become silent `null`s. For optional cron values use a JSON `null` (the reference-type converter handles null tokens itself; there is no separate nullable converter).

---

## See Also

- [Core Types](/modules/temporal/core-types/): the exact `ToString()`/`Parse` formats each converter delegates to
- [ASP.NET Core Integration](/modules/temporal/aspnetcore/): model binding and per-request timezone context
