using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

// Result handling, action instantiation, and versioned endpoint rendering
internal sealed partial class DomainActionHandlerTemplate
{
    private void RenderVersionedMapEndpointBody()
    {
        var route = _model.Route;
        var invokerType = GetInvokerType();

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
            var parameters = BuildVersionedBoundParameters(version, dtoName, hasVersionBodyProps, invokerType);

            AppendLine($"var {handlerVar} = async (");
            RequestDelegateRenderer.RenderSignature(parameters, AppendLine);
            AppendLine(") =>");

            Block(() =>
            {
                // Claims and cookies are read, and refused, before the action is built
                foreach (var line in RequestValueBinding.ReadLines(_model))
                    AppendLine(line);

                // Build object initializer with version-appropriate body properties
                var initProps = new List<string>();

                foreach (var param in _model.RouteParameters)
                    initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

                foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
                    initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

                initProps.AddRange(RequestValueBinding.InitializerEntries(_model, includeQuery: false));

                if (hasVersionBodyProps)
                    foreach (var prop in version.BodyProperties)
                        initProps.Add($"{prop.Name} = body.{prop.Name}");

                if (initProps.Count == 0)
                    AppendLine($"var action = new {_model.FullTypeName}();");
                else if (initProps.Count <= 3)
                {
                    var props = string.Join(", ", initProps);
                    AppendLine($"var action = new {_model.FullTypeName} {{ {props} }};");
                }
                else
                {
                    AppendLine($"var action = new {_model.FullTypeName}");
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

                // Set TargetVersion (skip for v1.0)
                if (version.Major != 1 || version.Minor != 0)
                    AppendLine($"action.TargetVersion = ({version.Major}, {version.Minor});");

                // Optional values on set properties; the rest are in the initializer above.
                var postConstruction = RequestValueBinding.PostConstructionLines(_model, "action", includeQuery: false).ToList();
                foreach (var line in postConstruction)
                    AppendLine(line);

                if (postConstruction.Count > 0)
                    AppendLine();

                // No inline ISyncValidator call: the invoker pipeline's ValidationFilter
                // already validates (see DomainActionHandlerTemplate.RenderMapMethod).

                // Enrich ASP.NET Core Activity with Pragmatic tags
                AppendLine($"System.Diagnostics.Activity.Current?.SetTag(\"pragmatic.action.name\", \"{_model.TypeName}\");");
                var vActionKind = _model.IsMutation ? "mutation"
                    : _model.IsVoidDomainAction ? "void_action"
                    : "domain_action";
                AppendLine($"System.Diagnostics.Activity.Current?.SetTag(\"pragmatic.action.kind\", \"{vActionKind}\");");

                // Invoke
                AppendLine("var result = await invoker.InvokeAsync(action, ct);");
                AppendLine();

                // Result handling (inline for versioned — same pattern)
                RenderResultHandling();
            });

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

            // Apply shared endpoint configuration to EVERY version, not just the last one.
            RenderEndpointConfiguration(builderVar);
            AppendLine();
        }

        AppendLine("return builder;");
    }

    private void RenderResultHandling()
    {
        if (_model.IsVoidDomainAction)
        {
            // VoidDomainAction - return 204 on success
            AppendLine("return result.Match(");
            IncreaseIndent();
            AppendLine("() => Microsoft.AspNetCore.Http.Results.NoContent(),");
            AppendLine("(global::Pragmatic.Result.IError error) => MapError(error, httpContext)");
            DecreaseIndent();
            AppendLine(");");
        }
        else if (_model.IsFileResponse)
        {
            // DomainAction<FileResponse> — the success value IS the file. Serializing it as JSON
            // (the generic branch below) would emit the record's shape and leave the Stream unread.
            // Same helper the Endpoint<FileResponse> path uses, so ETag / If-None-Match /
            // Content-Disposition behave identically no matter which base type the author picked.
            AppendLine("return result.Match(");
            IncreaseIndent();
            AppendLine(
                "(success) => global::Pragmatic.Endpoints.Extensions.FileResponseExtensions.ToResult(success, httpContext),");
            AppendLine("(global::Pragmatic.Result.IError error) => MapError(error, httpContext)");
            DecreaseIndent();
            AppendLine(");");
        }
        else
        {
            // DomainAction - return response on success
            AppendLine("return result.Match(");
            IncreaseIndent();

            var successCode = _model.ComputedSuccessStatusCode;
            if (successCode == 200)
                AppendLine($"(success) => {GeneratedResponseRenderer.Render(_model, "success", 200) ?? "Microsoft.AspNetCore.Http.Results.Ok(success)"},");
            else if (successCode == 201)
            {
            // [CreatedAt] template → real Location header; otherwise 201 with Location: null (B20).
            var location = _model.CreatedAtTemplate is { } template
                ? CreatedAtLocationRenderer.Render(template, "success")
                : "(string?)null";
            AppendLine($"(success) => {GeneratedResponseRenderer.Render(_model, "success", 201, location) ?? $"Microsoft.AspNetCore.Http.Results.Created({location}, success)"},");
        }
            else if (successCode == 204)
                // 204 has no body by definition.
                AppendLine("(success) => Microsoft.AspNetCore.Http.Results.NoContent(),");
            else
                // Preserve the response body for non-standard success codes (e.g. 202 Accepted).
                AppendLine($"(success) => {GeneratedResponseRenderer.Render(_model, "success", successCode) ?? $"Microsoft.AspNetCore.Http.Results.Json(success, statusCode: {successCode})"},");

            AppendLine("(global::Pragmatic.Result.IError error) => MapError(error, httpContext)");
            DecreaseIndent();
            AppendLine(");");
        }

        AppendLine();

        // Add MapError helper - use extension method
        AppendLine("static Microsoft.AspNetCore.Http.IResult MapError(global::Pragmatic.Result.IError error, Microsoft.AspNetCore.Http.HttpContext httpContext)");
        Block(() => { AppendLine("return global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(error, httpContext);"); });
    }

    private void RenderActionInstantiation()
    {
        // Collect all properties that must be set in object initializer
        // (for init-only and required properties)
        var initProps = new List<string>();

        // Route parameters (always required) — Name matches route placeholder, PropertyName is the action property
        foreach (var param in _model.RouteParameters)
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.Name)}");

        // Required header parameters
        foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // Required query parameters
        foreach (var param in _model.QueryParameters.Where(p => p.IsRequired))
            initProps.Add($"{param.PropertyName} = {param.Name}");

        // Optional header and query values on init properties
        initProps.AddRange(RequestValueBinding.InitializerEntries(_model));

        // Multipart file upload ([HasAttachments]): map the IFormFile onto the action's file members.
        if (_model.IsAttachmentUpload)
        {
            var up = _model.AttachmentUpload!;
            initProps.Add($"{up.FileContentProperty} = file.OpenReadStream()");
            initProps.Add($"{up.FileNameProperty} = file.FileName");
            initProps.Add($"{up.FileSizeProperty} = file.Length");
            initProps.Add($"{up.ContentTypeProperty} = file.ContentType");
            if (up.DescriptionProperty is not null)
                initProps.Add($"{up.DescriptionProperty} = description");
        }

        // Values bound from the query string because the verb carries no body.
        //
        // ⚠️ Emitted here as well as in the delegate signature: binding them and not assigning them
        // would produce `new TheAction()` with a required property unset — CS9035, on generated code the
        // author did not write. The same shape as the body properties below, which is what these are
        // when the verb has a body.
        foreach (var queryProperty in _model.QueryBoundProperties)
            initProps.Add($"{queryProperty.Name} = {ToCamelCase(queryProperty.Name)}");

        // Form fields, in the initializer with everything else. ⚠️ Assigned after construction, a
        // `required` or `init` form property, the shape every operation of this framework is written
        // in, would not compile: CS9035 on the first, CS8852 on the second. No declaration avoids both.
        foreach (var param in _model.FormParameters)
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // And the ones nobody marked, on a multipart request: same shape, same reason as the query-bound
        // ones above — bound in the signature and assigned here, or the operation is built with a
        // required property unset.
        foreach (var formProperty in _model.FormBoundProperties)
            initProps.Add($"{formProperty.Name} = {ToCamelCase(formProperty.Name)}");

        // Body properties
        if (_model.HasDirectBodyParam)
        {
            // DomainAction with single body property — map directly
            var bp = _model.BodyProperties[0];
            initProps.Add($"{bp.Name} = {ToCamelCase(bp.Name)}");
        }
        else if (_model.NeedsBodyDto)
        {
            foreach (var prop in _model.BodyProperties)
                initProps.Add($"{prop.Name} = body.{prop.Name}");
        }

        // Generate with or without object initializer
        if (initProps.Count == 0)
        {
            AppendLine($"var action = new {_model.FullTypeName}();");
        }
        else if (initProps.Count <= 3)
        {
            // Single line for short initializers
            var props = string.Join(", ", initProps);
            AppendLine($"var action = new {_model.FullTypeName} {{ {props} }};");
        }
        else
        {
            // Multi-line for longer initializers
            AppendLine($"var action = new {_model.FullTypeName}");
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
    }

    /// <summary>
    ///     The parameters of one version's handler, and where each value comes from.
    /// </summary>
    private List<BoundParameter> BuildVersionedBoundParameters(
        Models.ActionVersionModel version, string dtoName, bool hasVersionBodyProps, string invokerType)
    {
        var parameters = new List<BoundParameter>();

        foreach (var param in _model.RouteParameters)
            parameters.Add(new BoundParameter(
                param.TypeName, ToCamelCase(param.PropertyName), BindingSource.Route, param.Name, param.BindKind));

        foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
            parameters.Add(new BoundParameter(
                param.TypeName, ToCamelCase(param.PropertyName), BindingSource.Header, param.HeaderName, param.BindKind));

        foreach (var param in _model.HeaderParameters.Where(p => !p.IsRequired))
            parameters.Add(new BoundParameter(
                Nullable(param.TypeName), ToCamelCase(param.PropertyName), BindingSource.Header, param.HeaderName,
                param.BindKind, IsRequired: false));

        if (hasVersionBodyProps)
            parameters.Add(new BoundParameter($"{_model.Namespace}.{dtoName}", "body", BindingSource.Body));

        parameters.Add(new BoundParameter(invokerType, "invoker", BindingSource.Service));
        parameters.Add(new BoundParameter(
            "Microsoft.AspNetCore.Http.HttpContext", "httpContext", BindingSource.HttpContext));
        parameters.Add(new BoundParameter(
            "System.Threading.CancellationToken", "ct", BindingSource.CancellationToken));

        return parameters;
    }
}
