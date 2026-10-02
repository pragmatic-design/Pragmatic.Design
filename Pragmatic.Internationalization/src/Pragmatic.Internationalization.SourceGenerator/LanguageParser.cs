// =============================================================================
// Pragmatic.Internationalization - LanguageParser
// Simple JSON parser for ISO 639-1 language codes (netstandard2.0 compatible)
// =============================================================================

using System.Collections.Immutable;
using System.Text;

using Pragmatic.Internationalization.SourceGenerator.Models;

namespace Pragmatic.Internationalization.SourceGenerator;

/// <summary>
///     Parses language JSON data into models for source generation.
/// </summary>
internal static class LanguageParser
{
    /// <summary>
    ///     Parses JSON content into language models.
    /// </summary>
    /// <remarks>
    ///     Simple JSON parsing without System.Text.Json (not available in netstandard2.0).
    /// </remarks>
    public static ImmutableArray<LanguageModel> Parse(string json)
    {
        var languages = ImmutableArray.CreateBuilder<LanguageModel>();

        var currentProperty = "";
        var currentValue = new StringBuilder();
        var inString = false;
        var escape = false;

        string? code = null;
        string? name = null;
        string? nativeName = null;
        bool? rtl = null;
        string? pluralFamily = null;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (escape)
            {
                if (inString)
                    currentValue.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                if (inString)
                    currentValue.Append(c);
                continue;
            }

            if (c == '"')
            {
                if (inString)
                {
                    inString = false;
                    var value = currentValue.ToString();
                    currentValue.Clear();

                    if (string.IsNullOrEmpty(currentProperty))
                    {
                        currentProperty = value;
                    }
                    else
                    {
                        switch (currentProperty.ToLowerInvariant())
                        {
                            case "code":
                                code = value;
                                break;
                            case "name":
                                name = value;
                                break;
                            case "nativename":
                                nativeName = value;
                                break;
                            case "pluralfamily":
                                pluralFamily = value;
                                break;
                        }

                        currentProperty = "";
                    }
                }
                else
                {
                    inString = true;
                }

                continue;
            }

            if (inString)
            {
                currentValue.Append(c);
                continue;
            }

            if (c == '[' || c == ']')
                continue;

            if (c == '{')
            {
                code = null;
                name = null;
                nativeName = null;
                rtl = null;
                pluralFamily = null;
                continue;
            }

            if (c == '}')
            {
                if (code != null && name != null && nativeName != null && rtl.HasValue && pluralFamily != null)
                    languages.Add(new LanguageModel(code, name, nativeName, rtl.Value, pluralFamily));
                continue;
            }

            if (c == ':')
                continue;
            if (c == ',')
                continue;

            // Parse boolean for rtl
            if (currentProperty.Equals("rtl", StringComparison.OrdinalIgnoreCase))
            {
                // Look for 'true' or 'false'
                if (c == 't' || c == 'T')
                {
                    rtl = true;
                    currentProperty = "";
                }
                else if (c == 'f' || c == 'F')
                {
                    rtl = false;
                    currentProperty = "";
                }
            }
        }

        return languages.ToImmutable();
    }
}
