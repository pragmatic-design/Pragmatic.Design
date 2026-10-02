---
title: "Core Types"
description: "Full reference for the Pragmatic.Temporal value types. Every signature on this page is verified"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Temporal/docs/core-types.md
sidebar:
  order: 5
---
Full reference for the Pragmatic.Temporal value types. Every signature on this page is verified
against the current implementation.

**The rule of thumb — type = scope:**

| Type | Has Date | Has Time | Has Zone | Meaning |
|------|----------|----------|----------|---------|
| `LocalDate` | ✅ | ❌ | ❌ | A calendar date ("June 15th") |
| `LocalTime` | ❌ | ✅ | ❌ | A wall-clock time ("9:00 AM") |
| `LocalDateTime` | ✅ | ✅ | ❌ | A wall-clock date+time, **not** an instant |
| `ZonedDateTime` | ✅ | ✅ | ✅ | An exact instant + the zone it belongs to |
| `Duration` | — | — | — | Exact elapsed *physical* time (24h is always 24h) |
| `Period` | — | — | — | Calendar units (years/months/days — "1 month" varies) |
| `DateRange` | ✅ | ❌ | ❌ | An inclusive range of dates `[Start, End]` |
| `CronExpression` | — | — | — | A recurrence schedule |

> There is no `LocalDate.Today` or static "now" on any type. "Now" always comes from an injected
> `IClock` (`clock.Today`, `clock.UtcNow`) so your code stays testable. See [Testing](/modules/temporal/testing/).

---

## LocalDate

**When you need it**: birthdays, invoice dates, check-in dates, deadlines — anything where "the
date" is meaningful on its own and must not shift with timezones. A `readonly struct` wrapping
`DateOnly`.

### Creation

```csharp
var date    = new LocalDate(2026, 6, 15);          // from components
var fromDo  = new LocalDate(someDateOnly);          // from DateOnly
var fromDt  = LocalDate.FromDateTime(dateTime);     // date part of a DateTime
var min     = LocalDate.MinValue;                   // 0001-01-01
var max     = LocalDate.MaxValue;                   // 9999-12-31

// "today" comes from the clock, never from a static property:
LocalDate today = clock.Today;                      // IClock.Today is DateOnly → implicit conversion
```

> ⚠️ `LocalDate.FromDateTimeOffset(dto)` takes the **wall-clock date of the offset value** and
> ignores the offset itself. For "what date is this instant in zone X?" use
> `dto.ToLocalDate(zone)` from `Pragmatic.Temporal.Extensions` instead — it converts the UTC
> instant into the zone first.

### Properties

```csharp
date.Year         // 2026
date.Month        // 6  (1-12)
date.Day          // 15 (1-31)
date.DayOfWeek    // DayOfWeek.Monday
date.DayOfYear    // 166 (1-366)
date.Quarter      // 2  (1-4)
date.IsWeekend    // Saturday or Sunday
date.IsWeekday    // Monday-Friday
date.IsLeapYear   // is the year a leap year
```

### Arithmetic

```csharp
date.AddDays(7);
date.AddMonths(1);       // Jan 31 + 1 month → Feb 28/29 (clamped, no exception)
date.AddYears(1);
date.DaysBetween(other); // signed: this − other (days). a.DaysBetween(b) > 0 when a is later.
```

For years/months/days in one shot use a [`Period`](#period): `date.Add(Period.FromMonths(3))`.

### Navigation

```csharp
date.StartOfMonth();   date.EndOfMonth();
date.StartOfYear();    date.EndOfYear();
date.StartOfQuarter(); date.EndOfQuarter();
date.StartOfWeek();    // default week start: Monday
date.EndOfWeek();      // both accept a DayOfWeek firstDayOfWeek argument
```

Richer navigation (next Friday, third Tuesday of the month, ISO weeks) lives in
[`LocalDateExtensions`](#localdate-extensions).

### Combining and converting

```csharp
date.At(new LocalTime(14, 30));  // → LocalDateTime
date.AtMidnight();               // → LocalDateTime at 00:00
date.AtNoon();                   // → LocalDateTime at 12:00
date.ToDateOnly();               // → DateOnly
date.ToDateTime();               // → DateTime at midnight (Kind unspecified)
date.ToDateTime(time);           // → DateTime at the given LocalTime
```

### Operators, parsing, formatting

- Full comparison set: `== != < <= > >=`.
- **Implicit conversions both ways** with `DateOnly` — you can pass a `LocalDate` wherever a
  `DateOnly` is expected and vice versa (this is why `clock.Today` assigns directly).
- `ToString()` → ISO `yyyy-MM-dd` (invariant). `ToString(format)` for custom formats.
- `Parse` / `TryParse`: tries exact `yyyy-MM-dd` first, then falls back to invariant
  `DateOnly.TryParse`. Prefer feeding it ISO strings only — the fallback accepts other
  invariant-culture formats (e.g. `06/15/2026` parses as month/day), which can surprise you.

---

## LocalTime

**When you need it**: store opening hours, a daily report time, an alarm — a time of day with no
date and no zone. Wraps `TimeOnly`.

### Creation

```csharp
var t1 = new LocalTime(9, 0);            // 09:00:00
var t2 = new LocalTime(9, 0, 30);        // 09:00:30
var t3 = new LocalTime(9, 0, 30, 500);   // with milliseconds
var t4 = new LocalTime(someTimeOnly);

LocalTime.Midnight;   // 00:00:00
LocalTime.Noon;       // 12:00:00
LocalTime.MinValue;   // 00:00:00
LocalTime.MaxValue;   // 23:59:59.9999999

LocalTime.FromDateTime(dt);        LocalTime.FromDateTimeOffset(dto);
LocalTime.FromTicks(ticks);        LocalTime.FromTimeSpan(ts);
```

### Properties and checks

```csharp
time.Hour; time.Minute; time.Second; time.Millisecond; time.Ticks;

time.IsMorning;     // Hour < 12
time.IsAfternoon;   // 12 ≤ Hour < 18
time.IsEvening;     // Hour ≥ 18
time.IsBetween(start, end);
```

> ⚠️ `IsBetween` delegates to `TimeOnly.IsBetween`: the start is **inclusive**, the end is
> **exclusive**, and ranges that cross midnight (e.g. 22:00 → 02:00) are supported.

### Arithmetic

```csharp
time.Add(TimeSpan.FromMinutes(90));   // wraps around midnight: 23:30 + 1h → 00:30
time.Add(duration);                   // same, takes a Duration
time.AddHours(1.5);                   // double — fractional hours OK
time.AddMinutes(30);

time.DurationUntil(other);            // TimeSpan, always forward: if other is "earlier",
                                      // it assumes the next day (result is always 0-24h)
```

### Combining, operators, parsing

```csharp
time.On(date);          // → LocalDateTime (mirror of date.At(time))
time.ToTimeOnly();      time.ToTimeSpan();
```

- Comparisons `== != < <= > >=`; `+` / `-` with `TimeSpan`; implicit conversions both ways with
  `TimeOnly`.
- `ToString()` → `HH:mm:ss`. `Parse`/`TryParse` accept `HH:mm:ss`, `HH:mm:ss.fff`, `HH:mm`,
  then an invariant fallback.

---

## LocalDateTime

**When you need it**: a wall-clock date+time where the zone is context, not data — "the meeting
is at 2026-03-21 14:30" (in whatever office you're in). Internally a `DateTime` with
`Kind.Unspecified`.

> ⚠️ **Never use `LocalDateTime` for instants** (things that happened at an exact moment
> worldwide). Use `ZonedDateTime` or `DateTimeOffset` for those.

### Creation

```csharp
var dt  = new LocalDateTime(2026, 3, 21, 14, 30);       // seconds default to 0
var dt2 = new LocalDateTime(date, time);                 // from LocalDate + LocalTime
var dt3 = new LocalDateTime(someDateTime);               // Kind is ignored → treated as wall clock
var dt4 = date.At(time);                                 // fluent equivalent

LocalDateTime.MinValue; LocalDateTime.MaxValue;
```

(There is no `FromDateTime` static — use the constructor.)

### Properties and navigation

```csharp
dt.Date;   // LocalDate      dt.Time;   // LocalTime
dt.Year; dt.Month; dt.Day; dt.Hour; dt.Minute; dt.Second;
dt.DayOfWeek; dt.DayOfYear;

dt.StartOfDay();   // 00:00
dt.EndOfDay();     // 23:59:59.9999999
dt.StartOfHour();

var (date, time) = dt;   // Deconstruct
```

### Arithmetic

```csharp
dt.AddDays(1); dt.AddMonths(1); dt.AddYears(1);
dt.AddHours(2); dt.AddMinutes(15); dt.AddSeconds(30);    // all int
dt.Add(duration);  dt.Add(timeSpan);
dt.DurationUntil(other);                                  // → Duration

dt + duration;  dt - duration;      // operators
later - earlier;                     // LocalDateTime − LocalDateTime → Duration
```

These are plain wall-clock operations — no DST is involved because there is no zone yet.

### Attaching a zone

```csharp
// The safe way — explicit DST policies (defaults shown):
ZonedDateTime zoned = dt.InZone("Europe/Rome");
ZonedDateTime z2    = dt.InZone(tzInfo,
    nonExistentPolicy: NonExistentTimePolicy.ShiftForward,
    ambiguousPolicy:   AmbiguousTimePolicy.UseStandardTime);
```

See [DST Handling](/modules/temporal/dst-handling/) for what the policies do.

`dt.ToDateTimeOffset(zone)` is DST-safe and consistent with `InZone`: gaps shift forward,
ambiguous times resolve to standard time by default. Pass explicit policies when you need
different behavior:

```csharp
dt.ToDateTimeOffset(zone);                                     // default policies
dt.ToDateTimeOffset(zone,
    NonExistentTimePolicy.ThrowException,
    AmbiguousTimePolicy.UseDaylightTime);                      // explicit
```

### Parsing and formatting

- `ToString()` → `yyyy-MM-ddTHH:mm:ss`.
- `Parse`/`TryParse` accept `yyyy-MM-ddTHH:mm:ss`, with `.fff`, with a space separator, and
  `yyyy-MM-ddTHH:mm`, plus an invariant fallback.

> ⚠️ If the input string carries a timezone suffix (`Z`, `+02:00`, `-05:00`), the parser
> **strips and discards it** — a `LocalDateTime` has no zone to store it in. If the offset
> matters, parse a `ZonedDateTime` or `DateTimeOffset` instead.

---

## ZonedDateTime

**When you need it**: an exact instant where the zone is part of the data — flight departures,
meeting starts across offices, anything you'll show in "local time" later. Internally stores the
UTC instant + the `TimeZoneInfo`.

Constructors are internal — you always go through a factory, which is what makes the type
DST-safe:

### Creation

```csharp
// From a UTC instant (safest — UTC has no DST):
var z1 = ZonedDateTime.FromUtc(dtoUtc, "Europe/Rome");
var z2 = ZonedDateTime.FromUtc(dtoUtc, tzInfo);

// From a wall-clock DateTime interpreted in a zone (policies handle DST edges):
var z3 = ZonedDateTime.FromLocal(dateTime, tzInfo);                    // default policies
var z4 = ZonedDateTime.FromLocal(dateTime, tzInfo,
             NonExistentTimePolicy.ThrowException,
             AmbiguousTimePolicy.UseDaylightTime);
var z5 = ZonedDateTime.FromLocalStrict(dateTime, tzInfo);              // throws on any DST edge

// "Now" requires a clock (injectable, testable — there is no parameterless Now):
var z6 = ZonedDateTime.Now("Europe/Rome", clock);
var z7 = ZonedDateTime.Now(tzInfo, clock);
```

`FromLocal` takes a `DateTime`; if you have a `LocalDateTime`, call `.InZone(...)` on it — same
thing, fluent form.

### Properties

```csharp
z.UtcDateTime      // DateTimeOffset — the UTC instant
z.Zone             // TimeZoneInfo
z.ZoneId           // string — the IANA id ("Europe/Rome")
z.Offset           // TimeSpan — UTC offset at this instant (+01:00 or +02:00 depending on DST)
z.IsDaylightSavingTime

z.LocalDateTime    // DateTime  — wall clock in the zone
z.Date             // LocalDate   z.Time  // LocalTime
z.LocalDate        // DateOnly    z.LocalTime // TimeOnly
z.Year; z.Month; z.Day; z.Hour; z.Minute; z.Second; z.DayOfWeek;   // all in the zone
```

> Performance note: every local-side getter re-runs the UTC→zone conversion. In a loop that
> reads several components, read `z.LocalDateTime` once into a variable.

### The two kinds of arithmetic (the whole point of this type)

```csharp
// WALL-CLOCK (calendar) — "same local time tomorrow". May be 23h or 25h of real time
// across a DST transition; the result is re-resolved with the default DST policies:
z.AddDays(1); z.AddMonths(1); z.AddYears(1); z.AddHours(2); z.AddMinutes(30);

// PHYSICAL — exact elapsed time on the UTC instant. Local time-of-day may shift
// across a DST transition:
z.Add(Duration.FromHours(24));
z.Add(timeSpan);
z + duration;  z - duration;

z.DurationUntil(other);     // physical Duration between instants
later - earlier;            // same, as an operator
```

Pick consciously: a hotel checkout "tomorrow at 11:00" is `AddDays(1)`; a parking meter expiring
"in 24 hours" is `Add(Duration.FromHours(24))`.

### Equality — instant vs exact

```csharp
var rome  = ZonedDateTime.FromUtc(instant, "Europe/Rome");
var tokyo = rome.InZone("Asia/Tokyo");

rome == tokyo;              // TRUE  — same instant (== and Equals compare the instant only)
rome.EqualsExact(tokyo);    // FALSE — also compares Zone.Id
```

Comparisons (`< <= > >=`) and `GetHashCode` also use the instant only.

### Conversion

```csharp
z.InZone("Asia/Tokyo");   // same instant, different zone
z.ToUtc();                // DateTimeOffset (UTC)
z.ToDateTimeOffset();     // DateTimeOffset with the zone's local offset
```

### Formatting and parsing

```csharp
z.ToString();     // "2026-01-15T10:30:00+01:00[Europe/Rome]"
z.ToString("O");  // custom formats apply to the offset form (no bracket)

ZonedDateTime.Parse("2026-01-15T10:30:00+01:00[Europe/Rome]");
ZonedDateTime.Parse("2026-01-15T10:30:00Z");    // no bracket → zone is UTC
ZonedDateTime.TryParse(s, out var result);
```

Parsing rules worth knowing:

- With a `[Zone]` bracket, the zone id must resolve and be ≤ 64 chars.
- If the string has **both** an explicit offset and a `[Zone]`, the offset must be valid for
  that zone at that wall time — inconsistent values (wrong offset, or a time inside a DST gap)
  are rejected rather than silently reinterpreted.
- Without a bracket the value is parsed as a `DateTimeOffset` and normalized to the **UTC**
  zone — the original offset is not kept as a zone.

---

## Duration

**When you need it**: elapsed physical time — timeouts, SLAs, "how long did it take". `24 hours`
is always exactly 24 hours; DST doesn't exist for `Duration`. Wraps `TimeSpan`.

Contrast with [`Period`](#period) (calendar units) and with `ZonedDateTime.AddDays` (wall-clock).

### Creation

```csharp
Duration.FromDays(1.5); Duration.FromHours(2); Duration.FromMinutes(30);
Duration.FromSeconds(45); Duration.FromMilliseconds(250);
Duration.FromTicks(ticks); Duration.FromTimeSpan(ts);

Duration.Zero; Duration.OneDay; Duration.OneHour;
Duration.OneMinute; Duration.OneSecond; Duration.OneMillisecond;

(Duration)someTimeSpan;    // explicit cast in; implicit cast out
```

### Properties

```csharp
d.TotalDays; d.TotalHours; d.TotalMinutes; d.TotalSeconds; d.TotalMilliseconds; d.Ticks;
d.IsZero; d.IsPositive; d.IsNegative;
d.Abs(); d.Negate();
d.ToTimeSpan();
```

### Operators

```csharp
d1 + d2;   d1 - d2;
d * 2.0;   2.0 * d;   d / 2.0;      // scaling (÷0 → DivideByZeroException)
double ratio = d1 / d2;              // how many d2 fit in d1
-d; +d;
d1 < d2;   // full comparison set
```

### ISO 8601 parsing and formatting

```csharp
Duration.FromHours(1.5).ToString();      // "PT1H30M"
Duration.Parse("PT1H30M");
Duration.Parse("P2DT3H");                // days + time part
Duration.Parse("-PT30S");                // leading minus for negative
Duration.TryParse(s, out var d);
```

Limits of the format (by design): days are the largest unit emitted/accepted — **no weeks, no
years/months** (those are calendar concepts → `Period`); `H`/`M`/`S` must appear after the `T`;
fractional values are accepted (`PT0.5H`); custom format strings are not supported —
`ToString(format, provider)` always returns the ISO form.

---

## Period

**When you need it**: calendar-speak — "a 1-month subscription", "payment due in 30 days",
"2 years and 3 months". Components are `Years`/`Months`/`Days` ints; the *actual* length depends
on the date you apply it to.

### Creation

```csharp
new Period(1, 2, 3);          // 1 year, 2 months, 3 days
Period.FromYears(1); Period.FromMonths(6); Period.FromDays(30); Period.FromWeeks(2); // = 14 days
Period.Zero;

Period.Between(start, end);   // calendar difference between two LocalDates
```

`Between` computes years, then months, then leftover days (borrowing from the month before the
end date when needed). It negates cleanly when `start > end`, but because month lengths differ,
`Between(a, b)` applied back to `a` lands on `b` while intermediate components may not be what
you'd guess near month ends — verify with round-trip tests if you do bookkeeping with it.

### Properties and arithmetic

```csharp
p.Years; p.Months; p.Days;
p.IsZero;
p.IsNegative;         // true if ANY component is negative
p.TotalMonths;        // Years*12 + Months — ignores Days

p.Negate();
p.Normalize();        // 14 months → 1 year 2 months; days are never folded

p1 + p2; p1 - p2;     // component-wise — results are NOT normalized (can hold 14 months)
p * 3; -p;
var (y, m, d) = p;    // Deconstruct
```

### Applying to dates

Apply via `LocalDateExtensions` (years → months → days, clamping at month ends):

```csharp
using Pragmatic.Temporal.Extensions;

var expiry = startDate.Add(Period.FromYears(1));
var back   = someDate.Subtract(Period.FromMonths(3));
// Jan 31 + P1M → Feb 28/29 (clamped, like AddMonths)
```

### Parsing and formatting

```csharp
p.ToString();          // "P1Y2M3D" (zero → "P0D")
p.ToDisplayString();   // "1 year, 2 months, 3 days"
Period.Parse("P1Y2M3D"); Period.Parse("P-1M");   // negative components allowed
Period.TryParse(s, out var p);
```

The parser accepts only `P…Y…M…D` (case-insensitive, at least one component) — no weeks (`P2W`
is invalid; use `FromWeeks`), no time part (`PT…` belongs to `Duration`).

---

## DateRange

**When you need it**: reporting periods, booking spans, "the last 30 days" — a **closed,
inclusive** range `[Start, End]` of `LocalDate`s that you can query, combine, and enumerate.

### Creation

```csharp
new DateRange(start, end);                 // throws ArgumentException if start > end
DateRange.Between(d1, d2);                 // auto-orders the endpoints
DateRange.SingleDay(date);

DateRange.Week(date);                      // week containing date (Monday start by default)
DateRange.Month(date);  DateRange.Month(2026, 6);
DateRange.Quarter(date); DateRange.Quarter(2026, 2);   // quarter validated 1-4
DateRange.Year(date);   DateRange.Year(2026);
DateRange.LastDays(today, 30);             // last 30 days ending at today (inclusive)
DateRange.NextDays(start, 7);
```

> ⚠️ **Testability**: `ThisWeek()`, `ThisMonth()`, `ThisQuarter()`, `ThisYear()` — the
> overloads *without* a date — read `DateTime.Today` (server-local, not injectable) and are
> marked `[Obsolete]`. `LastDays(n)`/`NextDays(n)` without a date have the same problem.
> In application code always use the overloads that take an explicit `today`:
>
> ```csharp
> var range = DateRange.ThisMonth(clock.Today);      // ✅ testable, zone-explicit
> var last30 = DateRange.LastDays(clock.Today, 30);  // ✅
> ```

### Properties

```csharp
r.Start; r.End;        // both inclusive
r.Days;                // count inclusive of both endpoints (Jan 1..Jan 3 → 3)
r.IsSingleDay;
r.IsEmpty;             // true only for DateRange.Empty
r.Duration;            // Duration.FromDays(Days)
var (s, e) = r;        // Deconstruct
```

`DateRange.Empty` (also the `default` of the struct) is empty: `Days == 0`, it
enumerates nothing, `Contains`/`Overlaps`/`IsAdjacentTo` are always false, and
`Union` with it returns the other range unchanged.

### Set operations

```csharp
r.Contains(date);          r.Contains(otherRange);
r.Overlaps(other);         r.IsAdjacentTo(other);      // touching, no gap, no overlap
r.Intersect(other);        // → DateRange.Empty when no overlap
r.Union(other);            // smallest range containing BOTH
r.Expand(3);               // 3 days wider on each side
r.Shift(7);                // move the whole range forward a week
r.Split(30);               // lazy chunks of ≤30 days
```

> ⚠️ `Union` returns the **bounding span**: for disjoint ranges it silently includes the gap
> between them (`[Jan 1-5] ∪ [Jan 20-25]` → `[Jan 1-25]`). If you need "only the covered days",
> keep the ranges separate.

### Enumeration

```csharp
foreach (var day in r) { ... }    // IEnumerable<LocalDate>, day by day
r.ToList();                        // pre-sized List<LocalDate>
r.Weekdays();  r.Weekends();       // lazy filters
```

Enumerating materializes every day — fine for months, think twice for multi-decade ranges.

### Equality, parsing, formatting

- `==` / `!=` compare both endpoints.
- `ToString()` → ISO interval `"2026-01-01/2026-03-31"`; `Parse`/`TryParse` accept the same
  format and reject `start > end`.

---

## CronExpression

**When you need it**: recurring schedules — "every day at 9", "last day of the month", "second
Tuesday". Zero-dependency implementation; a `sealed class` (not a struct).

### Creation

```csharp
// Presets (properties, not methods):
CronExpression.EveryMinute;   // "* * * * *"
CronExpression.EveryHour;     // "0 * * * *"
CronExpression.Midnight;      // "0 0 * * *"
CronExpression.Noon;          // "0 12 * * *"
CronExpression.Weekdays;      // "0 0 * * 1-5"
CronExpression.Weekends;      // "0 0 * * 0,6"

// Factories:
CronExpression.EveryMinutes(15);                          // 1-59
CronExpression.EveryHours(4);                             // 1-23
CronExpression.Daily(new TimeOnly(9, 0));
CronExpression.Weekly(DayOfWeek.Monday, new TimeOnly(8, 0));
CronExpression.Monthly(1, new TimeOnly(0, 0));            // day validated 1-31

// Parsing (5 fields, or 6 with leading seconds):
var c  = CronExpression.Parse("*/15 9-17 * * 1-5");       // every 15min, 9-17, weekdays
var c6 = CronExpression.Parse("30 0 9 * * *");            // 6 fields → seconds supported
CronExpression.TryParse(s, out var parsed);
```

### Grammar

Fields: `minute hour day-of-month month day-of-week` (prepend `second` for the 6-field form).

| Syntax | Meaning | Valid in |
|--------|---------|----------|
| `*` or `?` | any value | all fields |
| `a,b,c` | list | all fields |
| `a-b` | range | all fields |
| `*/n`, `a/n`, `a-b/n` | steps | all fields |
| `L` | last day of the month | day-of-month only |
| `15W` | nearest weekday to the 15th | day-of-month only |
| `2#3` | third Tuesday of the month (`dow#n`, n 1-5) | day-of-week only |
| `JAN`-`DEC` | month names | month |
| `SUN`-`SAT` | day names | day-of-week |
| `7` | alias for Sunday (same as `0`) | day-of-week |

### Getting occurrences

```csharp
DateTimeOffset? next = cron.GetNextOccurrence(from);                  // evaluated in UTC
DateTimeOffset? nextRome = cron.GetNextOccurrence(from, romeZone);    // evaluated in that zone

IEnumerable<DateTimeOffset> runs = cron.GetOccurrences(
    from,                       // exclusive
    until: endOfYear,           // inclusive; null → until maxOccurrences
    zone: romeZone,
    maxOccurrences: 1000);      // default cap

bool hits = cron.Matches(someInstant, romeZone);
```

- Results are returned **normalized to UTC**, but matching happens on the wall clock of the
  given zone (default UTC).
- `GetNextOccurrence` returns `null` if nothing matches within ~4 years of search.
- **DST-aware**: wall times inside a spring-forward gap are skipped; during a fall-back overlap
  the earliest instant strictly after `from` is chosen, so `GetOccurrences` never stalls or
  double-fires.
- There is no `GetNextOccurrences(from, count)` — use `GetOccurrences(...).Take(count)`.

### Unix vs Quartz day semantics

When *both* day-of-month and day-of-week are restricted (`"0 0 1 * MON"`), implementations
disagree. Pragmatic supports both, explicitly:

```csharp
var unix   = CronExpression.Parse("0 0 1 * MON");                        // default: Unix
var quartz = CronExpression.Parse("0 0 1 * MON", CronSemantics.Quartz);
```

- **Unix** (default): OR — "the 1st of the month, *or* any Monday". If one of the two fields is
  `*`, only the other is evaluated.
- **Quartz**: AND — "the 1st of the month, *only if* it's a Monday".

### Equality caveat

`Equals`/`==`/`GetHashCode` compare the **raw expression string**. `"0 12 * * *"` and
`CronExpression.Noon` are semantically identical but *not* equal.

---

## LocalDate extensions

`using Pragmatic.Temporal.Extensions;` — calendar navigation on top of `LocalDate`.

### Relative weekday navigation

```csharp
date.Next(DayOfWeek.Friday);          // strictly after date; same weekday → +7 days
date.NextOrSame(DayOfWeek.Friday);    // date itself if it's already Friday
date.Previous(DayOfWeek.Monday);      // strictly before; same weekday → -7 days
date.PreviousOrSame(DayOfWeek.Monday);
```

### Month / year positional navigation

```csharp
date.FirstInMonth(DayOfWeek.Monday);
date.LastInMonth(DayOfWeek.Friday);
date.NthInMonth(2, DayOfWeek.Tuesday);      // Patch Tuesday. Returns LocalDate? —
                                            // null when the nth occurrence doesn't exist.
date.NthInMonth(-1, DayOfWeek.Sunday);      // last Sunday; -2 = one week before last
date.NthInMonthOrThrow(4, DayOfWeek.Thursday);  // Thanksgiving, throws if missing
date.FirstInYear(DayOfWeek.Monday);
date.LastInYear(DayOfWeek.Friday);
```

### ISO weeks

```csharp
date.IsoWeekOfYear();     // 1-53, ISO 8601 (weeks start Monday, week 1 has the first Thursday)
date.IsoWeekYear();       // may differ from Year near Jan 1 / Dec 31
LocalDateExtensions.FromIsoWeekDate(2026, 25, DayOfWeek.Wednesday);
```

### Weekday helpers (weekend-only — no holidays)

```csharp
date.NextWeekday();  date.NextWeekdayOrSame();
date.PreviousWeekday(); date.PreviousWeekdayOrSame();
date.NearestWeekday();    // Saturday → Friday, Sunday → Monday
```

These only skip Saturday/Sunday. For holiday-aware business days use `ITemporalCalculator` —
see [Business Days](/modules/temporal/business-days/).

---

## DateTimeOffset extensions

`using Pragmatic.Temporal.Extensions;` — the correct bridge from an instant to local types:

```csharp
DateTimeOffset instant = clock.UtcNow;

instant.ToLocalDate("Europe/Rome");        // what date is it in Rome right now?
instant.ToLocalTime(romeZone);             // what time is it in Rome?
instant.ToLocalDateTime("Asia/Tokyo");     // wall-clock datetime in Tokyo
```

Each converts the **UTC instant** into the target zone (string overloads resolve IANA or
Windows ids via `TimeZoneResolver`, throwing `TimeZoneNotFoundException` for unknown ids). This
is what you want instead of `LocalDate.FromDateTimeOffset` (which ignores the offset).

---

## Culture-aware display formatting

**When you need it:** showing a temporal value to a user in their culture ("21/03/2026" for
Italian, "3/21/2026" for US English) instead of the fixed ISO wire format.

**What you write:** `LocalDate` and `LocalTime` already work with the
`Pragmatic.Internationalization` formatting APIs — they convert implicitly to
`DateOnly`/`TimeOnly`. For `LocalDateTime` and `ZonedDateTime`, add the bridge package:

```bash
dotnet add package Pragmatic.Temporal.Internationalization
```

```csharp
using Pragmatic.Temporal.Internationalization.Formatting;   // GlobalizationFormatter overloads
using Pragmatic.Temporal.Internationalization.Extensions;   // ambient-culture extensions

public class BookingDisplayService(GlobalizationFormatter formatter)
{
    public string Describe(ZonedDateTime start, LocalDateTime deadline)
        => $"{formatter.FormatDateTime(start)} — reply by {formatter.FormatDate(deadline)}";
}

// Or with the ambient I18N culture context (mirrors I18n's DateExtensions):
var label = webinarStart.FormatDateTime();   // ZonedDateTime → wall time in ITS zone
```

**What you get:** `LocalDateTime` formats its wall time as-is (no timezone conversion);
`ZonedDateTime` formats the wall time **in its own zone** — the same instant shown for a
Rome-zoned and a UTC-zoned value renders differently, which is exactly what the type means
for display. All members delegate to the Internationalization APIs; no formatting logic is
duplicated. Full formatting reference: the `Pragmatic.Internationalization` module docs.

---

## Choosing the right type — quick answers

| You have… | Use |
|-----------|-----|
| "The customer's birthday" | `LocalDate` |
| "We open at 9" | `LocalTime` |
| "The meeting is Tuesday 14:30" (one office) | `LocalDateTime` |
| "The webinar starts at 15:00 Rome time" (global audience) | `ZonedDateTime` |
| "The token expires in 15 minutes" | `Duration` |
| "The subscription lasts 1 month" | `Period` |
| "Q2 2026" | `DateRange` |
| "Every weekday at 7" | `CronExpression` |
| "What time is it?" | `IClock` (injected) — never `DateTime.Now` |
