using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Renders the response/request metadata that <c>Produces</c> and <c>Accepts</c> would attach.
/// </summary>
/// <remarks>
///     Those two are extensions on <c>RouteHandlerBuilder</c>, which a generated endpoint does not
///     have: it is mapped as a <c>RequestDelegate</c> so that it survives an AOT publish, and that
///     overload returns the plainer <c>IEndpointConventionBuilder</c>. The metadata they attach is
///     still attachable — <c>WithMetadata</c> is defined on the plainer type — so the declarations
///     are kept intact.
/// </remarks>
internal static class EndpointMetadataRenderer
{
    private const string Produces = "global::Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata";
    private const string Accepts = "global::Pragmatic.Endpoints.Binding.PragmaticAcceptsMetadata";
    private const string ProblemDetails = "global::Microsoft.AspNetCore.Mvc.ProblemDetails";
    private const string RequestDescription = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticRequestDescription";
    private const string ParameterDescription = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticParameterDescription";
    private const string ParameterSource = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticParameterSource";

    /// <summary>A response of <paramref name="typeExpr" /> at <paramref name="statusCode" />.</summary>
    public static string RenderProduces(string builderVar, string typeExpr, int statusCode, string? contentType = null)
    {
        var types = contentType is null ? "" : $", new[] {{ \"{contentType}\" }}";
        return $"{builderVar}.WithMetadata(new {Produces}({statusCode}, typeof({typeExpr}){types}));";
    }

    /// <summary>
    ///     The marker that exempts a route from requiring a tenant.
    /// </summary>
    /// <remarks>
    ///     Here, not spelled out in each of the four handler configurations, so the four cannot end up
    ///     attaching four slightly different things. ⚠️ Until <c>[TenantAgnostic]</c> existed this
    ///     marker was attached from exactly one place inside the framework and no attribute declared
    ///     it, so a module could not ask for it and an anonymous probe was refused with 400.
    /// </remarks>
    public static string RenderTenantAgnostic(string builderVar)
        => $"{builderVar}.WithMetadata(global::Pragmatic.MultiTenancy.TenantAgnosticEndpoint.Instance);";

    /// <summary>A response with no body at <paramref name="statusCode" />.</summary>
    public static string RenderProducesEmpty(string builderVar, int statusCode)
        => $"{builderVar}.WithMetadata(new {Produces}({statusCode}));";

    /// <summary>A problem response at <paramref name="statusCode" />.</summary>
    public static string RenderProducesProblem(string builderVar, int statusCode)
        => $"{builderVar}.WithMetadata(new {Produces}({statusCode}, typeof({ProblemDetails}), " +
           "new[] { \"application/problem+json\" }));";

    /// <summary>The request content type the endpoint accepts.</summary>
    public static string RenderAccepts(string builderVar, string typeExpr, string contentType)
        => $"{builderVar}.WithMetadata(new {Accepts}(new[] {{ \"{contentType}\" }}, typeof({typeExpr})));";

    /// <summary>
    ///     Every value the endpoint binds from the request, for the API explorer.
    /// </summary>
    /// <remarks>
    ///     Built from the same list the binding is rendered from, so the description and the binding
    ///     cannot disagree about a name, a place or a type. Services, the context and the cancellation
    ///     token are not part of the request and are left out.
    /// </remarks>
    /// <param name="builderVar">The endpoint builder the metadata is attached to.</param>
    /// <param name="parameters">The bound parameters, as the binding is rendered from them.</param>
    /// <param name="requiredByValidation">
    ///     The values validation requires though the binding does not, as <see cref="RequiredByValidation" />
    ///     keys them: described as required, bound as before.
    /// </param>
    public static string RenderRequestDescription(
        string builderVar,
        IReadOnlyList<BoundParameter> parameters,
        IReadOnlyCollection<string>? requiredByValidation = null)
    {
        var described = parameters
            .Select(p => RenderParameterDescription(p, requiredByValidation))
            .Where(static d => d is not null);
        return $"{builderVar}.WithMetadata(new {RequestDescription}({string.Join(", ", described)}));";
    }

    /// <summary>
    ///     The query values, headers and form fields carrying Pragmatic.Validation's <c>[Required]</c>,
    ///     keyed by where they are bound and the key the binding reads.
    /// </summary>
    public static HashSet<string> RequiredByValidation(Models.EndpointModel model)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in model.QueryParameters.Where(static p => p.IsRequiredByValidation))
            keys.Add(Key("Query", p.Name));
        foreach (var p in model.HeaderParameters.Where(static p => p.IsRequiredByValidation))
            keys.Add(Key("Header", p.HeaderName));
        // The key a form field is bound by: its declared name, or the property name.
        foreach (var p in model.FormParameters.Where(static p => p.IsRequiredByValidation))
            keys.Add(Key("Form", p.Name));
        return keys;
    }

    private static string Key(string source, string name) => $"{source}:{name}";

    private static string? RenderParameterDescription(
        BoundParameter parameter, IReadOnlyCollection<string>? requiredByValidation)
    {
        var source = parameter.Source switch
        {
            BindingSource.Route => "Route",
            BindingSource.Query or BindingSource.QueryMany => "Query",
            BindingSource.Header => "Header",
            BindingSource.Form => "Form",
            BindingSource.FormFile => "FormFile",
            BindingSource.Body => "Body",
            _ => null,
        };

        if (source is null)
            return null;

        // The key on the wire, which is what a caller writes; the local is the generator's own name.
        var name = parameter.SourceName ?? parameter.Name;

        // typeof cannot name a nullable reference type, and requiredness is carried separately.
        var type = parameter.TypeName.EndsWith("?")
            ? parameter.TypeName.Substring(0, parameter.TypeName.Length - 1)
            : parameter.TypeName;

        var isRequired = parameter.IsRequired
                         || requiredByValidation?.Contains(Key(source, name)) == true;

        return $"new {ParameterDescription}(\"{StringHelper.CSharpLiteral(name)}\", {ParameterSource}.{source}, " +
               $"typeof({type}), {(isRequired ? "true" : "false")})";
    }
}
