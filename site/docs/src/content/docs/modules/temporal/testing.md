---
title: "Testing"
description: "Deterministic time in tests with `Pragmatic.Temporal.Testing`: a manipulable clock, pre-configured"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/testing.md
sidebar:
  order: 9
---
Deterministic time in tests with `Pragmatic.Temporal.Testing`: a manipulable clock, pre-configured
temporal contexts, and a mock holiday provider.

```bash
dotnet add package Pragmatic.Temporal.Testing
```

## Why: time is a dependency

**When you need it.** Any code that reads "now" — expiry checks, scheduling, audit timestamps,
"today" filters. If it calls `DateTime.Now` directly, the test result depends on when the test
runs: flaky around midnight, month boundaries, DST transitions, and impossible to reproduce.

**What you write.** Depend on `IClock` (from `Pragmatic.Temporal.Clock`) instead of the static
properties. Its real contract:

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }        // current UTC instant
    DateTimeOffset Now { get; }           // current local time (server timezone)
    DateOnly UtcToday { get; }            // current UTC date
    DateOnly Today { get; }               // current local date
    TimeOnly UtcTimeOfDay { get; }        // current UTC time of day
    TimeOnly TimeOfDay { get; }           // current local time of day
    TimeProvider GetTimeProvider();       // interop with .NET TimeProvider APIs
}
```

```csharp
public class SubscriptionService(IClock clock)
{
    public bool IsExpired(LocalDate expiresOn) => expiresOn < clock.Today;
}
```

**What you get.** In production, `AddPragmaticTemporal()` registers `SystemClock` (a `TimeProvider`
wrapper) automatically. In tests, you swap in a `TestClock` and control time explicitly — no
`Task.Delay`, no flakiness.

> Note: `Today`/`UtcToday` return `DateOnly`, which converts implicitly to `LocalDate`, so
> `clock.Today < someLocalDate` works directly.

## TestClock

**When you need it.** Every unit test exercising time-dependent logic.

**What you write.**

```csharp
using Pragmatic.Temporal.Testing;

// Default start: 2024-01-15T12:00:00Z
var clock = new TestClock();

// Or start at a specific instant
var clock2 = new TestClock(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
```

**What you get.** An `IClock` whose time only moves when you tell it to. Reads are thread-safe.

### Setting the time

```csharp
// Absolute instant
clock.Set(new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero));

// Fluent helpers (all UTC)
clock.SetDate(2026, 6, 15);                    // midnight UTC
clock.SetDateTime(2026, 6, 15, 10, 30);        // optional seconds argument
clock.SetDateTime(2026, 6, 15, 10, 30, 45);

// Wall-clock time in a specific timezone
var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
clock.SetDateTime(2026, 6, 15, 10, 30, rome);  // 10:30 Rome time
```

There is no `Freeze` method — a `TestClock` is frozen by construction: it never advances unless
you call an `Advance*` method or enable auto-advance.

### Advancing the time

```csharp
clock.Advance(TimeSpan.FromHours(2));
clock.Advance(Duration.FromMinutes(30));
clock.AdvanceDays(1);
clock.AdvanceHours(3);
clock.AdvanceMinutes(15);
clock.AdvanceSeconds(30);
```

### Static factories

```csharp
var atNow      = TestClock.AtNow();                 // current real UTC time, then frozen
var atNoon     = TestClock.AtNoon(2026, 6, 15);     // 12:00 UTC
var atMidnight = TestClock.AtMidnight(2026, 6, 15); // 00:00 UTC
```

### Auto-advance

**When you need it.** Code that reads the clock in a loop and would spin forever (or produce
identical timestamps) if time never moved — e.g. polling loops, unique-timestamp generation.

**What you write.**

```csharp
var clock = new TestClock().WithAutoAdvance();                          // +1 ms per read
var fast  = new TestClock().WithAutoAdvance(TimeSpan.FromSeconds(1));   // +1 s per read
```

**What you get.** Every read of `Now` or `UtcNow` moves the clock forward by `AutoAdvanceAmount`.

> ⚠️ Auto-advance triggers **only** on `Now`/`UtcNow`. Reading `Today`, `UtcToday`, `TimeOfDay`,
> or `UtcTimeOfDay` does not advance the clock — so mixing `UtcNow` and `Today` reads under
> auto-advance can observe different instants. Disable auto-advance if you need perfectly
> consistent component reads.

### DST helpers

**When you need it.** Testing behavior across daylight-saving transitions without hand-computing
transition dates.

**What you write.**

```csharp
clock.SetBeforeRomeSpringForward();        // last Sunday of March, 01:59:59 +01:00 (year: 2024)
clock.SetBeforeRomeFallBack(2026);         // last Sunday of October, 02:59:59 +02:00
clock.SetBeforeUsEasternSpringForward();   // second Sunday of March, 01:59:59 -05:00

clock.AdvanceSeconds(2);                   // now inside the transition
```

**What you get.** The clock positioned one second before the transition; a small `Advance` crosses
it deterministically.

## Swapping the clock in DI

**When you need it.** Integration-style tests that build a service provider.

**What you write.**

```csharp
using Pragmatic.Temporal.Extensions;

var clock = TestClock.AtNoon(2026, 6, 15);

services.AddPragmaticTemporal();
services.UseClock(clock);          // instance
// or: services.UseClock<TestClock>();  — by type
```

**What you get.** `IClock` resolves to your test clock everywhere, and the `TimeProvider`
registration is re-synced automatically, so modules that consume `TimeProvider` (Events,
Persistence timestamps) see the same fake time.

⚠️ **HybridCache reads that `TimeProvider` too**, and it judges an entry invalidated when it was
written *at or before* its tag's last invalidation. Under a frozen clock the two instants are the same:
a write that invalidates a tag — declaring a term, say, with `[InvalidatesCache]` on its event — makes
every entry written afterwards under that tag born invalidated, and the cache stops hitting with
nothing to say why. Move the clock forward before a read that has to hit after such a write:

```csharp
clock.AdvanceSeconds(1);   // the entry the next read writes is now newer than the invalidation
```

Forward only. Moved backwards, an entry written later can outlive an invalidation issued after it.
`TagInvalidationUnderAPinnedClockTests` in `Pragmatic.Caching.Tests` keeps this paragraph true.

## TestTemporalContext

**When you need it.** Testing code that takes a `TemporalContext` (client/business timezone
conversions, "today" ranges) without spinning up the ASP.NET middleware.

**What you write.**

```csharp
using Pragmatic.Temporal.Testing;

// Both zones UTC
var utc = TestTemporalContext.Utc();

// Client and business zone = Rome
var rome = TestTemporalContext.ForRome();

// Other city presets: ForNewYork(), ForLosAngeles(), ForLondon()
// Any IANA zone:
var tokyo = TestTemporalContext.ForTimezone("Asia/Tokyo");

// Different client vs business zones, starting at a chosen instant
var ctx = TestTemporalContext.WithTimezones(
    "America/New_York",            // client
    "Europe/Rome",                 // business
    new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
```

**What you get.** A fully wired `TemporalContext` backed by a `TestClock`, exposed via the
`TestClock` property, with shortcuts for time manipulation:

```csharp
ctx.SetTime(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
ctx.SetDateTime(2026, 6, 15, 10, 30);
ctx.Advance(TimeSpan.FromHours(1));
ctx.AdvanceDays(7);

var clientToday   = ctx.ClientToday;    // LocalDate in client zone
var businessNow   = ctx.BusinessNow;    // wall clock in business zone
```

All factories accept an optional `DateTimeOffset? now` to choose the starting instant.

## TestHolidayProvider

**When you need it.** Testing business-day logic (`ITemporalCalculator`) with controlled holidays
instead of a real provider.

**What you write.**

```csharp
using Pragmatic.Temporal.Testing;

var holidays = new TestHolidayProvider()
    .AddHoliday(2026, 12, 25, "IT")                       // year, month, day, COUNTRY CODE
    .AddHoliday(new LocalDate(2026, 12, 26), "IT")
    .AddHolidays("US", new LocalDate(2026, 7, 4));

// Presets
var italian = TestHolidayProvider.WithItalianHolidays(2026);  // 10 national holidays
var us      = TestHolidayProvider.WithUsHolidays(2026);       // 4 federal holidays

var calculator = new TemporalCalculator(holidays);
```

**What you get.** An `IHolidayProvider` where every added date is a public holiday for that
country. `Clear()` resets it.

> ⚠️ The last argument of `AddHoliday` is the **ISO country code** (`"IT"`, `"US"`), not the
> holiday name. Test holidays are always named `"Test Holiday"` — the provider models *when*
> holidays fall, not what they are called. Region codes are ignored.

## TimeProvider interop

**When you need it.** Testing code built on .NET's `TimeProvider` (timers, `PeriodicTimer`,
libraries that take a `TimeProvider`).

**What you write.**

```csharp
var clock = TestClock.AtNoon(2026, 6, 15);
TimeProvider provider = clock.GetTimeProvider();

var sut = new ThrottledPublisher(provider);
clock.AdvanceMinutes(5);
```

**What you get.** A `TimeProvider` whose `GetUtcNow()` reads the `TestClock` — one source of
truth for both abstractions. (Note: if `AutoAdvance` is enabled, each `GetUtcNow()` call advances
the clock too.)

## Analyzer support

The `Pragmatic.Temporal.Analyzers` package flags the patterns that break all of the above:

- **PRAG0900** (Warning): `DateTime.Now`/`UtcNow`, `DateTimeOffset.Now`/`UtcNow` in application
  code — inject `IClock` instead.
- **PRAG0904** (Info): `DateTime.Now`/`UtcNow` inside test methods (xUnit/NUnit/MSTest) — use
  `TestClock` so the test is deterministic.

## Common test patterns

### Expiry logic

```csharp
public class SubscriptionServiceTests
{
    [Fact]
    public void Subscription_ExpiresAfter30Days()
    {
        var clock = TestClock.AtMidnight(2026, 1, 1);
        var service = new SubscriptionService(clock);
        var subscription = service.Create();

        clock.AdvanceDays(30);

        Assert.True(subscription.IsExpired(clock));
    }

    [Fact]
    public void Subscription_ValidBefore30Days()
    {
        var clock = TestClock.AtMidnight(2026, 1, 1);
        var service = new SubscriptionService(clock);
        var subscription = service.Create();

        clock.AdvanceDays(29);

        Assert.False(subscription.IsExpired(clock));
    }
}
```

### Timezone conversion

```csharp
[Fact]
public void DisplaysTimeInUserTimezone()
{
    var clock = new TestClock().SetDateTime(2026, 6, 15, 14, 0);   // 2 PM UTC

    var romeTime = ZonedDateTime.FromUtc(clock.UtcNow, "Europe/Rome");
    var nyTime   = ZonedDateTime.FromUtc(clock.UtcNow, "America/New_York");

    Assert.Equal(16, romeTime.LocalTime.Hour);   // 4 PM CEST
    Assert.Equal(10, nyTime.LocalTime.Hour);     // 10 AM EDT
}
```

### DST transitions

```csharp
[Fact]
public void HandlesUsEasternSpringForward()
{
    var clock = new TestClock().SetBeforeUsEasternSpringForward(2024);

    clock.AdvanceSeconds(2);   // cross the 02:00 → 03:00 gap

    var eastern = ZonedDateTime.FromUtc(clock.UtcNow, "America/New_York");
    Assert.Equal(TimeSpan.FromHours(-4), eastern.Offset);   // EDT, not EST
}
```

### Business days with holidays

```csharp
[Fact]
public void AddBusinessDays_SkipsChristmas()
{
    var holidays = new TestHolidayProvider().AddHoliday(2026, 12, 25, "IT");
    var calculator = new TemporalCalculator(holidays);

    var start = new LocalDate(2026, 12, 24);   // Thursday

    // The countryCode overload consults the holiday provider;
    // AddBusinessDays(from, days) without it only skips weekends.
    var result = calculator.AddBusinessDays(start, 1, "IT");

    Assert.Equal(new LocalDate(2026, 12, 28), result);   // skips Fri 25 (holiday) + weekend
}
```

## Best practices

1. **Never read `DateTime.Now`** — inject `IClock`; the analyzers (PRAG0900/0904) will remind you.
2. **Pick explicit instants** — start tests from `TestClock.AtNoon(...)`/`AtMidnight(...)` rather
   than `AtNow()`, so failures are reproducible.
3. **Test the boundaries** — midnight, month/year ends, and DST transitions (use the
   `SetBefore*` helpers).
4. **Use the countryCode overloads** for business-day assertions — the plain overloads skip
   weekends only.
5. **One clock per test** — share the same `TestClock` between the system under test and the
   assertions; don't mix it with real time.
