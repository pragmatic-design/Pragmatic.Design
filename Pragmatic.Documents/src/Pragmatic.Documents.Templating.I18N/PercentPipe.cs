using System.Globalization;
using Pragmatic.Documents.Templating.Pipes;

namespace Pragmatic.Documents.Templating.I18N;

/// <summary>
/// Locale-aware percent formatting pipe: <c>{{rate | percent:2}}</c>.
/// Input should be a fraction (0.42 = 42%).
/// </summary>
public sealed class PercentPipe : ITemplatePipe
{
    public string Name => "percent";

    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (input is null) return null;

        var value = ToDouble(input);
        if (value is null) return input.ToString();

        var decimals = args.Count > 0 && int.TryParse(args[0], out var d) ? d : 0;
        return value.Value.ToString($"P{decimals}", culture);
    }

    private static double? ToDouble(object? value) => value switch
    {
        double d => d,
        decimal m => (double)m,
        float f => f,
        int i => i,
        long l => l,
        string s when double.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };
}
