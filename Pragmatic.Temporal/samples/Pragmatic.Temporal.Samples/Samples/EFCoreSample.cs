using Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates the Pragmatic.Temporal EF Core value converters without needing a database.
///     Every converter exposes EF Core's <c>ConvertToProvider</c> (entity → column) and
///     <c>ConvertFromProvider</c> (column → entity) delegates, so a full store round-trip can be
///     exercised in-memory. In a real <c>DbContext</c> these are wired up via
///     <c>optionsBuilder.UsePragmaticTemporal()</c> or <c>modelBuilder.ApplyTemporalConventions()</c>.
/// </summary>
public static class EFCoreSample
{
    public static void Run()
    {
        Console.WriteLine("--- EF Core Value Converters Sample ---\n");
        Console.WriteLine("Each converter maps a temporal type <-> its database column representation.\n");

        // LocalDate <-> DateOnly column
        RoundTrip("LocalDate -> DateOnly",
            new LocalDateValueConverter(),
            new LocalDate(2024, 4, 25));

        // LocalTime <-> TimeOnly column
        RoundTrip("LocalTime -> TimeOnly",
            new LocalTimeValueConverter(),
            new LocalTime(new TimeOnly(9, 30, 0)));

        // LocalDateTime <-> DateTime column (Kind preserved as Unspecified)
        RoundTrip("LocalDateTime -> DateTime",
            new LocalDateTimeValueConverter(),
            new LocalDateTime(new DateTime(2024, 4, 25, 14, 30, 0)));

        // Duration <-> ticks (long) column
        RoundTrip("Duration -> long (ticks)",
            new DurationToTicksConverter(),
            Duration.FromTimeSpan(TimeSpan.FromHours(2.5)));

        // Duration <-> TimeSpan column (for providers with native TimeSpan support)
        RoundTrip("Duration -> TimeSpan",
            new DurationToTimeSpanConverter(),
            Duration.FromTimeSpan(TimeSpan.FromMinutes(90)));

        // Period <-> ISO 8601 string column ("P1Y2M3D")
        RoundTrip("Period -> string (ISO 8601)",
            new PeriodValueConverter(),
            Period.Parse("P1Y2M3D")); // 1 year, 2 months, 3 days

        // DateRange <-> "start/end" string column
        RoundTrip("DateRange -> string",
            new DateRangeValueConverter(),
            DateRange.Parse("2024-01-01/2024-12-31"));

        // CronExpression <-> string column
        RoundTrip("CronExpression -> string",
            new CronExpressionValueConverter(),
            CronExpression.Parse("0 9 * * 1-5"));

        // ZonedDateTime <-> ISO 8601 + zone bracket string column
        RoundTrip("ZonedDateTime -> string",
            new ZonedDateTimeValueConverter(),
            ZonedDateTime.FromUtc(
                new DateTimeOffset(2024, 7, 15, 12, 0, 0, TimeSpan.Zero),
                TimeZoneResolver.GetTimeZone("Europe/Rome")));

        // Nullable variants handle null end-to-end.
        Console.WriteLine("\nNullable converters:");
        var nullableDuration = new NullableDurationToTicksConverter();
        Console.WriteLine($"  Duration?  null  -> column: {Show(nullableDuration.ConvertToProvider(null))}");
        Console.WriteLine(
            $"  Duration?  90m   -> column: {Show(nullableDuration.ConvertToProvider(Duration.FromTimeSpan(TimeSpan.FromMinutes(90))))}");

        var nullablePeriod = new NullablePeriodValueConverter();
        Console.WriteLine($"  Period?    null  -> column: {Show(nullablePeriod.ConvertToProvider(null))}");

        Console.WriteLine();
    }

    private static void RoundTrip<TModel, TProvider>(
        string label,
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TModel, TProvider> converter,
        TModel value)
    {
        var stored = converter.ConvertToProvider(value);   // entity property -> DB column
        var loaded = converter.ConvertFromProvider(stored); // DB column -> entity property
        Console.WriteLine($"  {label,-30} value={value}  column={Show(stored)}  restored={loaded}");
    }

    private static string Show(object? o) => o is null ? "<null>" : o.ToString() ?? "<null>";
}
