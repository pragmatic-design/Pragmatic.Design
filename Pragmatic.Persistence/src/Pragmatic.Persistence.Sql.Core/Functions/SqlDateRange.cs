namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     A range of days, both ends included, either end open when null: an owned type, two columns <c>{Property}_From</c> and
///     <c>{Property}_To</c> on every dialect. In a query's lambda <see cref="Contains" /> is SQL: <c>daterange(from, to, '[]') @> day</c>
///     on PostgreSQL (the native operator over the two columns), the two bounds compared elsewhere (reported as an emulation,
///     PRAG3012); on a loaded row it runs in .NET with the same meaning.
/// </summary>
[Owned]
public sealed class SqlDateRange
{
    /// <summary>The first day, included; null when the range has no start.</summary>
    public DateOnly? From { get; set; }

    /// <summary>The last day, included; null when the range has no end.</summary>
    public DateOnly? To { get; set; }

    public static SqlDateRange Between(DateOnly? from, DateOnly? to) => new() { From = from, To = to };

    public bool Contains(DateOnly day) => (From is null || From.Value <= day) && (To is null || day <= To.Value);
}
