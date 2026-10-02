using System.Globalization;
using Pragmatic.Documents.Templating.Pipes;

namespace Pragmatic.Documents.Templating.I18N;

/// <summary>
/// Locale-aware date formatting pipe: <c>{{date | date:"dd/MM/yyyy"}}</c>.
/// Supports DateTime, DateTimeOffset, DateOnly.
/// </summary>
public sealed class DatePipe : ITemplatePipe
{
    public string Name => "date";

    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (input is null) return null;

        var format = args.Count > 0 ? args[0] : "d"; // short date default

        return input switch
        {
            DateTimeOffset dto => dto.ToString(format, culture),
            DateTime dt => dt.ToString(format, culture),
            DateOnly d => d.ToString(format, culture),
            string s when DateTimeOffset.TryParse(s, culture, DateTimeStyles.None, out var parsed) => parsed.ToString(format, culture),
            _ => input.ToString()
        };
    }
}
