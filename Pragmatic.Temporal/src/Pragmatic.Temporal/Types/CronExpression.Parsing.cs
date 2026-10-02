using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Temporal.Types;

public sealed partial class CronExpression
{
    #region Parsing

    /// <summary>Parses a cron expression with Unix semantics (OR for day fields).</summary>
    /// <exception cref="FormatException">If expression is invalid.</exception>
    public static CronExpression Parse(string expression)
    {
        return Parse(expression, CronSemantics.Unix);
    }

    /// <summary>Parses a cron expression with specified semantics.</summary>
    /// <param name="expression">The cron expression string.</param>
    /// <param name="semantics">How to interpret day-of-month and day-of-week fields.</param>
    /// <exception cref="FormatException">If expression is invalid.</exception>
    public static CronExpression Parse(string expression, CronSemantics semantics)
    {
        if (expression is not null &&
            expression.Trim().Equals("@reboot", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                "'@reboot' is not supported: it depends on process lifetime, not wall-clock time.");

        if (TryParse(expression, semantics, out var result))
            return result!;
        throw new FormatException($"'{expression}' is not a valid cron expression.");
    }

    /// <summary>Tries to parse a cron expression with Unix semantics.</summary>
    public static bool TryParse(string? expression, [NotNullWhen(true)] out CronExpression? result)
    {
        return TryParse(expression, CronSemantics.Unix, out result);
    }

    /// <summary>Tries to parse a cron expression with specified semantics.</summary>
    public static bool TryParse(
        string? expression,
        CronSemantics semantics,
        [NotNullWhen(true)] out CronExpression? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var effective = expression.Trim();

        // Vixie macros expand to standard 5-field patterns; Expression keeps the
        // original string (equality is raw-string based). @reboot has no wall-clock
        // meaning and is rejected.
        if (effective.StartsWith('@'))
        {
            var expanded = effective.ToLowerInvariant() switch
            {
                "@yearly" or "@annually" => "0 0 1 1 *",
                "@monthly" => "0 0 1 * *",
                "@weekly" => "0 0 * * 0",
                "@daily" or "@midnight" => "0 0 * * *",
                "@hourly" => "0 * * * *",
                _ => null
            };

            if (expanded is null)
                return false;

            effective = expanded;
        }

        var parts = effective.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || parts.Length > 6)
            return false;

        var hasSeconds = parts.Length == 6;
        var offset = hasSeconds ? 0 : -1;

        try
        {
            var seconds = hasSeconds
                ? CronField.Parse(parts[0], 0, 59, CronFieldType.Seconds)
                : CronField.Zero;

            var minutes = CronField.Parse(parts[offset + 1], 0, 59, CronFieldType.Minutes);
            var hours = CronField.Parse(parts[offset + 2], 0, 23, CronFieldType.Hours);
            var dayOfMonth = CronField.Parse(parts[offset + 3], 1, 31, CronFieldType.DayOfMonth);
            var month = CronField.Parse(parts[offset + 4], 1, 12, CronFieldType.Month);
            var dayOfWeek = CronField.Parse(parts[offset + 5], 0, 6, CronFieldType.DayOfWeek);

            result = new CronExpression(expression, hasSeconds, semantics,
                seconds, minutes, hours, dayOfMonth, month, dayOfWeek);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    #endregion
}
