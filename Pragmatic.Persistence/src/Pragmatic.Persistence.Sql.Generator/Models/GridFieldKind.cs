namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>The categories Pragmatic's grid adapter filters by (<c>PropertyTypeCategory</c>): one family of operators each.</summary>
internal enum GridFieldKind
{
    String,
    Boolean,
    Numeric,
    DateTime,
    Guid,
    Enum,
    Unknown,
}
