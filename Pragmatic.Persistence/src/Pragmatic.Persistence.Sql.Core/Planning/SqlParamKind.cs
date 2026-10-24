using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>The .NET type a parameter carries, so the command can be typed once and prepared.</summary>
public enum SqlParamKind
{
    Guid,
    String,
    Int32,
    Int64,
    Decimal,
    Double,
    Boolean,
    DateTimeOffset,
    DateTime,
    Bytes,
    DateOnly,
    TimeOnly,
    /// <summary>A collection for an IN list: expanded to one parameter per item when the command is built, so the plan's text varies with the count.</summary>
    List,
    /// <summary>A <c>float[]</c> vector (<c>[Vector(n)]</c>): bound as its JSON text, the statement casts it to the dialect's vector type.</summary>
    Vector,
    /// <summary>A type the generator could not name: bound untyped, so the command is reused but not prepared.</summary>
    Unknown,
    /// <summary>An array column (<c>string[]</c>, <c>List    Unknown,lt;Guid    Unknown,gt;</c>…): a native array on PostgreSQL (bound untyped, the provider maps it), the JSON text elsewhere.</summary>
    Array,
}
