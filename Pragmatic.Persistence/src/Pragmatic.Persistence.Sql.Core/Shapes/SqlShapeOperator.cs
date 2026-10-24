using System.Globalization;
using System.Text.Json;

namespace Pragmatic.Persistence.Sql.Shapes;

/// <summary>The operators a client may apply to a member: the comparisons, and the LIKE forms on strings.</summary>
public enum SqlShapeOperator
{
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
}
