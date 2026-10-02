using System.Globalization;
using Pragmatic.Temporal.Samples.Samples;

// Pin culture explicitly so sample output is deterministic regardless of the machine locale
// (the CI runner is it-IT). Temporal types already format invariantly; this covers the
// Console.WriteLine of dates / DayOfWeek / numeric values in the samples themselves.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

Console.WriteLine("=== Pragmatic.Temporal Samples ===\n");

// Run all samples
CoreTypesSample.Run();
DateRangeSample.Run();
PeriodSample.Run();
RelativeDatesSample.Run();
ClockSample.Run();
ZonedDateTimeSample.Run();
DstSample.Run();
CronExpressionSample.Run();
BusinessDaysSample.Run();
TestHolidayProviderSample.Run();
JsonSerializationSample.Run();
EFCoreSample.Run();
AspNetCoreSample.Run();

Console.WriteLine("\n=== All samples completed! ===");
