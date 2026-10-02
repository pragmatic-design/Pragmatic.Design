# Pragmatic.Temporal

Type-safe date/time for .NET 10 with DST-aware arithmetic and explicit timezone handling.

## The Problem

`DateTime` and `DateTimeOffset` are ambiguous: `DateTime` conflates date, time, and zone behind an
easily-ignored `Kind`; `DateTimeOffset` keeps an offset but not a zone; calendar arithmetic surprises
(`Jan 31 + 1 month`); DST transitions cause silent bugs; and `DateTime.Now` makes tests flaky.

## The Solution: Type = Scope

The type itself defines the temporal scope, and the compiler enforces it. A `LocalDate` can't carry
time; a `ZonedDateTime` can't lose its zone.

```csharp
using Pragmatic.Temporal.Types;

var birthday    = new LocalDate(1990, 5, 15);                          // date only
var opening      = new LocalTime(9, 0);                                 // time only
var appointment = new LocalDateTime(2026, 3, 21, 14, 30);              // wall clock, no zone
var flight       = ZonedDateTime.FromUtc(DateTimeOffset.UtcNow, "Europe/Rome");  // zone-aware
```

`IClock` makes "now" injectable and tests deterministic; calendar arithmetic, business days, and DST
transitions are handled correctly.

## Packages

| Package | Role |
|---------|------|
| `Pragmatic.Temporal` | Core types, calendar arithmetic, business days, cron, `IClock` |
| `Pragmatic.Temporal.Json` | System.Text.Json converters for all temporal types |
| `Pragmatic.Temporal.EFCore` | EF Core value converters, type mapping, conventions |
| `Pragmatic.Temporal.AspNetCore` | Middleware, timezone detection, model binding |
| `Pragmatic.Temporal.Testing` | `TestClock`, `TestTemporalContext`, `TestHolidayProvider` |
| `Pragmatic.Temporal.Internationalization` | Culture-aware display formatting for `LocalDateTime`/`ZonedDateTime` via Pragmatic.Internationalization |
| `Pragmatic.Temporal.Analyzers` / `.CodeFixers` | Diagnostics PRAG0900-0904 — `DateTime.Now`/`Today` instead of `IClock`, a `DateTime` without a `Kind`, a `DateTimeOffset` compared with relational operators — with fixes |

## Installation

```bash
dotnet add package Pragmatic.Temporal
```

## Quick Start

```csharp
using Pragmatic.Temporal.Types;

var checkIn  = new LocalDate(2026, 6, 1);
var checkOut = checkIn.AddDays(3);                 // calendar-correct
int nights   = checkOut.DaysBetween(checkIn);       // 3

// Inject IClock instead of DateTime.Now → deterministic tests
public class BookingService(IClock clock)
{
    // IClock.Today is a DateOnly; LocalDate converts both ways, so name the type you compare in
    public bool IsPast(LocalDate date) => date < (LocalDate)clock.Today;
}
```

Full walkthrough: [Getting Started](docs/getting-started.md).

## Status

**Stable** within 1.0.0-alpha — the type set, calendar/business-day arithmetic, DST handling, and the
JSON/EF Core/ASP.NET integrations are settled. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Type = scope, `IClock`, the mental model, NodaTime comparison |
| [Getting Started](docs/getting-started.md) | First temporal types, arithmetic, injecting `IClock` |
| [Core Types](docs/core-types.md) | `LocalDate`/`LocalTime`/`LocalDateTime`/`ZonedDateTime`/`Duration`/… — full reference |
| [Business Days](docs/business-days.md) | Holiday providers, business-day arithmetic, cron |
| [DST Handling](docs/dst-handling.md) | Ambiguous/non-existent local times, safe conversions |
| [Testing](docs/testing.md) | `TestClock` and deterministic time |
| [JSON Integration](docs/json.md) | System.Text.Json converters, wire formats, Minimal API setup |
| [EF Core Integration](docs/efcore.md) | Value converters, column mappings, temporal query extensions |
| [ASP.NET Core Integration](docs/aspnetcore.md) | Middleware, timezone detection, model binding |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent date/time pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Temporal is **MIT-licensed**.
