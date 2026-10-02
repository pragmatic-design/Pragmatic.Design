using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a cron expression for scheduling.
///     Custom implementation with zero dependencies.
///     <para>
///         Supports standard 5-part format: "minute hour day-of-month month day-of-week"
///         And extended 6-part format: "second minute hour day-of-month month day-of-week"
///     </para>
///     <para>
///         Supports: *, ranges (1-5), lists (1,3,5), steps (*/5), L (last day), W (weekday), # (nth weekday)
///     </para>
/// </summary>
public sealed partial class CronExpression : IEquatable<CronExpression>
{
    private readonly CronField _dayOfMonth;
    private readonly CronField _dayOfWeek;
    private readonly CronField _hours;
    private readonly CronField _minutes;
    private readonly CronField _month;
    private readonly CronField _seconds;

    private CronExpression(string expression, bool hasSeconds, CronSemantics semantics,
        CronField seconds, CronField minutes, CronField hours,
        CronField dayOfMonth, CronField month, CronField dayOfWeek)
    {
        Expression = expression;
        HasSeconds = hasSeconds;
        Semantics = semantics;
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    /// <summary>The original cron string.</summary>
    public string Expression { get; }

    /// <summary>Whether this is a 6-part expression (includes seconds).</summary>
    public bool HasSeconds { get; }

    /// <summary>
    ///     The semantics used for day-of-month and day-of-week matching.
    ///     Unix (default): OR logic. Quartz: AND logic.
    /// </summary>
    public CronSemantics Semantics { get; }

    /// <summary>
    ///     Vixie-cron DST rule: a schedule is "interval-like" when its minute or hour
    ///     field (or seconds, when present) is star-based (*, */n). Star-based time
    ///     fields mean "keep firing as real time advances", so during a DST fall-back
    ///     the repeated wall hour fires in BOTH offsets. Fixed-time schedules fire
    ///     once, in the first (daylight) pass — same behavior as Cronos.
    /// </summary>
    internal bool IsIntervalExpression =>
        _minutes.IsStarBased || _hours.IsStarBased || (HasSeconds && _seconds.IsStarBased);

    #region Common Expressions

    /// <summary>Runs every minute: "* * * * *"</summary>
    public static CronExpression EveryMinute { get; } = Parse("* * * * *");

    /// <summary>Runs every hour at minute 0: "0 * * * *"</summary>
    public static CronExpression EveryHour { get; } = Parse("0 * * * *");

    /// <summary>Runs daily at midnight: "0 0 * * *"</summary>
    public static CronExpression Midnight { get; } = Parse("0 0 * * *");

    /// <summary>Runs daily at noon: "0 12 * * *"</summary>
    public static CronExpression Noon { get; } = Parse("0 12 * * *");

    /// <summary>Runs at midnight on weekdays: "0 0 * * 1-5"</summary>
    public static CronExpression Weekdays { get; } = Parse("0 0 * * 1-5");

    /// <summary>Runs at midnight on weekends: "0 0 * * 0,6"</summary>
    public static CronExpression Weekends { get; } = Parse("0 0 * * 0,6");

    #endregion

    #region Factory Methods

    /// <summary>Creates a cron expression that runs every N minutes.</summary>
    public static CronExpression EveryMinutes(int minutes)
    {
        ThrowIfOutOfRange(minutes, 1, 59);
        return Parse($"*/{minutes} * * * *");
    }

    /// <summary>Creates a cron expression that runs every N hours.</summary>
    public static CronExpression EveryHours(int hours)
    {
        ThrowIfOutOfRange(hours, 1, 23);
        return Parse($"0 */{hours} * * *");
    }

    /// <summary>Creates a cron expression that runs daily at a specific time.</summary>
    public static CronExpression Daily(TimeOnly time)
    {
        return Parse($"{time.Minute} {time.Hour} * * *");
    }

    /// <summary>Creates a cron expression that runs weekly on a specific day and time.</summary>
    public static CronExpression Weekly(DayOfWeek day, TimeOnly time)
    {
        return Parse($"{time.Minute} {time.Hour} * * {(int)day}");
    }

    /// <summary>Creates a cron expression that runs monthly on a specific day and time.</summary>
    public static CronExpression Monthly(int dayOfMonth, TimeOnly time)
    {
        ThrowIfOutOfRange(dayOfMonth, 1, 31);
        return Parse($"{time.Minute} {time.Hour} {dayOfMonth} * *");
    }

    #endregion

    #region Equality

    public bool Equals(CronExpression? other)
    {
        return other is not null && Expression == other.Expression;
    }

    public override bool Equals(object? obj)
    {
        return obj is CronExpression other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Expression.GetHashCode();
    }

    public static bool operator ==(CronExpression? left, CronExpression? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(CronExpression? left, CronExpression? right)
    {
        return !Equals(left, right);
    }

    public override string ToString()
    {
        return Expression;
    }

    #endregion

}