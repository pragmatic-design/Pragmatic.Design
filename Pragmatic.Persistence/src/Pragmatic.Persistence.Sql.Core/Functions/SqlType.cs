using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>A SQL type to cast to, named per dialect by the generator.</summary>
public enum SqlType
{
    Integer,
    BigInt,
    Numeric,
    Double,
    Text,
}
