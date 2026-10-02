using System.Text;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Renders the C# expression for a 201 <c>Location</c> header from a <c>[CreatedAt]</c> template.
///     <c>{PropertyName}</c> tokens become URL-escaped, invariant-formatted reads of the success
///     value's properties; everything else is emitted as literal text. Shared by the Mutation,
///     DomainAction and Endpoint handler templates.
/// </summary>
internal static class CreatedAtLocationRenderer
{
    /// <summary>
    ///     Builds the location argument expression (an interpolated string, or a plain literal when
    ///     the template has no tokens) reading token values from <paramref name="successIdentifier"/>.
    /// </summary>
    public static string Render(string template, string successIdentifier)
    {
        var sb = new StringBuilder();
        var hasTokens = template.IndexOf('{') >= 0;
        sb.Append(hasTokens ? "$\"" : "\"");

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close > i + 1)
                {
                    var property = template.Substring(i + 1, close - i - 1);
                    // The hole is parenthesized: a bare `global::` inside {} would have its ':'
                    // parsed as the interpolation format clause (CS8076).
                    sb.Append("{(global::System.Uri.EscapeDataString(global::System.Convert.ToString(")
                        .Append(successIdentifier).Append('.').Append(property)
                        .Append(", global::System.Globalization.CultureInfo.InvariantCulture) ?? \"\"))}");
                    i = close;
                    continue;
                }

                // Unbalanced '{' — emit literally (escaped for the interpolated string).
                sb.Append(hasTokens ? "{{" : "{");
                continue;
            }

            switch (c)
            {
                case '}':
                    sb.Append(hasTokens ? "}}" : "}");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
