using System.Text.Json.Serialization.Metadata;
using Pragmatic.Temporal.Context;

namespace Pragmatic.Temporal.Json.Behaviors;

/// <summary>
///     JSON type-info modifier that applies the timezone behaviors registered in
///     <see cref="TemporalJsonBehaviorRegistry" /> to <see cref="DateTimeOffset" /> and
///     <see cref="DateTime" /> properties. Designed for use with
///     <c>JsonTypeInfoResolver.WithAddedModifier</c>.
/// </summary>
/// <remarks>
///     <para>
///         Conversions that need the client or business timezone read the per-request
///         <see cref="TemporalContext" /> from <see cref="TemporalContextHolder" />. When no
///         ambient context is available (background work, tests without setup) the value
///         passes through unconverted — the behavior degrades to a no-op rather than guessing.
///     </para>
///     <para>
///         <see cref="DateTime" /> values with <see cref="DateTimeKind.Unspecified" /> are
///         treated as UTC on output (golden rule: storage is UTC). On input, unspecified
///         values are interpreted according to the registered behavior.
///     </para>
/// </remarks>
public static class TemporalJsonModifier
{
    /// <summary>Applies registered timezone behaviors to the type's properties.</summary>
    public static void Apply(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        var behaviors = TemporalJsonBehaviorRegistry.GetBehaviors(typeInfo.Type);
        if (behaviors is null)
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (!behaviors.TryGetValue(property.Name, out var behavior))
                continue;

            if (property.PropertyType == typeof(DateTimeOffset) ||
                property.PropertyType == typeof(DateTimeOffset?) ||
                property.PropertyType == typeof(DateTime) ||
                property.PropertyType == typeof(DateTime?))
            {
                WrapProperty(property, behavior);
            }
        }
    }

    private static void WrapProperty(JsonPropertyInfo property, TemporalJsonBehavior behavior)
    {
        var innerGet = property.Get;
        if (innerGet is not null)
            property.Get = obj => ConvertOutbound(innerGet(obj), behavior);

        var innerSet = property.Set;
        if (innerSet is not null)
            property.Set = (obj, value) => innerSet(obj, ConvertInbound(value, behavior));
    }

    /// <summary>Serialization path: storage value → wire value.</summary>
    private static object? ConvertOutbound(object? value, TemporalJsonBehavior behavior)
    {
        return value switch
        {
            DateTimeOffset dto => ConvertOutbound(dto, behavior),
            DateTime dt => ConvertOutbound(dt, behavior),
            _ => value
        };
    }

    private static DateTimeOffset ConvertOutbound(DateTimeOffset value, TemporalJsonBehavior behavior)
    {
        var context = TemporalContextHolder.Current;
        return behavior switch
        {
            TemporalJsonBehavior.AsUtc => value.ToUniversalTime(),
            TemporalJsonBehavior.ToClientTimezone when context is not null =>
                TimeZoneInfo.ConvertTime(value, context.ClientTimeZone),
            TemporalJsonBehavior.ToBusinessTimezone when context is not null =>
                TimeZoneInfo.ConvertTime(value, context.BusinessTimeZone),
            _ => value
        };
    }

    private static DateTime ConvertOutbound(DateTime value, TemporalJsonBehavior behavior)
    {
        // Unspecified is treated as UTC on the way out: storage is UTC by golden rule.
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        var context = TemporalContextHolder.Current;
        return behavior switch
        {
            TemporalJsonBehavior.AsUtc => utc,
            TemporalJsonBehavior.ToClientTimezone when context is not null =>
                TimeZoneInfo.ConvertTimeFromUtc(utc, context.ClientTimeZone),
            TemporalJsonBehavior.ToBusinessTimezone when context is not null =>
                TimeZoneInfo.ConvertTimeFromUtc(utc, context.BusinessTimeZone),
            TemporalJsonBehavior.KeepTimezone => value,
            _ => value
        };
    }

    /// <summary>Deserialization path: wire value → storage value.</summary>
    private static object? ConvertInbound(object? value, TemporalJsonBehavior behavior)
    {
        return value switch
        {
            DateTimeOffset dto => ConvertInbound(dto, behavior),
            DateTime dt => ConvertInbound(dt, behavior),
            _ => value
        };
    }

    private static DateTimeOffset ConvertInbound(DateTimeOffset value, TemporalJsonBehavior behavior)
    {
        // A DateTimeOffset always carries an explicit offset on the wire; the input
        // behaviors only decide whether to normalize the instant to UTC for storage.
        return behavior switch
        {
            TemporalJsonBehavior.AsUtc or
                TemporalJsonBehavior.FromClientTimezone or
                TemporalJsonBehavior.FromBusinessTimezone => value.ToUniversalTime(),
            _ => value
        };
    }

    private static DateTime ConvertInbound(DateTime value, TemporalJsonBehavior behavior)
    {
        var context = TemporalContextHolder.Current;

        if (value.Kind != DateTimeKind.Unspecified)
        {
            // The wire value carried offset information; the instant is already known.
            return behavior is TemporalJsonBehavior.AsUtc
                or TemporalJsonBehavior.FromClientTimezone
                or TemporalJsonBehavior.FromBusinessTimezone
                ? value.ToUniversalTime()
                : value;
        }

        return behavior switch
        {
            TemporalJsonBehavior.AsUtc => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            TemporalJsonBehavior.FromClientTimezone when context is not null =>
                context.ClientToUtc(value).UtcDateTime,
            TemporalJsonBehavior.FromBusinessTimezone when context is not null =>
                context.BusinessToUtc(value).UtcDateTime,
            _ => value
        };
    }
}
