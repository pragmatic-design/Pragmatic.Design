using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Transforms;

/// <summary>
///     Utility methods for TranslationKeysTransform.
/// </summary>
internal static partial class TranslationKeysTransform
{
    /// <summary>
    ///     Derives namespace from file path.
    /// </summary>
    private static string DeriveNamespace(string filePath)
    {
        var dir = Path.GetDirectoryName(NormalizePath(filePath)) ?? "";
        var parts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        var skipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "translations", "resources", "i18n", "locales", "lang", "languages",
            "src", "source", "lib", "app", "en", "it", "de", "fr", "es", "pt", "ru", "zh", "ja", "ko"
        };

        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var part = parts[i];
            if (!skipFolders.Contains(part) && IsValidIdentifier(part))
                return part;
        }

        return "Translations";
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        return name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
    }

    private static bool IsCultureCode(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Match patterns: "en", "en-US", "en_US", "zh-Hans", etc.
        return Regex.IsMatch(name, @"^[a-z]{2}(-[a-zA-Z]{2,4})?(_[a-zA-Z]{2,4})?$", RegexOptions.IgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    ///     Converts a key segment to PascalCase property name.
    /// </summary>
    internal static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new List<char>();
        var capitalizeNext = true;

        foreach (var c in input)
        {
            if (c == '_' || c == '-' || c == ' ')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                result.Add(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                result.Add(c);
            }
        }

        var name = new string(result.ToArray());

        if (name.Length > 0 && char.IsDigit(name[0]))
            name = "_" + name;

        return name;
    }

    /// <summary>
    ///     The keys that another key extends — <c>a.b</c> when <c>a.b.title</c> is also present.
    /// </summary>
    /// <remarks>
    ///     Both hierarchy modes turn every segment but the last into a nested class, so such a key
    ///     needs a member and a class of the same name in the same scope. Ordinal comparison because
    ///     the keys are compared to each other, not to a culture-sensitive form.
    /// </remarks>
    private static ImmutableArray<string> PrefixCollisions(Dictionary<string, TranslationKeyModel> keys)
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys.Keys)
        {
            var cut = key.LastIndexOf('.');
            while (cut > 0)
            {
                prefixes.Add(key.Substring(0, cut));
                cut = key.LastIndexOf('.', cut - 1);
            }
        }

        return keys.Keys
            .Where(prefixes.Contains)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
