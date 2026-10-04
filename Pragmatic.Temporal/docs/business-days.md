# Business Days

Business-day arithmetic with weekend and holiday awareness: delivery dates, SLA deadlines,
settlement dates, working-day counters, without hand-rolling weekend loops.

Everything goes through `ITemporalCalculator` (implementation: `TemporalCalculator`). It is
registered by `AddPragmaticTemporal()` and injectable; you can also `new` it directly.

```csharp
using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Types;

public class DeliveryService(ITemporalCalculator calculator, IClock clock)
{
    public LocalDate EstimateDelivery(int businessDays)
        => calculator.AddBusinessDays(clock.Today, businessDays);
}
```

> There is no `LocalDate.Today`: "today" always comes from an injected `IClock`
> (`clock.Today` returns a `DateOnly` that converts implicitly to `LocalDate`).

## Adding Business Days

**When you need it**: "ship in 5 working days", "respond within 3 business days".

**What you write**: three overloads.

```csharp
var calculator = new TemporalCalculator();          // weekends only (NoHolidaysProvider)

// 1. Weekends only
var delivery = calculator.AddBusinessDays(orderDate, 5);

// 2. Weekends + public holidays of a country (needs a holiday provider, see below)
var delivery = calculator.AddBusinessDays(orderDate, 5, "IT");

// 3. Weekends + an explicit set of dates to skip
var closures = new[] { new LocalDate(2026, 8, 14) };
var delivery = calculator.AddBusinessDays(orderDate, 5, closures);
```

**What you get**: the resulting `LocalDate`. Negative `days` moves backward. The
implementation skips full weeks mathematically (5 business days = 7 calendar days), so cost
is O(1) on the weekday part, so it is safe for batch/reporting workloads.

## Counting Business Days

**When you need it**: "how many working days between two dates" (payroll, SLA reports).

```csharp
int count = calculator.CountBusinessDays(from, to);          // weekends only
int count = calculator.CountBusinessDays(from, to, "IT");    // weekends + IT holidays
```

**Interval semantics (important)**: the range is `[from, to)`, so `from` is **included**,
`to` is **excluded**. `CountBusinessDays(monday, friday)` on a plain week returns **4**
(Mon, Tue, Wed, Thu). If `from >= to` the result is `0`. To include the end date, pass
`to.AddDays(1)`.

## Checks

```csharp
calculator.IsWeekend(date);                 // Saturday or Sunday
calculator.IsBusinessDay(date);             // not a weekend
calculator.IsBusinessDay(date, "IT");       // not a weekend AND not an IT holiday
calculator.IsHoliday(date, "IT");           // asks the holiday provider
```

Note: `IsBusinessDay(date)` without a country code only checks weekends; Christmas on a
Thursday is a "business day" for that overload.

## Next / Previous Business Day

**When you need it**: "first working day after the due date", "last working day before month end".

```csharp
var next = calculator.NextBusinessDay(date);              // weekends only
var next = calculator.NextBusinessDay(date, "IT");        // weekends + holidays
var prev = calculator.PreviousBusinessDay(date);
var prev = calculator.PreviousBusinessDay(date, "IT");
```

**What you get**: always a date **strictly after** (or before) `date`. If `date` itself is
a business day, you still get the following one. There is no "include today" option; check
`IsBusinessDay(date)` first if you need that behavior.

## Period Boundaries

Convenience pass-throughs, useful when you already have the calculator injected:

```csharp
calculator.StartOfWeek(date);               // Monday by default
calculator.EndOfWeek(date, DayOfWeek.Sunday);
calculator.StartOfMonth(date);   calculator.EndOfMonth(date);
calculator.StartOfQuarter(date); calculator.EndOfQuarter(date);
calculator.StartOfYear(date);    calculator.EndOfYear(date);
```

The same operations exist directly on `LocalDate` (see [Core Types](core-types.md)).

## Holidays

### The Holiday model

`Holiday` is a `readonly record struct` with three components; the type is required:

```csharp
using Pragmatic.Temporal.Holidays;

var christmas = new Holiday(new LocalDate(2026, 12, 25), "Natale", HolidayType.Public);

// Or the factory shortcuts (one per HolidayType):
var h1 = Holiday.Public(new LocalDate(2026, 1, 1), "Capodanno");
var h2 = Holiday.Regional(new LocalDate(2026, 12, 7), "Sant'Ambrogio");   // local holiday
var h3 = Holiday.Bank(new LocalDate(2026, 12, 31), "Bank closure");
var h4 = Holiday.Optional(new LocalDate(2026, 2, 14), "Observance");
```

### IHolidayProvider: the contract

Holiday data is **country-based and year-based**: the calculator asks the provider per
year and ISO 3166-1 alpha-2 country code:

```csharp
public interface IHolidayProvider
{
    IEnumerable<string> SupportedCountries { get; }
    IEnumerable<Holiday> GetHolidays(int year, string countryCode);
    IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode);
    bool IsHoliday(LocalDate date, string countryCode);
}
```

### NoHolidaysProvider (default)

`new TemporalCalculator()` and the default DI registration use `NoHolidaysProvider.Instance`:
every country-aware overload behaves as "weekends only" until you plug in a real provider.

### StaticHolidayProvider

**When you need it**: a fixed, known-in-advance holiday list (configuration, seed data, tests).

**What you write**: the fluent builder is the recommended way.

```csharp
var provider = StaticHolidayProvider.CreateBuilder()
    .AddHoliday("IT", 2026, 1, 1, "Capodanno")
    .AddHoliday("IT", 2026, 12, 25, "Natale")
    .AddHoliday("IT", new LocalDate(2026, 4, 25), "Liberazione", HolidayType.Public)
    .AddHoliday("US", 2026, 7, 4, "Independence Day")
    .Build();

var calculator = new TemporalCalculator(provider);
calculator.IsHoliday(new LocalDate(2026, 12, 25), "IT");   // true
```

Alternatives: the constructor takes `IEnumerable<(string CountryCode, Holiday Holiday)>`
tuples, and `AddHoliday(countryCode, holiday)` / `AddHolidays(countryCode, holidays)` mutate
an existing instance.

Caveats:
- **Not thread-safe for mutation**: build it fully (ideally via the builder) before sharing
  it as a singleton; don't call `AddHoliday` concurrently at runtime.
- Country codes are matched case-insensitively.
- The `regionCode` overload currently **ignores the region** and returns country-level
  holidays.

### Custom providers

**When you need it**: holidays from a database, an HTTP service, or a NuGet dataset.

```csharp
public sealed class DatabaseHolidayProvider(IHolidayRepository repository) : IHolidayProvider
{
    public IEnumerable<string> SupportedCountries => repository.GetCountries();

    public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
        => repository.GetHolidays(year, countryCode)
                     .Select(h => Holiday.Public(h.Date, h.Name));

    public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
        => GetHolidays(year, countryCode);

    public bool IsHoliday(LocalDate date, string countryCode)
        => repository.IsHoliday(date, countryCode);
}
```

Tip: `GetHolidays` is called once per year in the requested range; cache per
`(year, countryCode)` if the source is remote.

### Real holiday data (Nager.Date adapter)

**When you need it**: real public-holiday calendars for many countries without
maintaining the dates yourself. Pragmatic.Temporal deliberately ships no holiday
data (it goes stale and needs constant per-country maintenance); plug a data
source into `IHolidayProvider` instead. [Nager.Date](https://date.nager.at/)
covers 100+ countries; its **REST API is free**, while the offline NuGet/Docker
packages require a sponsorship license key for commercial use.

**What you write**: an adapter over the free REST API, cached per `(year, country)`.

```csharp
public sealed class NagerHolidayProvider(HttpClient http) : IHolidayProvider
{
    private readonly ConcurrentDictionary<(int Year, string Country), IReadOnlyList<Holiday>> _cache = new();

    public IEnumerable<string> SupportedCountries => ["IT", "US", "DE", "FR"]; // the ones you use

    public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
        => _cache.GetOrAdd((year, countryCode.ToUpperInvariant()), key =>
        {
            // GET https://date.nager.at/api/v3/PublicHolidays/{year}/{country}
            var response = http
                .GetFromJsonAsync<NagerHolidayDto[]>($"api/v3/PublicHolidays/{key.Year}/{key.Country}")
                .GetAwaiter().GetResult() ?? [];
            return response
                .Select(h => Holiday.Public(LocalDate.Parse(h.Date), h.LocalName))
                .ToArray();
        });

    public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
        => GetHolidays(year, countryCode);

    public bool IsHoliday(LocalDate date, string countryCode)
        => GetHolidays(date.Year, countryCode).Any(h => h.Date == date);

    private sealed record NagerHolidayDto(string Date, string LocalName);
}
```

```csharp
services.AddHttpClient<NagerHolidayProvider>(c => c.BaseAddress = new Uri("https://date.nager.at/"));
services.AddPragmaticTemporal();
services.UseHolidayProvider<NagerHolidayProvider>();
```

**What you get**: `calculator.AddBusinessDays(date, 5, "IT")` skips real Italian
public holidays, with one HTTP call per `(year, country)` for the process lifetime.
For production, prefer an async warm-up (fetch the years you need at startup) over
the blocking `GetAwaiter().GetResult()` shown here for brevity.

## Wiring It Up (DI)

```csharp
// Default: calculator with NoHolidaysProvider
services.AddPragmaticTemporal();

// With a holiday provider type (resolved from DI)
services.AddPragmaticTemporal<DatabaseHolidayProvider>();

// With a provider instance
services.AddPragmaticTemporal(StaticHolidayProvider.CreateBuilder()
    .AddHoliday("IT", 2026, 12, 25, "Natale")
    .Build());

// Swap the provider after the fact (e.g., in tests)
services.UseHolidayProvider<FakeHolidayProvider>();
```

**What you get**: `ITemporalCalculator` registered as a singleton wired to the configured
`IHolidayProvider`. Inject `ITemporalCalculator` (the interface), not the concrete class.

## Scheduling with Cron

**When you need it**: recurring schedules ("every weekday at 8:00") stored as data,
evaluated without external dependencies.

**What you write**:

```csharp
using Pragmatic.Temporal.Types;

var cron = CronExpression.Parse("0 8 * * 1-5");          // 5-part: min hour dom month dow
var withSec = CronExpression.Parse("30 0 8 * * 1-5");    // 6-part: leading seconds field

if (!CronExpression.TryParse(userInput, out var parsed)) { /* reject */ }

// Presets
var everyMinute = CronExpression.EveryMinute;   // "* * * * *"
var everyHour   = CronExpression.EveryHour;     // "0 * * * *"
var midnight    = CronExpression.Midnight;      // "0 0 * * *"
var noon        = CronExpression.Noon;          // "0 12 * * *"
var weekdays    = CronExpression.Weekdays;      // "0 0 * * 1-5"
var weekends    = CronExpression.Weekends;      // "0 0 * * 0,6"

// Factories
var every15 = CronExpression.EveryMinutes(15);
var every6h = CronExpression.EveryHours(6);
var daily   = CronExpression.Daily(new TimeOnly(8, 30));
var weekly  = CronExpression.Weekly(DayOfWeek.Monday, new TimeOnly(9, 0));
var monthly = CronExpression.Monthly(1, new TimeOnly(0, 0));
```

Supported grammar: `*`, `?`, lists (`1,3,5`), ranges (`1-5`), steps (`*/15`, `10-50/10`),
`L` (last day of month), `nW` (nearest weekday), `dow#n` (nth weekday of month), month and
day names (`JAN`, `MON`), `7` as an alias for Sunday. Vixie macros parse too: `@yearly` /
`@annually`, `@monthly`, `@weekly`, `@daily` / `@midnight`, `@hourly` (case-insensitive;
`Expression` keeps the original string). `@reboot` is rejected: it depends on process
lifetime, not wall-clock time.

**What you get**, on evaluation:

```csharp
// Next run strictly after `from`; evaluated in `zone` (defaults to UTC)
DateTimeOffset? next = cron.GetNextOccurrence(clock.UtcNow, romeZone);

// Lazy stream: `from` exclusive, `until` inclusive, capped by maxOccurrences (default 1000)
foreach (var occurrence in cron.GetOccurrences(from, until, romeZone))
    Console.WriteLine(occurrence);

// Does a specific instant match?
bool hit = cron.Matches(someInstant, romeZone);
```

Results are UTC-normalized `DateTimeOffset`s. `GetNextOccurrence` returns `null` if no match
exists within ~4 years of search.

DST is handled with Vixie/Cronos semantics:

- **Spring-forward gap**: local times that don't exist are skipped forward to the next
  valid wall time.
- **Fall-back (repeated hour)**: the behavior depends on the schedule's shape:
  - *Interval* expressions, with a star-based minute or hour field (`*`, `*/n`), e.g.
    `*/30 * * * *` or `0 * * * *`, fire in **both** passes of the repeated hour: an
    every-30-minutes job really runs every 30 minutes of real time.
  - *Fixed-time* expressions, with explicit values, lists or ranges in the time fields, e.g.
    `30 2 * * *` or `0,30 2 * * *`, fire **once**, in the first (daylight) pass.

Occurrences are always strictly increasing instants; schedules never stall across
transitions.

### Unix vs Quartz day semantics

When **both** day-of-month and day-of-week are restricted (e.g. `0 0 13 * 5`), the two
conventions disagree:

```csharp
var unix   = CronExpression.Parse("0 0 13 * 5");                        // default: Unix
var quartz = CronExpression.Parse("0 0 13 * 5", CronSemantics.Quartz);
```

- `CronSemantics.Unix` (default): day fields combine with **OR**, as in "the 13th, or any Friday".
- `CronSemantics.Quartz`: day fields combine with **AND**, as in "only Friday the 13th".

> Full Unix crontab parity is a non-goal: `L`/`W`/`#` are extensions, and exotic crontab
> quirks are not guaranteed. Equality of two `CronExpression`s compares the **raw string**,
> not the schedule semantics.

## Testing

Deterministic tests need a fixed "today" and controlled holidays; use
`Pragmatic.Temporal.Testing` (see [Testing](testing.md)):

```csharp
using Pragmatic.Temporal.Testing;

public class DeliveryCalculationTests
{
    private readonly TemporalCalculator _calculator = new(
        new TestHolidayProvider().AddHoliday(2026, 12, 25, "IT"));

    [Fact]
    public void AddBusinessDays_SkipsWeekend()
    {
        var friday = new LocalDate(2026, 6, 12);
        var result = _calculator.AddBusinessDays(friday, 1);
        Assert.Equal(new LocalDate(2026, 6, 15), result);   // Monday
    }

    [Fact]
    public void AddBusinessDays_SkipsHoliday()
    {
        var thursday = new LocalDate(2026, 12, 24);
        var result = _calculator.AddBusinessDays(thursday, 1, "IT");
        Assert.Equal(new LocalDate(2026, 12, 28), result);  // skips Fri 25th + weekend
    }
}
```

> `TestHolidayProvider.AddHoliday(year, month, day, countryCode)`: the last argument is the
> **country code**, not the holiday name.
