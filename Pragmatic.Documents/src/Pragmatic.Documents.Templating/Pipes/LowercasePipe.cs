using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

internal sealed class LowercasePipe : ITemplatePipe
{
    public string Name => "lowercase";
    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
        => input?.ToString()?.ToLower(culture);
}
