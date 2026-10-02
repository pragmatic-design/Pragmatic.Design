namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>
/// Parses template expression strings into <see cref="TemplateExpression"/> AST nodes.
/// Handles interpolated strings (<c>Hello {{name}}</c>) and standalone expressions.
/// </summary>
public static class ExpressionParser
{
    /// <summary>
    /// Parse an interpolated template string that may contain <c>{{expr}}</c> segments.
    /// Returns a single expression (may be <see cref="InterpolatedStringExpression"/> if mixed).
    /// </summary>
    public static TemplateExpression ParseTemplate(string template)
    {
        if (string.IsNullOrEmpty(template))
            return new LiteralExpression(template ?? "");

        var segments = new List<InterpolatedSegment>();
        var pos = 0;

        while (pos < template.Length)
        {
            var start = template.IndexOf("{{", pos, StringComparison.Ordinal);
            if (start < 0)
            {
                // Rest is literal text
                segments.Add(new TextSegment(template[pos..]));
                break;
            }

            // Literal text before {{
            if (start > pos)
                segments.Add(new TextSegment(template[pos..start]));

            // Find closing }}
            var end = template.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
                throw new TemplateParseException($"Unclosed '{{{{' at position {start}");

            var exprText = template[(start + 2)..end].Trim();
            segments.Add(new ExpressionSegment(ParseExpression(exprText)));
            pos = end + 2;
        }

        if (segments.Count == 0) return new LiteralExpression("");
        if (segments.Count == 1 && segments[0] is ExpressionSegment single) return single.Expression;
        if (segments.Count == 1 && segments[0] is TextSegment text) return new LiteralExpression(text.Text);

        return new InterpolatedStringExpression(segments);
    }

    /// <summary>
    /// Parse a standalone expression (no <c>{{ }}</c> delimiters).
    /// </summary>
    public static TemplateExpression ParseExpression(string expression)
    {
        var trimmed = expression.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new LiteralExpression("");

        // Translation: t:key or t:key(params)
        if (trimmed.StartsWith("t:", StringComparison.Ordinal))
            return ParseTranslation(trimmed[2..]);

        var reader = new ExpressionReader(trimmed);
        return reader.ParseToEnd();
    }

    private static TranslateExpression ParseTranslation(string rest)
    {
        var parenIndex = rest.IndexOf('(');
        if (parenIndex < 0)
            return new TranslateExpression(rest.Trim(), null);

        var key = rest[..parenIndex].Trim();
        var closeIndex = rest.LastIndexOf(')');
        if (closeIndex < parenIndex)
            throw new TemplateParseException($"Unclosed '(' in translation expression: t:{rest}");

        var paramsStr = rest[(parenIndex + 1)..closeIndex];
        var parameters = new Dictionary<string, TemplateExpression>();

        foreach (var pair in SplitParams(paramsStr))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0)
                throw new TemplateParseException($"Expected 'name=value' in translation params, got: {pair}");

            var paramName = pair[..eqIndex].Trim();
            var paramExpr = pair[(eqIndex + 1)..].Trim();
            parameters[paramName] = ParseExpression(paramExpr);
        }

        return new TranslateExpression(key, parameters);
    }

    private static IEnumerable<string> SplitParams(string s)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth < 0)
                        throw new TemplateParseException($"Unbalanced ')' in translation params: {s}");
                    break;
                case ',' when depth == 0:
                    yield return s[start..i].Trim();
                    start = i + 1;
                    break;
            }
        }
        var last = s[start..].Trim();
        if (last.Length > 0) yield return last;
    }
}
