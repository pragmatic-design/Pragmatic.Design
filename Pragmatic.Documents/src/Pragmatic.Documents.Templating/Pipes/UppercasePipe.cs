using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

internal sealed class UppercasePipe : ITemplatePipe
{
    public string Name => "uppercase";
    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
        => input?.ToString()?.ToUpper(culture);
}
