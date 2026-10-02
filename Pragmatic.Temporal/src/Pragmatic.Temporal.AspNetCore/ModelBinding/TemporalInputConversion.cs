namespace Pragmatic.Temporal.AspNetCore.ModelBinding;

/// <summary>
///     How a bound <see cref="DateTimeOffset" /> / <see cref="DateTime" /> value lacking
///     explicit offset information is interpreted. Resolved from the conversion attributes
///     by <see cref="TemporalConversionModelBinderProvider" />.
/// </summary>
public enum TemporalInputConversion
{
    /// <summary>Interpret as UTC.</summary>
    AsUtc,

    /// <summary>Interpret in the client timezone from the current <c>TemporalContext</c>.</summary>
    FromClientTimezone,

    /// <summary>Interpret in the business timezone from the current <c>TemporalContext</c>.</summary>
    FromBusinessTimezone
}
