namespace Pragmatic.Persistence.Sql;

/// <summary>The unit of a date function (<c>Sql.DateDiff</c>, <c>Sql.DateAdd</c>, <c>Sql.Truncate</c>): a constant at the call site, the generator reads it.</summary>
public enum DatePart
{
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
}
