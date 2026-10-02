namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Derives assembly-level namespace prefixes for generated per-assembly files.
///     Uses the longest common namespace prefix across all types in the assembly,
///     producing names like "Showcase.Booking" instead of just "Showcase".
/// </summary>
internal static class NamespacePrefixHelper
{
    /// <summary>
    ///     Finds the longest common namespace prefix from a list of namespaces.
    ///     Falls back to the first namespace in ordinal order if there is no common prefix.
    /// </summary>
    /// <remarks>
    ///     The input is sorted before anything else is done with it. Unsorted, the fallback would return
    ///     whatever the upstream incremental provider happened to enumerate first — so the derived
    ///     prefix, and with it the hint name of generated files, could change between two runs over
    ///     identical source. Sorting makes the result a function of the input set alone.
    /// </remarks>
    /// <example>
    ///     ["Showcase.Booking.Entities", "Showcase.Booking.Services"] → "Showcase.Booking"
    ///     ["Showcase.Booking.Entities"] → "Showcase.Booking.Entities"
    ///     ["Beta.Api", "Alpha.Api"] → "Alpha.Api" (no common prefix — first in ordinal order)
    ///     [] → ""
    /// </example>
    public static string DerivePrefix(IEnumerable<string> namespaces)
    {
        var list = namespaces
            .Where(ns => !string.IsNullOrEmpty(ns))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        if (list.Count == 0)
            return string.Empty;
        if (list.Count == 1)
            return list[0];

        // Pre-split all namespaces to avoid repeated allocations in the loop
        var allSegments = list.Select(ns => ns.Split('.')).ToList();
        var minSegments = allSegments.Min(s => s.Length);
        var commonSegments = new List<string>();

        for (var i = 0; i < minSegments; i++)
        {
            var segment = allSegments[0][i];
            if (allSegments.All(s => s[i] == segment))
                commonSegments.Add(segment);
            else
                break;
        }

        return commonSegments.Count > 0 ? string.Join(".", commonSegments) : list[0];
    }

    /// <summary>
    ///     Extracts the first segment of a dotted prefix for use as a C# identifier.
    ///     "Showcase.Booking.Entities" → "Showcase".
    /// </summary>
    public static string ToIdentifier(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return string.Empty;
        var firstDot = prefix.IndexOf('.');
        return firstDot > 0 ? prefix.Substring(0, firstDot) : prefix;
    }

}
