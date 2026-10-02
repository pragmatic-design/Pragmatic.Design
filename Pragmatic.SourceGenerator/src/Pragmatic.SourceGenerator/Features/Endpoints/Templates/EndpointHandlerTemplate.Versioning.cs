using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Versioned endpoint rendering for EndpointHandlerTemplate.
///     Generates multiple route registrations (one per HandleAsyncV{n} method)
///     with version-specific body DTOs and ApiVersionSet configuration.
/// </summary>
internal sealed partial class EndpointHandlerTemplate
{
    private void RenderVersionedMapEndpointBody()
    {
        var route = _model.Route;

        // Build ApiVersionSet
        AppendLine("var versionSet = endpoints.NewApiVersionSet()");
        IncreaseIndent();
        foreach (var version in _model.ActionVersions)
            AppendLine(
                $".HasApiVersion(new Asp.Versioning.ApiVersion({version.Major}, {version.Minor}))");
        AppendLine(".Build();");
        DecreaseIndent();
        AppendLine();

        // Generate a route handler for each version
        for (var vi = 0; vi < _model.ActionVersions.Length; vi++)
        {
            var version = _model.ActionVersions[vi];
            var isLast = vi == _model.ActionVersions.Length - 1;
            var builderVar = isLast ? "builder" : $"builder{version.BodyDtoSuffix}";

            var dtoName = $"{_model.TypeName}{version.BodyDtoSuffix}Body";
            var hasVersionBodyProps = !version.BodyProperties.IsDefaultOrEmpty;

            // The handler keeps its shape; the RequestDelegate below binds its arguments and calls
            // it, because ASP.NET cannot bind a generated handler under AOT.
            var handlerVar = $"handler{version.BodyDtoSuffix}";
            var parameters = BuildVersionedBoundParameters(dtoName, hasVersionBodyProps);

            AppendLine($"var {handlerVar} = async (");
            RequestDelegateRenderer.RenderSignature(parameters, AppendLine);
            AppendLine(") =>");

            Block(() =>
            {
                // Claims and cookies are read, and refused, before the endpoint is built
                foreach (var line in RequestValueBinding.ReadLines(_model))
                    AppendLine(line);

                // Build object initializer with version-appropriate body properties
                var initProps = new List<string>();

                foreach (var param in _model.RouteParameters)
                    initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

                foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
                    initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

                foreach (var param in _model.QueryParameters.Where(p => p.IsRequired))
                    initProps.Add($"{param.PropertyName} = {param.Name}");

                initProps.AddRange(RequestValueBinding.InitializerEntries(_model));

                if (hasVersionBodyProps)
                    foreach (var prop in version.BodyProperties)
                        initProps.Add($"{prop.Name} = body.{prop.Name}");

                if (initProps.Count == 0)
                    AppendLine($"var endpoint = new {_model.FullTypeName}();");
                else if (initProps.Count <= 3)
                {
                    var props = string.Join(", ", initProps);
                    AppendLine($"var endpoint = new {_model.FullTypeName} {{ {props} }};");
                }
                else
                {
                    AppendLine($"var endpoint = new {_model.FullTypeName}");
                    AppendLine("{");
                    IncreaseIndent();
                    for (var i = 0; i < initProps.Count; i++)
                    {
                        var comma = i < initProps.Count - 1 ? "," : "";
                        AppendLine($"{initProps[i]}{comma}");
                    }

                    DecreaseIndent();
                    AppendLine("};");
                }

                AppendLine();

                // Set dependencies
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

                // Enrich trace
                AppendLine($"System.Diagnostics.Activity.Current?.SetTag(\"pragmatic.endpoint.name\", \"{_model.TypeName}\");");
                AppendLine($"System.Diagnostics.Activity.Current?.SetTag(\"pragmatic.endpoint.version\", \"{version.VersionString}\");");
                AppendLine();

                // Call the versioned HandleAsync method
                AppendLine($"var result = await endpoint.{version.MethodName}(ct);");
                AppendLine();

                // Result handling
                RenderResultHandling();
            });

            // Version-specific builder configuration
            AppendLine(";");
            AppendLine();

            AppendLine($"var {builderVar} = endpoints.{MapInvocationHelper.Render(_model.HttpMethod, StringHelper.CSharpLiteral(route))}(Microsoft.AspNetCore.Http.RequestDelegate)(async httpContext =>");
            Block(() =>
            {
                RequestDelegateRenderer.RenderBinding(parameters, AppendLine);
                AppendLine();
                AppendLine($"var __result = await {handlerVar}({RequestDelegateRenderer.RenderArguments(parameters)}).ConfigureAwait(false);");
                AppendLine("await __result.ExecuteAsync(httpContext).ConfigureAwait(false);");
            });
            AppendLine("));");
            AppendLine(EndpointMetadataRenderer.RenderRequestDescription(builderVar, parameters,
            EndpointMetadataRenderer.RequiredByValidation(_model)));
            AppendLine();
            AppendLine(
                $"{builderVar}.WithApiVersionSet(versionSet).MapToApiVersion(new Asp.Versioning.ApiVersion({version.Major}, {version.Minor}));");
            AppendLine();

            // Apply the shared endpoint configuration (auth, permissions, rate-limit, caching,
            // OpenAPI metadata) to EVERY version, not just the last one. Run once after the loop
            // against "builder" (= last version), it would leave earlier API versions exposed with
            // no authorization/rate-limit/caching.
            RenderEndpointConfiguration(builderVar);
            AppendLine();
        }

        AppendLine("return builder;");
    }

    /// <summary>The parameters of one version's handler, and where each value comes from.</summary>
    private List<BoundParameter> BuildVersionedBoundParameters(string dtoName, bool hasVersionBodyProps)
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

        if (hasVersionBodyProps)
            parameters.Add(new BoundParameter($"{_model.Namespace}.{dtoName}", "body", BindingSource.Body));

        foreach (var dep in _model.Dependencies)
            parameters.Add(DependencyParameter(dep, ToCamelCase(GetFieldNameWithoutUnderscore(dep.FieldName))));

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
}
