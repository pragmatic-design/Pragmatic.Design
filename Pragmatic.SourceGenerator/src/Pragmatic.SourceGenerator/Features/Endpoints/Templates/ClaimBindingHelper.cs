using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates claim extraction code for handler bodies. Claims are read from HttpContext.User since
///     ASP.NET Core Minimal APIs don't support [FromClaim] binding natively.
/// </summary>
/// <remarks>
///     Three parts, emitted in three places, because the operation is built with an object initializer:
///     the reads and their 401s before the construction, required values and optional <c>init</c> ones
///     inside it, optional <c>set</c> ones after it. ⚠️ Emitting all of it after the construction would
///     make a <c>required</c> claim CS9035 and an <c>init</c> one CS8852 in a generated file — the same
///     shape <see cref="RequestValueBinding" /> follows for header and query.
/// </remarks>
internal static class ClaimBindingHelper
{
    /// <summary>The reads, and the 401 for a required claim that is missing or malformed.</summary>
    public static List<string> ReadLines(EndpointModel model)
    {
        var lines = new List<string>();

        if (model.ClaimParameters.IsDefaultOrEmpty)
            return lines;

        lines.Add("// Read claim parameters from the authenticated user");

        foreach (var claim in model.ClaimParameters)
        {
            var claimVar = Local(claim);

            lines.Add($"var {claimVar} = httpContext.User.FindFirst(\"{claim.ClaimType}\")?.Value;");

            if (!claim.IsRequired)
                continue;

            lines.Add($"if ({claimVar} is null)");
            lines.Add(
                $"    return Microsoft.AspNetCore.Http.Results.Problem(\"Missing required claim: {claim.ClaimType}\", statusCode: 401);");

            // A present-but-malformed required claim must be rejected, not coerced to a
            // trusted-looking default (Guid.Empty/0/false). The out variable is what the initializer reads.
            if (claim.NeedsConversion && GetTryParseCall(claim.TypeName, claimVar) is { } tryParse)
            {
                lines.Add($"if (!{tryParse})");
                lines.Add(
                    $"    return Microsoft.AspNetCore.Http.Results.Problem(\"Invalid required claim: {claim.ClaimType}\", statusCode: 401);");
            }
        }

        lines.Add("");
        return lines;
    }

    /// <summary>The initializer entries: required claims, and optional claims on <c>init</c> properties.</summary>
    public static IEnumerable<string> InitializerEntries(EndpointModel model)
    {
        if (model.ClaimParameters.IsDefaultOrEmpty)
            yield break;

        foreach (var claim in model.ClaimParameters)
        {
            var claimVar = Local(claim);

            if (claim.IsRequired)
            {
                yield return $"{claim.PropertyName} = {RequiredValue(claim, claimVar)}";
                continue;
            }

            if (claim is not { IsInitOnly: true, InitOnlyFallback: { } fallback })
                continue;

            yield return claim.NeedsConversion
                ? $"{claim.PropertyName} = {claimVar} is not null ? {GetParseExpression(claim.TypeName, claimVar, isRequired: true)} : {fallback}"
                : $"{claim.PropertyName} = {claimVar} ?? {fallback}";
        }
    }

    /// <summary>The assignments after construction: optional claims on <c>set</c> properties.</summary>
    public static IEnumerable<string> PostConstructionLines(EndpointModel model, string targetVariable)
    {
        if (model.ClaimParameters.IsDefaultOrEmpty)
            yield break;

        foreach (var claim in model.ClaimParameters.Where(c => c is { IsRequired: false, IsInitOnly: false }))
        {
            var claimVar = Local(claim);

            yield return claim.NeedsConversion
                ? $"{targetVariable}.{claim.PropertyName} = {GetParseExpression(claim.TypeName, claimVar, false)};"
                : $"if ({claimVar} is not null) {targetVariable}.{claim.PropertyName} = {claimVar};";
        }
    }

    private static string RequiredValue(ClaimParameterModel claim, string claimVar)
    {
        if (!claim.NeedsConversion)
            return claimVar;

        // The read declared `{claimVar}Parsed` in its TryParse; a type with no parse is taken as it came.
        return GetTryParseCall(claim.TypeName, claimVar) is not null ? $"{claimVar}Parsed" : $"{claimVar}!";
    }

    private static string Local(ClaimParameterModel claim) => $"{ToCamelCase(claim.PropertyName)}Claim";

    private static string GetParseExpression(string typeName, string claimVar, bool isRequired)
    {
        // Strip global:: prefix and nullable suffix for matching
        var cleanType = typeName
            .Replace("global::", "")
            .TrimEnd('?');

        // Use TryParse to avoid FormatException on malformed claim values.
        var parseExpr = cleanType switch
        {
            "System.Guid" => $"(System.Guid.TryParse({claimVar}, out var {claimVar}Parsed) ? {claimVar}Parsed : default)",
            "int" or "System.Int32" => $"(int.TryParse({claimVar}, out var {claimVar}Parsed) ? {claimVar}Parsed : default)",
            "long" or "System.Int64" => $"(long.TryParse({claimVar}, out var {claimVar}Parsed) ? {claimVar}Parsed : default)",
            "bool" or "System.Boolean" => $"(bool.TryParse({claimVar}, out var {claimVar}Parsed) ? {claimVar}Parsed : default)",
            "System.DateTimeOffset" => $"(System.DateTimeOffset.TryParse({claimVar}, out var {claimVar}Parsed) ? {claimVar}Parsed : default)",
            _ => $"{claimVar}!" // Fallback: direct assignment with null-forgiving
        };

        // For optional claims with conversion, wrap in null check
        if (!isRequired)
            parseExpr = $"{claimVar} is not null ? {parseExpr} : default";

        return parseExpr;
    }

    /// <summary>
    ///     Returns a <c>Type.TryParse(claimVar, out var claimVarParsed)</c> expression for the
    ///     supported typed claims, or <c>null</c> for string-like/unknown types that need no parsing.
    /// </summary>
    private static string? GetTryParseCall(string typeName, string claimVar)
    {
        var cleanType = typeName
            .Replace("global::", "")
            .TrimEnd('?');

        var parsedVar = $"{claimVar}Parsed";

        return cleanType switch
        {
            "System.Guid" => $"System.Guid.TryParse({claimVar}, out var {parsedVar})",
            "int" or "System.Int32" => $"int.TryParse({claimVar}, out var {parsedVar})",
            "long" or "System.Int64" => $"long.TryParse({claimVar}, out var {parsedVar})",
            "bool" or "System.Boolean" => $"bool.TryParse({claimVar}, out var {parsedVar})",
            "System.DateTimeOffset" => $"System.DateTimeOffset.TryParse({claimVar}, out var {parsedVar})",
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
