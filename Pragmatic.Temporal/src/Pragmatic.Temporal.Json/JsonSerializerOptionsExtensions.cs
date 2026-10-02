using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Temporal.Json.Behaviors;
using Pragmatic.Temporal.Json.Converters;

namespace Pragmatic.Temporal.Json;

/// <summary>
///     Extension methods for <see cref="JsonSerializerOptions" />.
/// </summary>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    ///     Adds Pragmatic.Temporal JSON converters to the options.
    /// </summary>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The options for chaining.</returns>
    public static JsonSerializerOptions AddPragmaticTemporal(this JsonSerializerOptions options)
    {
        // Core date/time types
        options.Converters.Add(new LocalDateConverter());
        options.Converters.Add(new NullableLocalDateConverter());
        options.Converters.Add(new LocalTimeConverter());
        options.Converters.Add(new NullableLocalTimeConverter());
        options.Converters.Add(new LocalDateTimeConverter());
        options.Converters.Add(new NullableLocalDateTimeConverter());
        options.Converters.Add(new ZonedDateTimeConverter());
        options.Converters.Add(new NullableZonedDateTimeConverter());

        // Duration types
        options.Converters.Add(new DurationConverter());
        options.Converters.Add(new NullableDurationConverter());
        options.Converters.Add(new PeriodConverter());
        options.Converters.Add(new NullablePeriodConverter());

        // Range and scheduling
        options.Converters.Add(new DateRangeConverter());
        options.Converters.Add(new NullableDateRangeConverter());
        options.Converters.Add(new CronExpressionConverter());

        return options;
    }

    /// <summary>
    ///     Enables the per-property timezone behaviors registered in
    ///     <see cref="TemporalJsonBehaviorRegistry" /> by chaining
    ///     <see cref="TemporalJsonModifier" /> onto the options' type-info resolver.
    ///     Existing resolvers (including source-generated contexts) are preserved.
    /// </summary>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The options for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown under Native AOT when the options carry no resolver.
    /// </exception>
    public static JsonSerializerOptions AddPragmaticTemporalBehaviors(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A modifier has to be chained onto something. When the caller brought no resolver, the old
        // code quietly supplied a reflective one — which under Native AOT is the silent fallback this
        // whole effort exists to remove: behaviours would appear to be enabled while resolution had
        // become reflective underneath. Say it instead.
        var resolver = options.TypeInfoResolver ?? (RuntimeFeature.IsDynamicCodeSupported
            ? new DefaultJsonTypeInfoResolver()
            : throw new InvalidOperationException(
                "AddPragmaticTemporalBehaviors needs a TypeInfoResolver to chain onto, and none is set. "
                + "Register a source-generated JsonSerializerContext first — under Native AOT there is no "
                + "reflection-based resolver to fall back on."));

        options.TypeInfoResolver = resolver.WithAddedModifier(TemporalJsonModifier.Apply);
        return options;
    }

    /// <summary>
    ///     Creates a new <see cref="JsonSerializerOptions" /> with Pragmatic.Temporal converters.
    /// </summary>
    public static JsonSerializerOptions CreateTemporalOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        return options.AddPragmaticTemporal();
    }
}