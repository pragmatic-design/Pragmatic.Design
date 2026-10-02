using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates the endpoint handler for Mutation-based endpoints.
///     Uses IMutationInvoker for the mutation pipeline (validate → load/create → apply → persist → events).
/// </summary>
internal sealed partial class MutationHandlerTemplate : CSharpTemplate
{
    private readonly EndpointModel _model;

    public MutationHandlerTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] on Mutation {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("Endpoint"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, IsMutation: true };
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Http");
        AddUsing("Microsoft.AspNetCore.Routing");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Result");
        AddUsing("Pragmatic.Actions");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderClassBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Maps this Mutation endpoint to the route builder.");
        XmlParam("endpoints", "The endpoint route builder.");
        XmlReturns("The route handler builder for further configuration.");

        Method("MapEndpoint", RenderMapEndpointBody,
            "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder",
            new List<MethodParameter>
            {
                new("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "endpoints")
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });

        // ReturnType = Id: the boundary member returns the bare key, and the wire carries it as
        // {"id": …}. A named record, because the generated JSON context cannot name an anonymous one.
        if (_model is { MutationKeyResponseType: not null, MutationReturnsId: true })
        {
            AppendLine();
            XmlSummary("What this endpoint answers with: the key of the row the mutation wrote.");
            AppendLine($"public sealed record {Transforms.EndpointTransform.IdResponseRecordName}");
            Block(() => AppendLine("public required global::System.Guid Id { get; init; }"));
        }
    }

    private void RenderMapEndpointBody()
    {
        var parameters = BuildBoundParameters();

        // The handler keeps the shape it always had. What changed is who calls it: ASP.NET cannot bind
        // a generated handler under AOT, so the RequestDelegate below binds and invokes it.
        AppendLine("var handler = async (");
        RequestDelegateRenderer.RenderSignature(parameters, AppendLine);
        AppendLine(") =>");

        // Handler body
        Block(() =>
        {
            // [WithoutFilter<T>] / [FilterMode] — disable specific filters
            if (_model.HasFilterOverrides)
            {
                RenderFilterOverrideScopes();
                AppendLine();
            }

            // Claims and cookies are read, and refused, before the mutation is built
            foreach (var line in RequestValueBinding.ReadLines(_model))
                AppendLine(line);

            // Create mutation instance with object initializer
            RenderActionInstantiation();

            // Optional values on set properties; the rest are in the initializer above.
            var postConstruction = RequestValueBinding.PostConstructionLines(_model, "action").ToList();
            foreach (var line in postConstruction)
                AppendLine(line);

            if (postConstruction.Count > 0)
                AppendLine();

            // Build EndpointContext for pre/post processors
            if (!_model.PreProcessors.IsDefaultOrEmpty || !_model.PostProcessors.IsDefaultOrEmpty)
            {
                var endpointName = _model.Summary ?? _model.TypeName;
                AppendLine(
                    $"var endpointContext = new global::Pragmatic.Endpoints.Context.EndpointContext(httpContext, \"{endpointName}\", action);");
                AppendLine();
            }

            // Execute pre-processors
            if (!_model.PreProcessors.IsDefaultOrEmpty)
                foreach (var processor in _model.PreProcessors.OrderBy(p => p.Order))
                {
                    AppendLine(
                        $"var preProcessor{processor.Order} = httpContext.RequestServices.GetRequiredService<{processor.TypeName}>();");
                    AppendLine(
                        $"var preResult{processor.Order} = await preProcessor{processor.Order}.ProcessAsync(endpointContext, ct);");
                    AppendLine($"if (!preResult{processor.Order}.ShouldContinue)");
                    Block(() =>
                    {
                        AppendLine($"return preResult{processor.Order}.Error is not null");
                        AppendLine(
                            $"    ? global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(preResult{processor.Order}.Error, httpContext)");
                        AppendLine("    : Microsoft.AspNetCore.Http.Results.BadRequest();");
                    });
                    AppendLine();
                }

            // No inline ISyncValidator call here: MutationInvoker runs L1 validation in its own
            // pipeline — validating inline too would run every sync validator twice per request.

            // Call InvokeAsync on the mutation invoker
            AppendLine("var result = await invoker.InvokeAsync(action, ct);");
            AppendLine();

            // Execute post-processors
            if (!_model.PostProcessors.IsDefaultOrEmpty)
            {
                foreach (var processor in _model.PostProcessors.OrderBy(p => p.Order))
                {
                    AppendLine(
                        $"var postProcessor{processor.Order} = httpContext.RequestServices.GetRequiredService<{processor.TypeName}>();");
                    AppendLine($"await postProcessor{processor.Order}.ProcessAsync(endpointContext, result, ct);");
                }

                AppendLine();
            }

            // Handle result — mutations always return entity
            RenderResultHandling();
        });

        AppendLine(";");
        AppendLine();

        AppendLine($"var builder = endpoints.{MapInvocationHelper.Render(_model.HttpMethod, StringHelper.CSharpLiteral(_model.Route))}(Microsoft.AspNetCore.Http.RequestDelegate)(async httpContext =>");
        Block(() =>
        {
            RequestDelegateRenderer.RenderBinding(parameters, AppendLine);
            AppendLine();
            AppendLine($"var __result = await handler({RequestDelegateRenderer.RenderArguments(parameters)}).ConfigureAwait(false);");
            AppendLine("await __result.ExecuteAsync(httpContext).ConfigureAwait(false);");
        });
        AppendLine("));");
        AppendLine(EndpointMetadataRenderer.RenderRequestDescription("builder", parameters,
            EndpointMetadataRenderer.RequiredByValidation(_model)));
        AppendLine();

        // Configure the endpoint
        RenderEndpointConfiguration();

        AppendLine();
        AppendLine("return builder;");
    }

    private string GetInvokerType()
    {
        var entityType = _model.MutationEntityType ?? "object";
        return $"global::Pragmatic.Actions.Invoker.IMutationInvoker<{_model.FullTypeName}, {entityType}>";
    }

    private void RenderFilterOverrideScopes()
    {
        foreach (var line in FilterOverrideEmitter.ScopeLines(_model.FilterOverrides!, "filterToggle"))
            AppendLine(line);
    }

    /// <summary>The handler's parameters, and where each value comes from.</summary>
    private List<BoundParameter> BuildBoundParameters()
    {
        var parameters = new List<BoundParameter>();

        foreach (var param in _model.RouteParameters)
            parameters.Add(new BoundParameter(
                param.TypeName, ToCamelCase(param.PropertyName), BindingSource.Route, param.Name, param.BindKind));

        foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
            parameters.Add(new BoundParameter(
                param.TypeName, ToCamelCase(param.PropertyName), BindingSource.Header, param.HeaderName, param.BindKind));

        foreach (var param in _model.QueryParameters.Where(p => p.IsRequired))
            parameters.Add(new BoundParameter(
                param.TypeName, param.Name, BindingSource.Query, param.Name, param.BindKind));

        foreach (var param in _model.FormParameters)
        {
            var isFile = param.TypeName.TrimEnd('?').EndsWith("IFormFile")
                          || param.TypeName.TrimEnd('?').EndsWith("IFormFileCollection");

            parameters.Add(new BoundParameter(
                // Declared as nullable when it is, so an absent optional field reaches a slot that
                // can hold nothing.
                param.IsNullable ? Nullable(param.TypeName) : param.TypeName,
                ToCamelCase(param.PropertyName),
                isFile ? BindingSource.FormFile : BindingSource.Form,
                // The key on the wire: [FromForm(Name = …)], or the property name.
                param.Name,
                // ⚠️ The kind comes from the type. Fixed at String, a Guid form field was bound with
                // TryBindString and handed to a parameter of its own type: CS1503, on generated code.
                isFile ? BindKind.Complex : Models.BindKindNames.FromTypeName(param.TypeName),
                IsRequired: !param.IsNullable));
        }

        // The values nobody marked, on a multipart request: form fields like everything else there.
        // Empty unless the request is multipart. See the domain-action template for the CS0103 this
        // closes.
        foreach (var formProperty in _model.FormBoundProperties)
            parameters.Add(new BoundParameter(
                formProperty.IsNullable ? Nullable(formProperty.TypeName) : formProperty.TypeName,
                ToCamelCase(formProperty.Name),
                BindingSource.Form,
                formProperty.Name,
                Models.BindKindNames.FromTypeName(formProperty.TypeName),
                IsRequired: formProperty.IsRequired && !formProperty.IsNullable,
                DefaultValue: formProperty.DefaultValueSyntax));

        if (!_model.HasFormParams)
        {
            if (_model.HasDirectBodyParam)
            {
                var bodyProperty = _model.BodyProperties[0];
                parameters.Add(new BoundParameter(
                    bodyProperty.TypeName, ToCamelCase(bodyProperty.Name), BindingSource.Body));
            }
            else if (_model.NeedsBodyDto)
            {
                parameters.Add(new BoundParameter(
                    $"{_model.Namespace}.{_model.BodyDtoName}", "body", BindingSource.Body));
            }
        }

        parameters.Add(new BoundParameter(GetInvokerType(), "invoker", BindingSource.Service));

        if (_model.HasFilterOverrides)
            parameters.Add(new BoundParameter(
                "global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle", "filterToggle", BindingSource.Service));

        parameters.Add(new BoundParameter(
            "Microsoft.AspNetCore.Http.HttpContext", "httpContext", BindingSource.HttpContext));
        parameters.Add(new BoundParameter(
            "System.Threading.CancellationToken", "ct", BindingSource.CancellationToken));

        foreach (var param in _model.HeaderParameters.Where(p => !p.IsRequired))
            parameters.Add(new BoundParameter(
                Nullable(param.TypeName), ToCamelCase(param.PropertyName), BindingSource.Header, param.HeaderName,
                param.BindKind, IsRequired: false));

        foreach (var param in _model.QueryParameters.Where(p => !p.IsRequired))
            parameters.Add(new BoundParameter(
                Nullable(param.TypeName), param.Name, BindingSource.Query, param.Name,
                param.BindKind, IsRequired: false));

        return parameters;
    }

    /// <summary>The type with a nullable annotation, unless it already carries one.</summary>
    private static string Nullable(string typeName) => typeName.EndsWith("?") ? typeName : typeName + "?";
}
