using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     Describes the generated endpoints to ASP.NET's API explorer, which the runtime OpenAPI document is
///     built from.
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET's own provider describes a minimal-API endpoint from the <see cref="MethodInfo" /> of its
///         handler, and skips one that has none. A generated endpoint has none by design: it is mapped as
///         a <c>RequestDelegate</c> so that it survives an AOT publish. So the runtime document described
///         the application's hand-written endpoints and none of Pragmatic's.
///     </para>
///     <para>
///         What a <see cref="MethodInfo" /> would have told the explorer, the generator already knew and
///         attached to the endpoint: <see cref="PragmaticRequestDescription" /> for what the request binds,
///         <see cref="IAcceptsMetadata" /> for the body's content types, and
///         <see cref="IProducesResponseTypeMetadata" /> for the responses. This reads those, and does not
///         look at the handler.
///     </para>
///     <para>
///         A type whose schema the document cannot hold is described without it, and logged — see
///         <see cref="SchemaDepthGuard" />. The compile-time document describes it in full.
///     </para>
/// </remarks>
public sealed partial class PragmaticApiDescriptionProvider(
    EndpointDataSource endpoints,
    IHostEnvironment environment,
    IOptions<JsonOptions> json,
    ILogger<PragmaticApiDescriptionProvider> logger) : IApiDescriptionProvider
{
    private readonly SchemaDepthGuard _guard = new(json.Value.SerializerOptions);

    // The explorer describes again whenever the document is built; the warning is worth one line per
    // endpoint and type, not one per build.
    private readonly ConcurrentDictionary<(string?, Type), bool> _reported = new();

    /// <summary>The order of ASP.NET's own endpoint provider, which this one stands beside.</summary>
    public int Order => -1100;

    /// <inheritdoc />
    public void OnProvidersExecuting(ApiDescriptionProviderContext context)
    {
        foreach (var endpoint in endpoints.Endpoints)
        {
            if (endpoint is not RouteEndpoint route
                || route.Metadata.GetMetadata<PragmaticRequestDescription>() is not { } request)
                continue;

            // A handler ASP.NET can see is one it describes itself; describing it here as well would put
            // the operation in the document twice.
            if (route.Metadata.GetMetadata<MethodInfo>() is not null)
                continue;

            if (route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>() is { ExcludeFromDescription: true })
                continue;

            if (route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is not { Count: > 0 } methods)
                continue;

            foreach (var method in methods)
                context.Results.Add(Describe(route, method, request));
        }
    }

    /// <inheritdoc />
    public void OnProvidersExecuted(ApiDescriptionProviderContext context)
    {
        // Nothing to add after the other providers: every description is complete when it is created.
    }

    private ApiDescription Describe(RouteEndpoint route, string method, PragmaticRequestDescription request)
    {
        var description = new ApiDescription
        {
            HttpMethod = method,
            GroupName = route.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName,
            RelativePath = route.RoutePattern.RawText?.TrimStart('/'),
            ActionDescriptor = new ActionDescriptor
            {
                DisplayName = route.DisplayName,
                // The document reads this with an indexer when the endpoint carries no tags, so it has to
                // be present. The application's name is what ASP.NET uses for a handler with no type.
                RouteValues = { ["controller"] = environment.ApplicationName },
                EndpointMetadata = [.. route.Metadata],
            },
        };

        foreach (var parameter in request.Parameters)
            description.ParameterDescriptions.Add(Describe(route, parameter));

        if (route.Metadata.GetMetadata<IAcceptsMetadata>() is { } accepts)
            foreach (var contentType in accepts.ContentTypes)
                description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = contentType });

        foreach (var response in Responses(route))
            description.SupportedResponseTypes.Add(response);

        return description;
    }

    /// <summary>One response per status code, the last declaration of each winning.</summary>
    /// <remarks>
    ///     The document adds responses by status with <c>Dictionary.Add</c>, so a second declaration of
    ///     the same status fails the whole document instead of replacing the first.
    /// </remarks>
    private IEnumerable<ApiResponseType> Responses(RouteEndpoint route)
    {
        var byStatus = new Dictionary<int, ApiResponseType>();

        foreach (var produces in route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>())
        {
            var response = new ApiResponseType { StatusCode = produces.StatusCode, Type = Describable(route, produces.Type) };

            var contentTypes = produces.ContentTypes.Any()
                ? produces.ContentTypes
                : produces.Type is null ? [] : ["application/json"];

            foreach (var contentType in contentTypes)
                response.ApiResponseFormats.Add(new ApiResponseFormat { MediaType = contentType });

            byStatus[produces.StatusCode] = response;
        }

        return byStatus.Values;
    }

    private ApiParameterDescription Describe(RouteEndpoint route, PragmaticParameterDescription parameter)
    {
        // A body the document cannot hold is still a JSON body: described as any JSON, rather than as
        // nothing, which would read as an operation that takes no body at all.
        var type = parameter.Source == PragmaticParameterSource.Body
            ? Describable(route, parameter.Type) ?? typeof(JsonElement)
            : parameter.Type;

        return new ApiParameterDescription
        {
            Name = parameter.Name,
            Source = parameter.Source switch
            {
                PragmaticParameterSource.Route => BindingSource.Path,
                PragmaticParameterSource.Query => BindingSource.Query,
                PragmaticParameterSource.Header => BindingSource.Header,
                PragmaticParameterSource.Form => BindingSource.Form,
                PragmaticParameterSource.FormFile => BindingSource.FormFile,
                PragmaticParameterSource.Body => BindingSource.Body,
                _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter.Source, "Unknown parameter source."),
            },
            Type = type,
            IsRequired = parameter.IsRequired,
            ModelMetadata = new PragmaticModelMetadata(ModelMetadataIdentity.ForType(type)),
            // The document groups form fields by this name, and reads it without a null check.
            ParameterDescriptor = new ParameterDescriptor { Name = parameter.Name, ParameterType = type },
        };
    }

    /// <summary>The type, or <c>null</c> when the document cannot hold its schema.</summary>
    private Type? Describable(RouteEndpoint route, Type? type)
    {
        if (type is null || _guard.Fits(type, out var refusal))
            return type;

        if (_reported.TryAdd((route.DisplayName, type), true))
            LogSchemaLeftOut(logger, route.DisplayName, type.FullName ?? type.Name, refusal);

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The runtime OpenAPI document describes {Endpoint} without the schema of {Type}: {Refusal}. "
                  + "The compile-time document (MapPragmaticOpenApi) describes it in full; a response shaped "
                  + "as a DTO instead of an entity keeps it in both.")]
    private static partial void LogSchemaLeftOut(ILogger logger, string? endpoint, string type, string? refusal);
}
