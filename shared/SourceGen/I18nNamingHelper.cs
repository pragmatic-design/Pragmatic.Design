// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Derives i18n translation keys from enum namespace and type name.
///     Convention: {feature-kebab}.{enum-derived}.{value-camelCase}
/// </summary>
internal static class I18nNamingHelper
{
    /// <summary>
    ///     Derives the i18n key prefix for an enum from its namespace context.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Given <c>Showcase.Booking.Reservations.Enums.ReservationStatus</c>:
    ///         - Feature: "Reservations" → singularized → "reservation"
    ///         - Enum name: "ReservationStatus" → strip feature prefix "Reservation" → "status"
    ///         - Result: "reservation.status"
    ///     </para>
    ///     <para>
    ///         Fallback when derivation fails: "reservation-status" (full enum name kebab-cased).
    ///     </para>
    /// </remarks>
    public static string DeriveI18nPrefix(string enumNamespace, string enumTypeName)
    {
        // Try to extract feature from namespace (the segment before "Enums" or the last meaningful segment)
        var segments = enumNamespace.Split('.');
        var featureSegment = FindFeatureSegment(segments);

        if (featureSegment is not null)
        {
            var feature = Singularize(featureSegment);
            var featureLower = ToCamelCaseValue(feature);

            // Try to strip feature prefix from enum name (e.g., "ReservationStatus" → "Status")
            var remainder = StripPrefix(enumTypeName, feature);
            if (remainder is not null && remainder.Length > 0)
                return featureLower + "." + ToCamelCaseValue(remainder);

            // Feature found but no prefix match — use full enum name
            return featureLower + "." + ToCamelCaseValue(enumTypeName);
        }

        // Fallback: just kebab-case the enum name
        return ToKebabCase(enumTypeName);
    }

    /// <summary>
    ///     Computes the full i18n key for an enum member.
    /// </summary>
    public static string DeriveI18nKey(string prefix, string memberName)
        => prefix + "." + ToCamelCaseValue(memberName);

    /// <summary>
    ///     Converts PascalCase to camelCase (first char lowercase).
    /// </summary>
    public static string ToCamelCaseValue(string name)
        => name.Length > 0 ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;

    /// <summary>
    ///     Converts PascalCase to kebab-case (e.g., "ReservationStatus" → "reservation-status").
    /// </summary>
    public static string ToKebabCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase)) return pascalCase;

        var result = new System.Text.StringBuilder(pascalCase.Length + 4);
        for (var i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (char.IsUpper(c) && i > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }

    /// <summary>
    ///     Simple English singularization (strip trailing 's', handle 'ies' → 'y').
    /// </summary>
    internal static string Singularize(string name)
    {
        if (name.Length <= 2) return name;

        if (name.EndsWith("ies", System.StringComparison.Ordinal))
            return name.Substring(0, name.Length - 3) + "y";

        if (name.EndsWith("ses", System.StringComparison.Ordinal) ||
            name.EndsWith("xes", System.StringComparison.Ordinal))
            return name.Substring(0, name.Length - 2);

        if (name.EndsWith("s", System.StringComparison.Ordinal) &&
            !name.EndsWith("ss", System.StringComparison.Ordinal) &&
            !name.EndsWith("us", System.StringComparison.Ordinal))
            return name.Substring(0, name.Length - 1);

        return name;
    }

    private static string? FindFeatureSegment(string[] namespaceSegments)
    {
        // Look for the segment before "Enums" (common pattern: Feature/Enums/EnumType.cs)
        for (var i = 0; i < namespaceSegments.Length; i++)
        {
            if (namespaceSegments[i].Equals("Enums", System.StringComparison.Ordinal) && i > 0)
                return namespaceSegments[i - 1];
        }

        // No "Enums" segment — use the last segment that looks like a feature
        // (skip common suffixes like "Models", "Types")
        for (var i = namespaceSegments.Length - 1; i >= 0; i--)
        {
            var seg = namespaceSegments[i];
            if (seg is "Models" or "Types" or "Enums" or "Contracts" or "Dtos")
                continue;
            // Skip single-word project names (e.g., "Booking", "Billing") — too generic
            if (i <= 1) break;
            return seg;
        }

        return null;
    }

    private static string? StripPrefix(string enumName, string prefix)
    {
        if (enumName.StartsWith(prefix, System.StringComparison.Ordinal) && enumName.Length > prefix.Length)
            return enumName.Substring(prefix.Length);
        return null;
    }
}
