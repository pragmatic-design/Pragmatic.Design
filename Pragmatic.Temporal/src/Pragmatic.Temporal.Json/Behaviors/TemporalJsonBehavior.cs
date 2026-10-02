namespace Pragmatic.Temporal.Json.Behaviors;

/// <summary>
///     Timezone conversion behavior applied to a <see cref="DateTimeOffset" /> or
///     <see cref="DateTime" /> property during JSON serialization/deserialization.
///     Mirrors the Pragmatic.Temporal.AspNetCore conversion attributes.
/// </summary>
public enum TemporalJsonBehavior
{
    /// <summary>
    ///     Input: interpret values without offset information as UTC.
    ///     Output: always emit the value converted to UTC.
    /// </summary>
    AsUtc,

    /// <summary>Input: interpret values without offset information in the client timezone.</summary>
    FromClientTimezone,

    /// <summary>Input: interpret values without offset information in the business timezone.</summary>
    FromBusinessTimezone,

    /// <summary>Output: convert the value to the client timezone.</summary>
    ToClientTimezone,

    /// <summary>Output: convert the value to the business timezone.</summary>
    ToBusinessTimezone,

    /// <summary>Output: emit the value exactly as stored, without any conversion.</summary>
    KeepTimezone
}
