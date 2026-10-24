using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>Builds a <see cref="SqlRaw" /> from an interpolated string: the text as written, each hole replaced by the next parameter and its value kept.</summary>
[InterpolatedStringHandler]
public ref struct SqlRawHandler
{
    private readonly StringBuilder _text;
    private readonly List<object?> _values;

    public SqlRawHandler(int literalLength, int formattedCount)
    {
        _text = new StringBuilder(literalLength + formattedCount * 4);
        _values = new List<object?>(formattedCount);
    }

    public void AppendLiteral(string text) => _text.Append(text);

    public void AppendFormatted<T>(T value)
    {
        _text.Append("@p").Append(_values.Count.ToString(CultureInfo.InvariantCulture));
        _values.Add(value);
    }

    public SqlRaw ToRaw() => new(_text.ToString(), _values.ToArray());
}
