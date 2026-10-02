using Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.Conventions;

/// <summary>
///     Single source of truth for the temporal CLR type → value converter (+ max length) mapping.
///     Consumed by both <see cref="TemporalModelConvention" /> and
///     <see cref="ModelBuilderExtensions.ApplyTemporalConventions" /> so the two paths cannot diverge.
/// </summary>
internal static class TemporalPropertyConfigurator
{
    /// <summary>Converter type and optional max length for a temporal property.</summary>
    internal readonly record struct TemporalPropertyMapping(Type ConverterType, int? MaxLength);

    /// <summary>
    ///     Returns true when the CLR type (or its Nullable&lt;&gt; underlying type) is a
    ///     Pragmatic.Temporal type. EF property discovery does not recognize these as
    ///     primitives, so their members must be added to the model explicitly.
    /// </summary>
    internal static bool IsTemporalType(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return type == typeof(LocalDate)
               || type == typeof(LocalTime)
               || type == typeof(LocalDateTime)
               || type == typeof(ZonedDateTime)
               || type == typeof(Duration)
               || type == typeof(Period)
               || type == typeof(DateRange)
               || type == typeof(CronExpression);
    }

    /// <summary>
    ///     Returns the mapping for a property CLR type, or null when the type is not temporal.
    ///     <paramref name="isNullable" /> matters only for reference types (CronExpression),
    ///     where nullability lives on the property rather than the CLR type.
    /// </summary>
    internal static TemporalPropertyMapping? GetMapping(Type clrType, bool isNullable, TemporalEfCoreOptions options)
    {
        if (clrType == typeof(LocalDate))
            return new TemporalPropertyMapping(typeof(LocalDateValueConverter), null);
        if (clrType == typeof(LocalDate?))
            return new TemporalPropertyMapping(typeof(NullableLocalDateValueConverter), null);

        if (clrType == typeof(LocalTime))
            return new TemporalPropertyMapping(typeof(LocalTimeValueConverter), null);
        if (clrType == typeof(LocalTime?))
            return new TemporalPropertyMapping(typeof(NullableLocalTimeValueConverter), null);

        if (clrType == typeof(LocalDateTime))
            return new TemporalPropertyMapping(typeof(LocalDateTimeValueConverter), null);
        if (clrType == typeof(LocalDateTime?))
            return new TemporalPropertyMapping(typeof(NullableLocalDateTimeValueConverter), null);

        if (clrType == typeof(Duration))
            return new TemporalPropertyMapping(
                options.StoreDurationAsTicks ? typeof(DurationToTicksConverter) : typeof(DurationToTimeSpanConverter),
                null);
        if (clrType == typeof(Duration?))
            return new TemporalPropertyMapping(
                options.StoreDurationAsTicks
                    ? typeof(NullableDurationToTicksConverter)
                    : typeof(NullableDurationToTimeSpanConverter),
                null);

        if (clrType == typeof(Period))
            return new TemporalPropertyMapping(typeof(PeriodValueConverter), 50); // P999Y12M31D
        if (clrType == typeof(Period?))
            return new TemporalPropertyMapping(typeof(NullablePeriodValueConverter), 50);

        if (clrType == typeof(DateRange))
            return new TemporalPropertyMapping(typeof(DateRangeValueConverter), 50); // yyyy-MM-dd/yyyy-MM-dd
        if (clrType == typeof(DateRange?))
            return new TemporalPropertyMapping(typeof(NullableDateRangeValueConverter), 50);

        // Reference type: nullability comes from the property, not the CLR type
        if (clrType == typeof(CronExpression))
            return new TemporalPropertyMapping(
                isNullable ? typeof(NullableCronExpressionValueConverter) : typeof(CronExpressionValueConverter),
                100);

        if (clrType == typeof(ZonedDateTime))
            return new TemporalPropertyMapping(typeof(ZonedDateTimeValueConverter), 100); // "2024-01-15T10:30:00+01:00[Europe/Rome]"
        if (clrType == typeof(ZonedDateTime?))
            return new TemporalPropertyMapping(typeof(NullableZonedDateTimeValueConverter), 100);

        return null;
    }
}
