using System.Globalization;
using System.Text;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Templating.I18N;

/// <summary>
/// Resolves <c>t:</c> expressions via Pragmatic <see cref="IStringLocalizer"/>.
/// </summary>
public sealed class StringLocalizerTranslationResolver(IStringLocalizer localizer) : ITranslationResolver
{
    /// <remarks>
    ///     Asks the localizer in <paramref name="culture" /> — the context's, the one the pipes use — and
    ///     formats parameters with it. An invariant culture means the context set none, and the ambient
    ///     one applies, as it always did.
    /// </remarks>
    public string Resolve(string key, IReadOnlyDictionary<string, object?>? parameters, CultureInfo culture)
    {
        var isSet = !Equals(culture, CultureInfo.InvariantCulture);
        var value = (isSet ? localizer.WithCulture(culture.Name) : localizer)[key].Value;

        if (parameters is null || parameters.Count == 0)
            return value;

        // Template parameters are NAMED. Relying on params-object[] positional order would
        // depend on the dictionary's (unspecified) enumeration order, which differs across
        // IStringLocalizer implementations and dictionary types. We interpolate by name —
        // index-stable and implementation-independent — replacing {paramName} placeholders.
        return Interpolate(value, parameters, isSet ? culture : CultureInfo.CurrentCulture);
    }

    private static string Interpolate(
        string template, IReadOnlyDictionary<string, object?> parameters, CultureInfo culture)
    {
        if (template.IndexOf('{') < 0) return template;

        var sb = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{')
            {
                // Escaped brace "{{"
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }

                var end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    var name = template.Substring(i + 1, end - i - 1).Trim();
                    // Named placeholder {paramName} — index-stable, implementation-independent.
                    if (parameters.TryGetValue(name, out var replacement))
                    {
                        sb.Append(Format(replacement, culture));
                        i = end + 1;
                        continue;
                    }
                    // Positional placeholder {0}, {1} — the standard IStringLocalizer/string.Format
                    // convention. Map the index onto the parameter values (insertion order).
                    if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        && index >= 0 && index < parameters.Count)
                    {
                        var n = 0;
                        foreach (var v in parameters.Values)
                        {
                            if (n++ != index) continue;
                            sb.Append(Format(v, culture));
                            break;
                        }
                        i = end + 1;
                        continue;
                    }
                }

                // Not a known placeholder — emit verbatim.
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static string Format(object? value, CultureInfo culture) => value switch
    {
        null => "",
        IFormattable f => f.ToString(null, culture),
        _ => value.ToString() ?? ""
    };
}
