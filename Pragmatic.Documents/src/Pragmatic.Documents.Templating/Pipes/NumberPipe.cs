using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

internal sealed class NumberPipe : ITemplatePipe
{
    public string Name => "number";

    public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (input is null) return null;

        // Guard non-numeric input: Convert.ToDouble throws on unparsable values. Pass through verbatim.
        if (!TryToDouble(input, out var number))
            return input.ToString();

        var decimals = args.Count > 0 && int.TryParse(args[0], out var d) ? d : 0;
        var format = $"N{decimals}";
        return number.ToString(format, culture);
    }

    private static bool TryToDouble(object input, out double value)
    {
        switch (input)
        {
            case double dd: value = dd; return true;
            case int ii: value = ii; return true;
            case long ll: value = ll; return true;
            case decimal mm: value = (double)mm; return true;
            case float ff: value = ff; return true;
            case short sh: value = sh; return true;
            case byte by: value = by; return true;
            case string s: return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default: value = 0; return false;
        }
    }
}
