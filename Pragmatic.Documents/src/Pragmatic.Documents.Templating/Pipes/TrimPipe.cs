using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

internal sealed class TrimPipe : ITemplatePipe
{
    public string Name => "trim";
    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
        => input?.ToString()?.Trim();
}
