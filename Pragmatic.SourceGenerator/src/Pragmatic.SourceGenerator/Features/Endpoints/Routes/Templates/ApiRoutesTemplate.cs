using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGenerator.Features.Endpoints.Routes.Templates;

/// <summary>
///     Generates the per-assembly <c>ApiRoutes</c> class: compile-time route constants and
///     typed URL builders per endpoint, grouped by boundary. Consumed by the Pragmatic.Testing
///     source generator (typed test client) and usable directly for link generation.
/// </summary>
internal sealed class ApiRoutesTemplate : CSharpTemplate
{
    private static readonly Regex RouteTokenRegex = new(@"\{(\w+)(?::[^}]*)?\??\}", RegexOptions.Compiled);

    private readonly ImmutableArray<EndpointModel> _endpoints;
    private readonly string _namespace;

    public ApiRoutesTemplate(ImmutableArray<EndpointModel> endpoints, string assemblyName)
    {
        _endpoints = endpoints;
        _namespace = assemblyName.Replace("-", "_");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";

    public override Artifact RenderOutput()
    {
        var hintPrefix = NamespacePrefixHelper.DerivePrefix(new[] { _namespace });
        return new Artifact(
            VirtualFolderHints.ForAssembly("Endpoints", "Routes"),
            ToSourceText());
    }

    protected override bool Validate() => _endpoints.Any(e => e.IsValid);

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();

        AppendLine("/// <summary>");
        AppendLine("///     Compile-time route constants and typed URL builders for every endpoint in this");
        AppendLine("///     assembly, grouped by boundary. Used by the typed test client and link generation.");
        AppendLine("/// </summary>");
        AppendLine("public static class ApiRoutes");
        AppendLine("{");
        IncreaseIndent();

        var boundaryGroups = _endpoints
            .Where(e => e.IsValid)
            .GroupBy(e => EndpointRouteFacts.Boundary(e) ?? "Api")
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var firstBoundary = true;
        foreach (var group in boundaryGroups)
        {
            if (!firstBoundary) AppendLine();
            firstBoundary = false;

            AppendLine($"/// <summary>Routes for the {group.Key} boundary.</summary>");
            AppendLine($"public static class {SanitizeIdentifier(group.Key)}");
            AppendLine("{");
            IncreaseIndent();

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var firstEntry = true;
            foreach (var ep in group.OrderBy(e => e.TypeName, StringComparer.Ordinal))
            {
                var name = SanitizeIdentifier(EndpointRouteFacts.OperationName(ep));
                if (!seenNames.Add(name))
                    continue; // collision — PRAG0526 reported by the feature

                if (!firstEntry) AppendLine();
                firstEntry = false;
                RenderEntry(ep, name);
            }

            DecreaseIndent();
            AppendLine("}");
        }

        AppendLine();
        RenderFormatHelper();

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderEntry(EndpointModel ep, string name)
    {
        var fullRoute = EndpointRouteFacts.FullRoute(ep);

        AppendLine($"/// <summary>{ep.HttpMethod.ToUpperInvariant()} {StringHelper.CSharpLiteral(fullRoute)}</summary>");
        AppendLine($"public const string {name}Method = \"{ep.HttpMethod.ToUpperInvariant()}\";");
        AppendLine();
        AppendLine($"/// <summary>Route template for {name}.</summary>");
        AppendLine($"public const string {name}Template = \"{StringHelper.CSharpLiteral(fullRoute)}\";");
        AppendLine();

        RenderBuilder(ep, name, fullRoute);
    }

    private void RenderBuilder(EndpointModel ep, string name, string fullRoute)
    {
        // Route params (required) + query params (required first, then optional)
        var parameters = new List<string>();
        foreach (var routeParam in ep.RouteParameters)
            parameters.Add($"{routeParam.TypeName} {ToCamelCase(routeParam.PropertyName)}");

        var queryParams = ep.QueryParameters.AsImmutableArray();
        foreach (var queryParam in queryParams.Where(q => q.IsRequired))
            parameters.Add($"{queryParam.TypeName} {ToCamelCase(queryParam.PropertyName)}");
        foreach (var queryParam in queryParams.Where(q => !q.IsRequired))
        {
            var nullableSuffix = queryParam.TypeName.EndsWith("?") ? "" : "?";
            parameters.Add($"{queryParam.TypeName}{nullableSuffix} {ToCamelCase(queryParam.PropertyName)} = null");
        }

        AppendLine($"/// <summary>Builds the {name} URL from typed parameters.</summary>");
        AppendLine($"public static string {name}({string.Join(", ", parameters)})");
        AppendLine("{");
        IncreaseIndent();

        // Route: replace each {token} with the escaped parameter value
        var routeExpr = "$\"" + RouteTokenRegex.Replace(
            fullRoute.Replace("\"", "\\\""),
            match =>
            {
                var paramName = match.Groups[1].Value;
                var routeParam = ep.RouteParameters.AsImmutableArray()
                    .FirstOrDefault(p => string.Equals(p.PropertyName, paramName, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase));
                return routeParam is not null
                    ? $"{{__Fmt({ToCamelCase(routeParam.PropertyName)})}}"
                    : match.Value; // unmatched token stays literal (PRAG0504 already warned)
            }) + "\"";
        AppendLine($"var __url = {routeExpr};");

        if (queryParams.Length > 0)
        {
            AppendLine("var __query = new System.Collections.Generic.List<string>();");
            foreach (var queryParam in queryParams)
            {
                var varName = ToCamelCase(queryParam.PropertyName);
                if (queryParam.IsRequired)
                    AppendLine($"__query.Add(\"{queryParam.Name}=\" + __Fmt({varName}));");
                else
                    AppendLine($"if ({varName} is not null) __query.Add(\"{queryParam.Name}=\" + __Fmt({varName}));");
            }

            AppendLine("return __query.Count > 0 ? __url + \"?\" + string.Join(\"&\", __query) : __url;");
        }
        else
        {
            AppendLine("return __url;");
        }

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderFormatHelper()
    {
        AppendLine("/// <summary>Invariant-culture, URL-escaped value formatting for route/query values.</summary>");
        AppendLine("private static string __Fmt<T>(T value)");
        AppendLine("    => System.Uri.EscapeDataString(string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}\", value));");
    }

    private static string SanitizeIdentifier(string value)
    {
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
        var identifier = new string(chars);
        if (identifier.Length == 0) return "Api";
        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
