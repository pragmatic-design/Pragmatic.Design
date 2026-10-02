using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates cookie extraction code for handler bodies. Cookies are read from
///     HttpContext.Request.Cookies since ASP.NET Core Minimal APIs don't support [FromCookie] binding
///     natively. Mirrors <see cref="ClaimBindingHelper" />, with a 400 (not 401) on missing required
///     values — a cookie is request input, not auth.
/// </summary>
/// <remarks>
///     The same three parts as the claims, for the same reason: reads before the
///     construction, required and optional <c>init</c> values inside it, optional <c>set</c> ones after.
/// </remarks>
internal static class CookieBindingHelper
{
    /// <summary>The reads, and the 400 for a required cookie that is missing or malformed.</summary>
    public static List<string> ReadLines(EndpointModel model)
    {
        var lines = new List<string>();

        if (model.CookieParameters.IsDefaultOrEmpty)
            return lines;

        lines.Add("// Read cookie parameters from the request");

        foreach (var cookie in model.CookieParameters)
        {
            var cookieVar = Local(cookie);

            lines.Add($"httpContext.Request.Cookies.TryGetValue(\"{cookie.CookieName}\", out var {cookieVar});");

            if (!cookie.IsRequired)
                continue;

            lines.Add($"if ({cookieVar} is null)");
            lines.Add(
                $"    return Microsoft.AspNetCore.Http.Results.Problem(\"Missing required cookie: {cookie.CookieName}\", statusCode: 400);");

            // A present-but-malformed required cookie must be rejected, not coerced to a default. The
            // out variable is what the initializer reads.
            if (cookie.NeedsConversion && GetTryParseCall(cookie.TypeName, cookieVar) is { } tryParse)
            {
                lines.Add($"if (!{tryParse})");
                lines.Add(
                    $"    return Microsoft.AspNetCore.Http.Results.Problem(\"Invalid required cookie: {cookie.CookieName}\", statusCode: 400);");
            }
        }

        lines.Add("");
        return lines;
    }

    /// <summary>The initializer entries: required cookies, and optional cookies on <c>init</c> properties.</summary>
    public static IEnumerable<string> InitializerEntries(EndpointModel model)
    {
        if (model.CookieParameters.IsDefaultOrEmpty)
            yield break;

        foreach (var cookie in model.CookieParameters)
        {
            var cookieVar = Local(cookie);

            if (cookie.IsRequired)
            {
                yield return $"{cookie.PropertyName} = {RequiredValue(cookie, cookieVar)}";
                continue;
            }

            if (cookie is not { IsInitOnly: true, InitOnlyFallback: { } fallback })
                continue;

            yield return cookie.NeedsConversion
                ? $"{cookie.PropertyName} = {cookieVar} is not null ? {ParseOf(cookie.TypeName, cookieVar)} : {fallback}"
                : $"{cookie.PropertyName} = {cookieVar} ?? {fallback}";
        }
    }

    /// <summary>The assignments after construction: optional cookies on <c>set</c> properties.</summary>
    public static IEnumerable<string> PostConstructionLines(EndpointModel model, string targetVariable)
    {
        if (model.CookieParameters.IsDefaultOrEmpty)
            yield break;

        foreach (var cookie in model.CookieParameters.Where(c => c is { IsRequired: false, IsInitOnly: false }))
        {
            var cookieVar = Local(cookie);

            yield return cookie.NeedsConversion
                ? $"{targetVariable}.{cookie.PropertyName} = {GetParseExpression(cookie.TypeName, cookieVar)};"
                : $"if ({cookieVar} is not null) {targetVariable}.{cookie.PropertyName} = {cookieVar};";
        }
    }

    private static string RequiredValue(CookieParameterModel cookie, string cookieVar)
    {
        if (!cookie.NeedsConversion)
            return cookieVar;

        // The read declared `{cookieVar}Parsed` in its TryParse; a type with no parse is taken as it came.
        return GetTryParseCall(cookie.TypeName, cookieVar) is not null ? $"{cookieVar}Parsed" : $"{cookieVar}!";
    }

    /// <summary>The parse of a present value, falling back to the type's default when malformed.</summary>
    private static string ParseOf(string typeName, string cookieVar)
        => GetTryParseCall(typeName, cookieVar) is { } tryParse
            ? $"({tryParse} ? {cookieVar}Parsed : default)"
            : $"{cookieVar}!";

    private static string Local(CookieParameterModel cookie) => $"{ToCamelCase(cookie.PropertyName)}Cookie";

    private static string GetParseExpression(string typeName, string cookieVar)
    {
        var cleanType = typeName
            .Replace("global::", "")
            .TrimEnd('?');

        // TryParse avoids FormatException on malformed cookie values.
        var parseExpr = cleanType switch
        {
            "System.Guid" => $"(System.Guid.TryParse({cookieVar}, out var {cookieVar}Parsed) ? {cookieVar}Parsed : default)",
            "int" or "System.Int32" => $"(int.TryParse({cookieVar}, out var {cookieVar}Parsed) ? {cookieVar}Parsed : default)",
            "long" or "System.Int64" => $"(long.TryParse({cookieVar}, out var {cookieVar}Parsed) ? {cookieVar}Parsed : default)",
            "bool" or "System.Boolean" => $"(bool.TryParse({cookieVar}, out var {cookieVar}Parsed) ? {cookieVar}Parsed : default)",
            "System.DateTimeOffset" => $"(System.DateTimeOffset.TryParse({cookieVar}, out var {cookieVar}Parsed) ? {cookieVar}Parsed : default)",
            _ => $"{cookieVar}!"
        };

        return $"{cookieVar} is not null ? {parseExpr} : default";
    }

    /// <summary>
    ///     Returns a <c>Type.TryParse(cookieVar, out var cookieVarParsed)</c> expression for the
    ///     supported typed cookies, or <c>null</c> for string-like/unknown types that need no parsing.
    /// </summary>
    private static string? GetTryParseCall(string typeName, string cookieVar)
    {
        var cleanType = typeName
            .Replace("global::", "")
            .TrimEnd('?');

        var parsedVar = $"{cookieVar}Parsed";

        return cleanType switch
        {
            "System.Guid" => $"System.Guid.TryParse({cookieVar}, out var {parsedVar})",
            "int" or "System.Int32" => $"int.TryParse({cookieVar}, out var {parsedVar})",
            "long" or "System.Int64" => $"long.TryParse({cookieVar}, out var {parsedVar})",
            "bool" or "System.Boolean" => $"bool.TryParse({cookieVar}, out var {parsedVar})",
            "System.DateTimeOffset" => $"System.DateTimeOffset.TryParse({cookieVar}, out var {parsedVar})",
            _ => null
        };
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
