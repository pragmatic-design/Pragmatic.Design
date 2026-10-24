using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     A statement written by hand as the root of a chain: <c>session.Sql&lt;Order&gt;($"SELECT * FROM \"Orders\" WHERE \"Total\" &gt; {minimum}")</c>.
///     Every hole of the interpolated string is a parameter (never text: the SQL cannot be composed from values), identifiers
///     are double-quoted as everywhere in the library (the dialect's quoting is applied), and the rows come back as the entity
///     or as a <c>[MapFrom]</c> DTO whose properties name the statement's columns. The chain's operators apply on top
///     (<c>SELECT … FROM (statement) t WHERE …</c>): the valve for what the translated subset does not cover, still a prepared plan.
/// </summary>
public sealed class SqlRaw
{
    public SqlRaw(string text, object?[] values)
    {
        Text = text;
        Values = values;
    }

    /// <summary>The statement with <c>@p0</c>… where the holes were, in order.</summary>
    public string Text { get; }

    public object?[] Values { get; }
}
