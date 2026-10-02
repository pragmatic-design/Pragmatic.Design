namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

internal sealed partial class EndpointRegistrationTemplate
{
    /// <summary>
    ///     Converts a window string ("1m", "30s", "1h", "1d") to a TimeSpan expression.
    /// </summary>
    private static string ParseWindowToTimeSpan(string window)
    {
        if (window.EndsWith("s", System.StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromSeconds({window.TrimEnd('s', 'S')})";
        if (window.EndsWith("m", System.StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromMinutes({window.TrimEnd('m', 'M')})";
        if (window.EndsWith("h", System.StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromHours({window.TrimEnd('h', 'H')})";
        if (window.EndsWith("d", System.StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromDays({window.TrimEnd('d', 'D')})";

        // Fallback: try to parse as seconds
        return $"System.TimeSpan.FromSeconds({window})";
    }
}
