using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     MapEndpoint body rendering for EndpointHandlerTemplate.
/// </summary>
internal sealed partial class EndpointHandlerTemplate
{
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
                RenderEndpointFilterOverrideScopes();
                AppendLine();
            }

            // Claims and cookies are read, and refused, before the endpoint is built
            foreach (var line in RequestValueBinding.ReadLines(_model))
                AppendLine(line);

            // Create endpoint instance with object initializer for required/init properties
            RenderEndpointInstantiation();

            // Set dependencies if any
            if (_model.HasDependencies)
            {
                var depParams = string.Join(", ", _model.Dependencies.Select(d =>
                    ToCamelCase(GetFieldNameWithoutUnderscore(d.FieldName))));
                AppendLine($"endpoint.SetDependencies({depParams});");
                AppendLine();
            }

            // Optional values on set properties; the rest are in the initializer above.
            var postConstruction = RequestValueBinding.PostConstructionLines(_model, "endpoint").ToList();
            foreach (var line in postConstruction)
                AppendLine(line);

            if (postConstruction.Count > 0)
                AppendLine();

            // Build EndpointContext for pre/post processors
            if (!_model.PreProcessors.IsDefaultOrEmpty || !_model.PostProcessors.IsDefaultOrEmpty)
            {
                var endpointName = _model.Summary ?? _model.TypeName;
                AppendLine(
                    $"var endpointContext = new global::Pragmatic.Endpoints.Context.EndpointContext(httpContext, \"{StringHelper.CSharpLiteral(endpointName)}\", endpoint);");
                AppendLine();
            }

            // Execute pre-processors (resolve via cached service provider reference)
            if (!_model.PreProcessors.IsDefaultOrEmpty)
            {
                AppendLine("var __sp = httpContext.RequestServices;");
                foreach (var processor in _model.PreProcessors.OrderBy(p => p.Order))
                {
                    AppendLine(
                        $"var preProcessor{processor.Order} = __sp.GetRequiredService<{processor.TypeName}>();");
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
            }

            // File validation: [MaxFileSize] and [AllowedContentTypes] checks
            RenderFileValidation();

            // Auto-validation: call ISyncValidator.Validate() before executing the handler
            if (_model.ShouldAutoValidate)
            {
                AppendLine("// Auto-validation: endpoint implements ISyncValidator");
                AppendLine("var validationResult = ((global::Pragmatic.Validation.ISyncValidator)endpoint).Validate();");
                AppendLine("if (validationResult.IsFailure)");
                Block(() =>
                {
                    AppendLine(
                        "return global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(validationResult, httpContext);");
                });
                AppendLine();
            }

            // Enrich ASP.NET Core Activity with Pragmatic tags
            AppendLine("// Enrich current trace span with Pragmatic endpoint metadata");
            AppendLine($"System.Diagnostics.Activity.Current?.SetTag(\"pragmatic.endpoint.name\", \"{_model.TypeName}\");");
            AppendLine();

            if (_model.IsStreamingResponse)
            {
                // SSE: hand the stream over (first-item peek decides pre-stream errors);
                // post-processors are rejected at compile time (PRAG0524 — no final result).
                foreach (var line in SseTemplateHelper.RenderStandaloneLines(_model))
                    AppendLine(line);
            }
            else
            {
                // Call HandleAsync
                AppendLine("var result = await endpoint.HandleAsync(ct);");
                AppendLine();

                // Execute post-processors (resolve via cached service provider reference)
                if (!_model.PostProcessors.IsDefaultOrEmpty)
                {
                    // Reuse __sp if pre-processors already declared it, otherwise declare here
                    if (_model.PreProcessors.IsDefaultOrEmpty)
                        AppendLine("var __sp = httpContext.RequestServices;");
                    foreach (var processor in _model.PostProcessors.OrderBy(p => p.Order))
                    {
                        AppendLine(
                            $"var postProcessor{processor.Order} = __sp.GetRequiredService<{processor.TypeName}>();");
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

    private void RenderEndpointFilterOverrideScopes()
    {
        var overrides = _model.FilterOverrides!;

        for (var i = 0; i < overrides.DisabledEntityTypes.Length; i++)
        {
            AppendLine(
                $"using var __disableFilter{i} = filterToggle.Disable(typeof({overrides.DisabledEntityTypes[i]}));");
        }

        if (overrides.FilterModeOverride is not null)
        {
            AppendLine(
                $"using var __filterMode = filterToggle.UseMode((global::Pragmatic.Persistence.Query.Filters.FilterMode){overrides.FilterModeOverride.Value});");
        }
    }

    /// <summary>The handler's parameters, and where each value comes from.</summary>
    /// <summary>
    ///     The delegate parameter for an injected field: keyed when the boundary registered it keyed.
    /// </summary>
    /// <remarks>
    ///     One definition for the two places that build the parameter list — the plain handler and the
    ///     versioned one. ⚠️ They were two copies of the same loop, which is how one of them would have
    ///     kept resolving <c>DbContext</c> unkeyed after the other stopped.
    /// </remarks>
    private static BoundParameter DependencyParameter(DependencyModel dep, string name)
        => string.IsNullOrEmpty(dep.KeyedServiceType)
            ? new BoundParameter(dep.TypeName, name, BindingSource.Service)
            : new BoundParameter(dep.TypeName, name, BindingSource.KeyedService,
                ServiceKey: $"typeof({dep.KeyedServiceType})");

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
            // A verb with no body puts the operation's own values in the query string. Emitted here as
            // well as in the domain-action template: switching the body off centrally without emitting
            // these would leave the endpoint compiling and silently unable to receive anything.
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
                    // ⚠️ The kind comes from the type, not a constant. Forcing BindKind.String emitted
                    // `var asOf = __raw_asOf;` — the raw string assigned straight to a parameter the
                    // delegate declares as DateTimeOffset?, which does not compile. Parsable is what
                    // every other query parameter in the framework already uses.
                    Models.BindKindNames.FromTypeName(queryProperty.TypeName),
                    IsRequired: queryProperty.IsRequired && !queryProperty.IsNullable,
                    // The declaration's initializer — see the domain-action template for why the
                    // local must not start at `default`.
                    DefaultValue: queryProperty.DefaultValueSyntax));
            }

            // Same shape as a domain action's: with BindBodyDirectly the single complex body property
            // is the body, so the parameter is that property rather than a generated envelope.
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

        foreach (var dep in _model.Dependencies)
            parameters.Add(DependencyParameter(dep, ToCamelCase(GetFieldNameWithoutUnderscore(dep.FieldName))));

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
