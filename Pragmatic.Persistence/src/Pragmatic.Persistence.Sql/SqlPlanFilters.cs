using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The filters a plan applies to Pragmatic's entities, decided per execution (<see cref="ISqlFilters" />). The generated text marks each
///     filter's predicate: <c>{f:k}</c>…<c>{/f:k}</c> for slot <c>k</c>. A combination of states (the mask, two bits per slot) makes one
///     statement: an active filter keeps its predicate, a lifted one becomes <c>(1 = 1)</c>, a closed one <c>(1 = 0)</c>, so each variant
///     carries only what applies and its plan is the database's to optimise. The values the predicates read (tenant, user, scopes, the
///     instant) are <see cref="ValueCount" /> parameters after the plan's own, <c>@p{Values}</c> on, on the command only when its variant
///     names them.
/// </summary>
public sealed class SqlPlanFilters
{
    /// <summary>A mask holds two bits per slot.</summary>
    public const int MaxSlots = 32;

    /// <summary>The caller's values a filter reads, in parameter order: the tenant, the user, the scopes, the instant.</summary>
    public const int ValueCount = 4;

    public SqlPlanFilters(SqlFilterSlot[] slots, int values)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Length > MaxSlots)
            throw new ArgumentException($"A plan filters at most {MaxSlots} slots; this one declares {slots.Length}.", nameof(slots));
        Slots = slots;
        Values = values;
    }

    public SqlFilterSlot[] Slots { get; }

    /// <summary>The index of the first value parameter (the tenant); the user's follows, then the scopes', then the instant's.</summary>
    public int Values { get; }

    /// <summary>The combination of states for the session's caller; 0 (every filter active) without <see cref="SqlSession.Filters" />.</summary>
    public ulong Mask(SqlSession session)
    {
        if (session.Filters is not { } filters)
            return 0;
        ulong mask = 0;
        for (var i = 0; i < Slots.Length; i++)
            mask |= (ulong)filters.StateOf(Slots[i]) << (2 * i);
        return mask;
    }

    /// <summary>The statement of one combination: each marked predicate kept, or replaced by a constant.</summary>
    public static string Apply(string sql, ulong mask)
    {
        var start = sql.IndexOf("{f:", StringComparison.Ordinal);
        if (start < 0)
            return sql;
        var text = new StringBuilder(sql.Length);
        var at = 0;
        while (start >= 0)
        {
            var close = sql.IndexOf('}', start);
            var slot = int.Parse(sql.AsSpan(start + 3, close - start - 3), CultureInfo.InvariantCulture);
            var end = "{/f:" + slot.ToString(CultureInfo.InvariantCulture) + "}";
            var stop = sql.IndexOf(end, close, StringComparison.Ordinal);
            if (stop < 0)
                throw new InvalidOperationException($"The filter mark {{f:{slot}}} is not closed in: {sql}");
            text.Append(sql, at, start - at);
            switch ((SqlFilterState)((mask >> (2 * slot)) & 3))
            {
                case SqlFilterState.Active:
                    text.Append(Apply(sql.Substring(close + 1, stop - close - 1), mask)); // a predicate may read another entity's rows (a subquery), marked inside
                    break;
                case SqlFilterState.Lifted:
                    text.Append("(1 = 1)");
                    break;
                default:
                    text.Append("(1 = 0)");
                    break;
            }

            at = stop + end.Length;
            start = sql.IndexOf("{f:", at, StringComparison.Ordinal);
        }

        return text.Append(sql, at, sql.Length - at).ToString();
    }
}
