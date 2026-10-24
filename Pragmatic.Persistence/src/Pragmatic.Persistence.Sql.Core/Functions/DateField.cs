using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>What <c>EXTRACT</c> reads from a date.</summary>
public enum DateField
{
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
    Epoch,
    DayOfWeek,
}
