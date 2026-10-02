namespace Pragmatic.Temporal.Types;

/// <summary>
///     Specifies the semantics for combining day-of-month and day-of-week fields in cron expressions.
/// </summary>
/// <remarks>
///     The interpretation of day-of-month and day-of-week fields varies between implementations:
///     <list type="bullet">
///         <item>
///             <term>Unix</term>
///             <description>
///                 OR logic - matches if EITHER day-of-month OR day-of-week matches.
///                 "0 0 1 * MON" = "midnight on the 1st of each month OR any Monday"
///             </description>
///         </item>
///         <item>
///             <term>Quartz</term>
///             <description>
///                 AND logic - matches only if BOTH day-of-month AND day-of-week match.
///                 "0 0 1 * MON" = "midnight on the 1st of each month, but only if it's a Monday"
///             </description>
///         </item>
///     </list>
/// </remarks>
public enum CronSemantics
{
    /// <summary>
    ///     Unix cron semantics: day-of-month and day-of-week use OR logic.
    ///     This is the most common interpretation and matches traditional Unix cron.
    ///     <para>
    ///         Exception: If one field is "*" (any), only the other is evaluated.
    ///     </para>
    /// </summary>
    Unix = 0,

    /// <summary>
    ///     Quartz scheduler semantics: day-of-month and day-of-week use AND logic.
    ///     Matches Spring/Quartz scheduler behavior.
    ///     <para>
    ///         This results in fewer matches (more restrictive).
    ///     </para>
    /// </summary>
    Quartz = 1
}