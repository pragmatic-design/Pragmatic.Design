using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates the endpoint handler for DomainAction-based endpoints.
///     Uses IDomainActionInvoker/IVoidDomainActionInvoker for pipeline execution.
/// </summary>
internal sealed partial class DomainActionHandlerTemplate : CSharpTemplate
{
    private readonly EndpointModel _model;

    public DomainActionHandlerTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("Endpoint"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, IsDomainAction: true };
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
        XmlSummary("Maps this DomainAction endpoint to the route builder.");
        XmlParam("endpoints", "The endpoint route builder.");
        XmlReturns("The route handler builder for further configuration.");

        // Streaming actions always use the non-versioned path (PRAG0523 warns on versioned methods).
        var bodyMethod = _model is { HasActionVersioning: true, HasAspVersioning: true, IsStreamingResponse: false }
            ? (Action)RenderVersionedMapEndpointBody
            : RenderMapEndpointBody;

        Method("MapEndpoint", bodyMethod,
            "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder",
            new List<MethodParameter>
            {
                new("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "endpoints")
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
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

            // Claims and cookies are read, and refused, before the action is built
            foreach (var line in RequestValueBinding.ReadLines(_model))
                AppendLine(line);

            // Create action instance with object initializer for required/init properties
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

            // File validation: [MaxFileSize] and [AllowedContentTypes] checks
            RenderFileValidation();

            // Attachment upload file validation (size / extension) at the HTTP boundary.
            if (_model.IsAttachmentUpload)
                RenderAttachmentUploadValidation();

            // No inline ISyncValidator call here: the action invoker pipeline already runs
            // ValidationFilter (Order 100) with full semantics ([NoValidation], nested, async).
            // Validating inline too would run every sync validator twice per request.

            // Enrich ASP.NET Core Activity with Pragmatic tags (single null-check to avoid repeated ?. overhead)
            var actionKind = _model.IsMutation ? "mutation"
                : _model.IsVoidDomainAction ? "void_action"
                : "domain_action";
            AppendLine("var __activity = System.Diagnostics.Activity.Current;");
            AppendLine("if (__activity is not null)");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"__activity.SetTag(\"pragmatic.action.name\", \"{_model.TypeName}\");");
            AppendLine($"__activity.SetTag(\"pragmatic.action.kind\", \"{actionKind}\");");
            DecreaseIndent();
            AppendLine("}");
            AppendLine();

            // Call InvokeAsync on the invoker
            AppendLine("var result = await invoker.InvokeAsync(action, ct);");
            AppendLine();

            if (_model.IsStreamingResponse)
            {
                // SSE: filters already ran (pre-stream) inside the invoker; the success value
                // IS the stream. Failures become ProblemDetails before the response starts.
                foreach (var line in SseTemplateHelper.RenderDomainActionLines(_model))
                    AppendLine(line);
            }
            else
            {
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

                // Handle result
                RenderResultHandling();
            }
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

    private void RenderFilterOverrideScopes()
    {
        foreach (var line in FilterOverrideEmitter.ScopeLines(_model.FilterOverrides!, "filterToggle"))
            AppendLine(line);
    }

    /// <summary>
    ///     The handler's parameters, and where each value comes from.
    /// </summary>
    /// <remarks>
    ///     One list drives both the signature and the binding in front of it. No <c>[From…]</c>
    ///     attributes are emitted for ASP.NET to read: this method is what decides where each value
    ///     comes from.
    /// </remarks>
    private List<BoundParameter> BuildBoundParameters()
    {
        var parameters = new List<BoundParameter>();

        foreach (var param in _model.RouteParameters)
            parameters.Add(new BoundParameter(
                param.TypeName, ToCamelCase(param.Name), BindingSource.Route, param.Name, param.BindKind));

        foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
            parameters.Add(new BoundParameter(
                param.TypeName, ToCamelCase(param.PropertyName), BindingSource.Header, param.HeaderName, param.BindKind));

        foreach (var param in _model.HeaderParameters.Where(p => !p.IsRequired))
            parameters.Add(new BoundParameter(
                Nullable(param.TypeName), ToCamelCase(param.PropertyName), BindingSource.Header, param.HeaderName,
                param.BindKind, IsRequired: false));

        foreach (var param in _model.QueryParameters.Where(p => p.IsRequired))
            parameters.Add(new BoundParameter(
                param.TypeName, param.Name, BindingSource.Query, param.Name, param.BindKind));

        foreach (var param in _model.QueryParameters.Where(p => !p.IsRequired))
            parameters.Add(new BoundParameter(
                Nullable(param.TypeName), param.Name, BindingSource.Query, param.Name,
                param.BindKind, IsRequired: false));

        foreach (var param in _model.FormParameters)
        {
            // A multipart field carrying a file is not a string with a different name: it comes from
            // the form's file collection, and asking the text collection for it yields nothing.
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

        // The operation's own values when the request is multipart: form fields, whether or not the
        // author wrote [FromForm] on them. ⚠️ Without this they would be read from `body`, which a
        // multipart handler does not declare — CS0103 on generated code, with a remedy the message
        // does not mention. Empty unless the request is multipart, so no other
        // shape is touched. The wire key is the property name, exactly what [FromForm] would have
        // produced, so adding or removing the attribute changes nothing.
        foreach (var formProperty in _model.FormBoundProperties)
            parameters.Add(new BoundParameter(
                formProperty.IsNullable ? Nullable(formProperty.TypeName) : formProperty.TypeName,
                ToCamelCase(formProperty.Name),
                BindingSource.Form,
                formProperty.Name,
                Models.BindKindNames.FromTypeName(formProperty.TypeName),
                IsRequired: formProperty.IsRequired && !formProperty.IsNullable,
                DefaultValue: formProperty.DefaultValueSyntax));

        if (_model.IsAttachmentUpload)
        {
            parameters.Add(new BoundParameter(
                "Microsoft.AspNetCore.Http.IFormFile", "file", BindingSource.FormFile));

            if (_model.AttachmentUpload!.DescriptionProperty is not null)
                parameters.Add(new BoundParameter(
                    "string?", "description", BindingSource.Form, "description", BindKind.String, IsRequired: false));
        }

        if (!_model.HasFormParams && !_model.IsAttachmentUpload)
        {
            // A verb with no body puts the operation's own values in the query string. Without this a
            // GET action with parameters generated a MapGet demanding a JSON body, which no browser,
            // HttpClient or generated client sends.
            foreach (var queryProperty in _model.QueryBoundProperties)
            {
                parameters.Add(new BoundParameter(
                    // ⚠️ The annotation, which the type name does not carry.
                    // ToDisplayString(FullyQualifiedFormat) renders `string?` as `string` — for a
                    // reference type the `?` is an annotation rather than part of the type — so
                    // passing TypeName through declared a non-nullable parameter and bound a
                    // possibly-absent query value into it: CS8604, inside a file the consumer cannot
                    // edit. Keyed on IsNullable and not on IsRequired: a property with a real default
                    // is optional and NOT nullable, and widening that one hands an int? to an int.
                    queryProperty.IsNullable
                        ? Nullable(queryProperty.TypeName)
                        : queryProperty.TypeName,
                    ToCamelCase(queryProperty.Name),
                    BindingSource.Query,
                    queryProperty.JsonName ?? ToCamelCase(queryProperty.Name),
                    // ⚠️ The kind comes from the type, not a constant. Forcing BindKind.String would
                    // emit `var asOf = __raw_asOf;` — the raw string assigned straight to a parameter the
                    // delegate declares as DateTimeOffset?, which does not compile. Parsable is what
                    // every other query parameter in the framework uses.
                    Models.BindKindNames.FromTypeName(queryProperty.TypeName),
                    IsRequired: queryProperty.IsRequired && !queryProperty.IsNullable,
                    // ⚠️ The declaration's initializer, so a value the caller leaves out is what the
                    // property says it is. Starting the local at `default` would make `Page = 1` reach
                    // the action as 0 whenever the query string did not mention it, and a consumer's
                    // WithPaging(0, 0) would return one row of a subtree with nothing to say why.
                    DefaultValue: queryProperty.DefaultValueSyntax));
            }

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

        return parameters;
    }

    /// <summary>The type with a nullable annotation, unless it already carries one.</summary>
    private static string Nullable(string typeName) => typeName.EndsWith("?") ? typeName : typeName + "?";
}
