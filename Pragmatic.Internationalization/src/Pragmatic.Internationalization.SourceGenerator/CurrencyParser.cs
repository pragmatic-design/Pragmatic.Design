// =============================================================================
// Pragmatic.Internationalization - CurrencyParser
// Parses currencies.json content into currency models
// =============================================================================

using System.Collections.Immutable;
using System.Text;
using Pragmatic.Internationalization.SourceGenerator.Models;

namespace Pragmatic.Internationalization.SourceGenerator;

/// <summary>
///     Parser for currencies.json content.
/// </summary>
internal static class CurrencyParser
{
    /// <summary>
    ///     Parses JSON content into currency models.
    /// </summary>
    /// <remarks>
    ///     Simple JSON parsing without System.Text.Json (not available in netstandard2.0).
    /// </remarks>
    public static ImmutableArray<CurrencyModel> Parse(string json)
    {
        var currencies = ImmutableArray.CreateBuilder<CurrencyModel>();

        var currentProperty = "";
        var currentValue = new StringBuilder();
        var inString = false;
        var escape = false;

        string? code = null;
        string? name = null;
        string? symbol = null;
        int? minorUnits = null;

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
                            case "symbol":
                                symbol = value;
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
                symbol = null;
                minorUnits = null;
                continue;
            }

            if (c == '}')
            {
                if (code != null && name != null && symbol != null && minorUnits.HasValue)
                    currencies.Add(new CurrencyModel(code, name, symbol, minorUnits.Value));
                continue;
            }

            if (c == ':')
                continue;
            if (c == ',')
                continue;

            // Parse numbers for minorUnits — accumulate all digits before clearing the property
            if (char.IsDigit(c) && currentProperty.Equals("minorUnits", StringComparison.OrdinalIgnoreCase))
            {
                minorUnits = (minorUnits ?? 0) * 10 + (c - '0');
                // Look ahead: if next non-whitespace char is not a digit, close the number
                var j = i + 1;
                while (j < json.Length && json[j] == ' ') j++;
                if (j >= json.Length || !char.IsDigit(json[j]))
                    currentProperty = "";
            }
        }

        return currencies.ToImmutable();
    }
}