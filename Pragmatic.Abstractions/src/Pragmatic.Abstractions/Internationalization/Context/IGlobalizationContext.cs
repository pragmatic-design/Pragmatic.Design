using System.Globalization;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Provides access to the current globalization context (culture, timezone, currency).
/// </summary>
/// <remarks>
///     This interface allows formatting operations to access culture information
///     without passing it explicitly. In ASP.NET Core, this is typically populated
///     from request headers or user preferences.
/// </remarks>
public interface IGlobalizationContext
{
    /// <summary>
    ///     Gets the current culture for formatting operations.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>
    ///     Gets the current timezone for date/time conversions.
    ///     Returns <c>null</c> when no timezone is resolved (falls back to UTC).
    /// </summary>
    TimeZoneInfo? TimeZone => null;

    /// <summary>
    ///     Gets the preferred currency code (e.g., "EUR", "USD").
    ///     Returns <c>null</c> when no currency preference is set (derive from culture).
    /// </summary>
    string? CurrencyCode => null;
}
