namespace Pragmatic.Temporal.EntityFrameworkCore;

/// <summary>
///     Configuration options for EF Core temporal handling.
/// </summary>
public sealed class TemporalEfCoreOptions
{
    /// <summary>
    ///     Whether to automatically apply converters to all temporal type properties.
    ///     Default: true
    /// </summary>
    public bool ApplyToAllProperties { get; set; } = true;

    /// <summary>
    ///     Whether to store Duration as ticks (true) or TimeSpan (false).
    ///     Default: true (ticks are more portable)
    /// </summary>
    public bool StoreDurationAsTicks { get; set; } = true;

    /// <summary>
    ///     Whether <see cref="DateTimeOffset" /> and <see cref="DateTime" /> reach the column in UTC.
    ///     Default: true.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rest of the framework reads stored instants as UTC — the JSON layer converts outward
    ///         from that assumption — so this is what makes the assumption true rather than hoped for.
    ///         An offset is converted; a <see cref="DateTimeKind.Local" /> value is converted; an
    ///         <see cref="DateTimeKind.Unspecified" /> one is taken at its word and labelled UTC.
    ///     </para>
    ///     <para>
    ///         ⚠️ Unspecified is deliberately not run through <c>ToUniversalTime()</c>: that reads it as
    ///         local and shifts it by the machine's offset, so a value already in UTC would move on
    ///         every machine but one.
    ///     </para>
    ///     <para>
    ///         Set it to false only where wall-clock time in the column is the intent. Everything that
    ///         converts on the way out will then be working from a value it cannot interpret.
    ///     </para>
    /// </remarks>
    public bool NormalizeInstantsToUtc { get; set; } = true;
}
