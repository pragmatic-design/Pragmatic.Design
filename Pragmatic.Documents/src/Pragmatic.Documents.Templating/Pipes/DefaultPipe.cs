using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

internal sealed class DefaultPipe : ITemplatePipe
{
    public string Name => "default";
    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (input is null || (input is string s && string.IsNullOrEmpty(s)))
            return args.Count > 0 ? args[0] : "";
        return input;
    }
}
