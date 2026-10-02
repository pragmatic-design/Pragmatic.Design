using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates assembly-level <c>[PragmaticEndpointContract]</c> attributes — one per endpoint.
///     This exposes the full contract (route, method, error types, permissions, status codes)
///     for host-SG consumption (OpenAPI generation, gateway config, client SDK generation).
/// </summary>
internal sealed class EndpointContractTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EndpointModel> _endpoints;
    private readonly string _hostNamespace;

    public EndpointContractTemplate(ImmutableArray<EndpointModel> endpoints)
    {
        _endpoints = endpoints;
        _hostNamespace = DeriveHostNamespace(endpoints);
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";

    public override Artifact RenderOutput()
    {
        var hintPrefix = NamespacePrefixHelper.DerivePrefix(new[] { _hostNamespace });
        return new Artifact(
            VirtualFolderHints.ForAssembly("Endpoints", "EndpointContracts"),
            ToSourceText());
    }

    protected override bool Validate() => !_endpoints.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Endpoints.Metadata");

        foreach (var endpoint in _endpoints.OrderBy(e => e.Namespace).ThenBy(e => e.TypeName))
            RenderEndpointContract(endpoint);

        RenderErrorRegistry();
    }

    /// <summary>
    ///     Every distinct error this assembly's endpoints declare, in one place.
    /// </summary>
    /// <remarks>
    ///     Permissions have a registry, and this is the errors' equivalent: without it the only way to
    ///     answer "which failures can this boundary produce" is to read every endpoint. Arrays of
    ///     <c>Type</c> and status code rather than a new public record, which is the shape the
    ///     contracts above already use.
    ///     <para>
    ///         Only what the endpoints <em>declare</em> appears. An action on the untyped
    ///         <c>DomainAction&lt;TReturn&gt;</c> contributes nothing, and neither does a rule raised
    ///         as <c>BusinessRuleError.Create("…")</c> — every one of those is the same type, so
    ///         there is nothing to distinguish. That is the cost of the untyped path, made visible.
    ///     </para>
    /// </remarks>
    private void RenderErrorRegistry()
    {
        var errors = _endpoints
            .SelectMany(e => e.ErrorTypes.IsDefaultOrEmpty
                ? Enumerable.Empty<ErrorTypeModel>()
                : e.ErrorTypes.AsEnumerable())
            .GroupBy(e => e.TypeName, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.TypeName, StringComparer.Ordinal)
            .ToList();

        if (errors.Count == 0)
            return;

        AppendLine();
        XmlSummary("Every error type the endpoints in this assembly declare they can return.");
        Class("ErrorRegistry", () =>
        {
            XmlSummary("The declared error types, ordered by name.");
            ExpressionProperty("All", "global::System.Type[]",
                $"[{string.Join(", ", errors.Select(e => $"typeof({e.TypeName})"))}]",
                isStatic: true);

            XmlSummary("The HTTP status code of each entry in All, at the same index.");
            ExpressionProperty("StatusCodes", "int[]",
                $"[{string.Join(", ", errors.Select(e => e.StatusCode))}]",
                isStatic: true);
        },
        modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderEndpointContract(EndpointModel model)
    {
        // Collect attribute properties as strings (no trailing comma — handled at emit time)
        var props = new List<string>();

        props.Add($"EndpointType = typeof({model.FullTypeName})");
        props.Add($"HttpMethod = \"{model.HttpMethod.ToUpperInvariant()}\"");
        props.Add($"Route = \"{EscapeString(model.Route)}\"");

        if (!model.IsVoid && !string.IsNullOrEmpty(model.ResponseType))
            props.Add($"ResponseType = typeof({model.ResponseType})");

        if (model.IsVoid)
            props.Add("IsVoid = true");

        props.Add($"SuccessStatusCode = {model.ComputedSuccessStatusCode}");

        // Error types + parallel status codes
        var errorTypes = model.ErrorTypes;
        if (errorTypes is { IsDefaultOrEmpty: false, Length: > 0 })
        {
            var typesList = string.Join(", ", errorTypes.Select(e => $"typeof({e.TypeName})"));
            props.Add($"ErrorTypes = new System.Type[] {{ {typesList} }}");

            var codesList = string.Join(", ", errorTypes.Select(e => e.StatusCode.ToString()));
            props.Add($"ErrorStatusCodes = new int[] {{ {codesList} }}");
        }
        else
        {
            props.Add("ErrorTypes = new System.Type[0]");
            props.Add("ErrorStatusCodes = new int[0]");
        }

        // Tags
        var tags = model.Tags;
        if (tags is { IsDefaultOrEmpty: false, Length: > 0 })
        {
            var tagsList = string.Join(", ", tags.Select(t => $"\"{EscapeString(t)}\""));
            props.Add($"Tags = new string[] {{ {tagsList} }}");
        }

        // Permissions — merge RequiredPermissions + AnyPermissions
        var permissions = ImmutableArray<string>.Empty;
        if (model.Authorization is { } auth)
            permissions = auth.RequiredPermissions.AsImmutableArray().AddRange(auth.AnyPermissions.AsImmutableArray());

        if (permissions is { IsDefaultOrEmpty: false, Length: > 0 })
        {
            var permList = string.Join(", ", permissions.Select(p => $"\"{EscapeString(p)}\""));
            props.Add($"RequiredPermissions = new string[] {{ {permList} }}");
        }

        // Correlation with ApiRoutes (typed test client): full route, operation name, boundary
        props.Add($"FullRoute = \"{EscapeString(Routes.EndpointRouteFacts.FullRoute(model))}\"");
        props.Add($"OperationName = \"{EscapeString(Routes.EndpointRouteFacts.OperationName(model))}\"");
        if (Routes.EndpointRouteFacts.Boundary(model) is { } boundary)
            props.Add($"Boundary = \"{EscapeString(boundary)}\"");

        // JSON body flag for the typed client (a typeof of the body DTO would be fragile
        // for versioned endpoints, whose DTO names carry version suffixes)
        if (model is { HasFormParams: false } && (model.NeedsBodyDto || model.HasDirectBodyParam))
            props.Add("HasBody = true");

        if (model.QueryIsPaged)
            props.Add("IsPaged = true");

        if (model.IsStreamingResponse)
            props.Add("IsStreaming = true");

        // Summary — optional, last
        if (!string.IsNullOrEmpty(model.Summary))
            props.Add($"Summary = \"{EscapeString(model.Summary!)}\"");

        // Emit attribute
        AppendLine("[assembly: PragmaticEndpointContract(");
        IncreaseIndent();

        for (var i = 0; i < props.Count; i++)
        {
            var comma = i < props.Count - 1 ? "," : "";
            AppendLine($"{props[i]}{comma}");
        }

        DecreaseIndent();
        AppendLine(")]");
    }

    private static string EscapeString(string? value) => StringHelper.CSharpLiteral(value);

    private static string DeriveHostNamespace(ImmutableArray<EndpointModel> endpoints)
    {
        if (endpoints.IsDefaultOrEmpty)
            return string.Empty;

        var namespaces = endpoints
            .Select(e => e.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        if (namespaces.Count == 0)
            return string.Empty;

        var first = namespaces[0];
        var parts = first.Split('.');
        var apiSuffixes = new[] { "Api", "Host", "Web", "Server" };

        for (var i = 0; i < parts.Length; i++)
            if (apiSuffixes.Any(suffix => parts[i].EndsWith(suffix)))
                return string.Join(".", parts.Take(i + 1));

        // Fallback: strip Endpoints segment and trailing
        var prefix = parts.TakeWhile(p => p != "Endpoints").ToArray();
        return prefix.Length > 0 ? string.Join(".", prefix) : parts[0];
    }
}
