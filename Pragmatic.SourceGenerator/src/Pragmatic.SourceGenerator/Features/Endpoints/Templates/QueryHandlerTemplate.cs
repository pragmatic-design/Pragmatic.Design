using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates the endpoint handler for Query-based endpoints.
///     Uses IQueryExecutor to execute the query and return results.
/// </summary>
internal sealed partial class QueryHandlerTemplate : CSharpTemplate
{
    private readonly EndpointModel _model;

    public QueryHandlerTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] on Query {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("Endpoint"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, IsQuery: true };
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Http");
        AddUsing("Microsoft.AspNetCore.Routing");
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderClassBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        RenderResourcePolicyField();

        // Static JsonSerializerOptions for complex filter deserialization (avoids per-request allocation)
        if (_model.QueryParameters.Any(p => p.IsComplexFilter))
        {
            Comment("Deserialized through the request's own options, not a private instance: a fresh");
            Comment("JsonSerializerOptions resolves by reflection, which hid for months the fact that no");
            Comment("context covered these filter types at all.");
            AppendLine();
        }

        XmlSummary("Maps this Query endpoint to the route builder.");
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
    }

    private void RenderMapEndpointBody()
    {
        var parameters = BuildBoundParameters();

        // The handler keeps the shape it always had. What changed is who calls it: ASP.NET cannot bind
        // a generated handler under AOT, so the RequestDelegate below binds and invokes it.
        AppendLine("var handler = async (");
        RequestDelegateRenderer.RenderSignature(parameters, AppendLine);
        AppendLine(") =>");

        Block(() =>
        {
            // [WithoutFilter<T>] / [FilterMode] — disable specific filters
            if (_model.HasFilterOverrides)
            {
                RenderFilterOverrideScopes();
                AppendLine();
            }

            // Claims and cookies are read, and refused, before the query is built
            foreach (var line in ClaimBindingHelper.ReadLines(_model))
                AppendLine(line);
            foreach (var line in CookieBindingHelper.ReadLines(_model))
                AppendLine(line);

            // Create query instance
            RenderQueryInstantiation();

            // Optional claims and cookies on set properties; the rest are in the initializer above.
            foreach (var line in ClaimBindingHelper.PostConstructionLines(_model, "query"))
                AppendLine(line);
            foreach (var line in CookieBindingHelper.PostConstructionLines(_model, "query"))
                AppendLine(line);

            // No validation here: it runs in the query's invoker, so an in-process caller gets the same
            // refusal this route gives, and there is one copy of it rather than one per door. The
            // refusal arrives as a ValidationError, which is an IHttpError, so this route answers 400.
            // Resource authorization, the third of the four levels, which a query must not skip. The type
            // argument of IResourceAuthorizer<T> is the operation, not the row — the Showcase registers
            // one for an action — so for a query it answers "may this caller run this query with these
            // arguments", which is as well defined for a list as for a single result.
            //
            // Optional by resolution rather than by declaration: nothing on the query says whether an
            // authorizer exists, because registering one is a DI decision. GetService, never
            // GetRequiredService.
            AppendLine(
                $"var __authorizer = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                + $".GetService<global::Pragmatic.Authorization.IResourceAuthorizer<{_model.TypeName}>>(httpContext.RequestServices);");
            AppendLine();
            AppendLine("if (__authorizer is not null)");
            Block(() =>
            {
                AppendLine(
                    "var __authUser = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                    + ".GetService<global::Pragmatic.Identity.ICurrentUser>(httpContext.RequestServices)"
                    + " ?? global::Pragmatic.Identity.AnonymousUser.Instance;");
                AppendLine(
                    "if (!await __authorizer.CanAccessAsync(__authUser, query, \"read\", ct).ConfigureAwait(false))");
                IncreaseIndent();
                AppendLine("return Microsoft.AspNetCore.Http.Results.StatusCode(403);");
                DecreaseIndent();
            });
            AppendLine();

            // The read, through the query's own invoker. A handler that resolved IQueryExecutor and the
            // keyed context and ran the read itself would make HTTP a second way to invoke the
            // operation: validating and enforcing its own way, while an in-process caller got neither.
            // The invoker is generated beside the query, so the source it reads from — tracking,
            // filters, the boundary's context — is the same one this handler built.
            AppendLine($"var __invoker = new {_model.FullTypeName}.Invoker(httpContext.RequestServices);");
            AppendLine("var __outcome = await __invoker.RunAsync(query, ct).ConfigureAwait(false);");
            AppendLine();
            // A refusal — validation or permission — before the shape of the answer is considered.
            // ValidationError and ForbiddenError are both IHttpError, so the status is theirs: 400 and
            // 403.
            AppendLine("if (__outcome.IsFailure)");
            IncreaseIndent();
            AppendLine("return global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(__outcome.Error!, httpContext);");
            DecreaseIndent();
            AppendLine();

            if (_model.QueryIsSingle)
            {
                // Single = true: at most one row, and 404 rather than an empty list, because "not
                // there" and "there but empty" are different answers to a client. The 404 comes from the
                // executor's failure, which the refusal check above already turned into a response.
                RenderAccessRecording();
                AppendLine($"return {GeneratedResponseRenderer.Render(_model, "__outcome.Value", 200) ?? "Microsoft.AspNetCore.Http.Results.Ok(__outcome.Value)"};");
            }
            else if (_model.QueryIsPaged)
            {
                // PagedResult carries the execution's own failure — a QueryError: 500 for the database,
                // 503 for the connection, 504 for a timeout — and it answers like every other failure of
                // this handler, with its own status as ProblemDetails. A BadRequest of the raw error
                // would make a database outage answer 400, "the request was wrong", with the error
                // serialized as application/json.
                AppendLine("var result = __outcome.Value!;");
                AppendLine("if (result.IsFailure)");
                IncreaseIndent();
                AppendLine("return global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(result.Error!, httpContext);");
                DecreaseIndent();
                AppendLine();
                RenderAccessRecording();
                AppendLine($"return {GeneratedResponseRenderer.Render(_model, "result", 200) ?? "Microsoft.AspNetCore.Http.Results.Ok(result)"};");
            }
            else
            {
                // IReadOnlyList<TResult> has no IsFailure of its own, so a failure check here would not
                // compile. ⚠️ A [Query] that declares Page/PageSize never reaches this branch, so only
                // a query without them exercises it.
                AppendLine("var items = __outcome.Value!;");
                AppendLine();
                RenderAccessRecording();
                AppendLine($"return {GeneratedResponseRenderer.Render(_model, "items", 200) ?? "Microsoft.AspNetCore.Http.Results.Ok(items)"};");
            }
        });

        AppendLine(";");
        AppendLine();

        // The verb the author declared, like every other handler. MapGet whatever the [Endpoint] said
        // would make a query declaring POST publish POST in the manifest, in the routes and in the
        // OpenAPI document, and answer 405 there while answering GET on the same path.
        AppendLine(
            $"var builder = endpoints.{MapInvocationHelper.Render(_model.HttpMethod, StringHelper.CSharpLiteral(_model.Route))}"
            + "(Microsoft.AspNetCore.Http.RequestDelegate)(async httpContext =>");
        Block(() =>
        {
            RenderResourcePolicyCheck();
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

    /// <summary>
    ///     Records the read in the audit trail, for a query that asked to be recorded.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Off unless the query carries <c>[RecordAccess]</c>. Writes reach the trail through an
    ///         interceptor over <c>SaveChanges</c>, which never sees a query, so reads reach it nowhere —
    ///         and recording all of them would bury the ones that matter under the ones that do not.
    ///     </para>
    ///     <para>
    ///         After the query succeeded, not before it ran: a read that failed is not a read. What is
    ///         written is the operation, the actor and the time — never the rows, or the trail would hold
    ///         a second copy of the personal data it exists to account for.
    ///     </para>
    ///     <para>
    ///         The trail is resolved rather than required, because an application can reference the audit
    ///         package without registering a store. That branch is silent by necessity: throwing inside a
    ///         successful read would turn a missing registration into a failed request.
    ///     </para>
    /// </remarks>
    private void RenderAccessRecording()
    {
        if (!_model.CanRecordAccess)
            return;

        var operation = string.IsNullOrEmpty(_model.Namespace)
            ? _model.TypeName
            : _model.Namespace + "." + _model.TypeName;

        AppendLine(
            "var __trail = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
            + ".GetService<global::Pragmatic.Audit.IAuditTrail>(httpContext.RequestServices);");
        AppendLine("if (__trail is not null)");
        Block(() =>
        {
            AppendLine(
                "var __accessUser = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                + ".GetService<global::Pragmatic.Identity.ICurrentUser>(httpContext.RequestServices);");
            AppendLine(
                "var __accessClock = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                + ".GetService<global::System.TimeProvider>(httpContext.RequestServices);");
            AppendLine();
            AppendLine("await __trail.RecordAsync(new global::Pragmatic.Audit.AuditEntry");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("SegmentId = string.Empty,");
            AppendLine(
                "OccurredAt = (__accessClock ?? global::System.TimeProvider.System).GetUtcNow(),");
            AppendLine("Category = global::Pragmatic.Audit.AuditCategory.Privacy,");
            AppendLine("Operation = \"Privacy.PersonalDataRead\",");
            AppendLine($"BusinessOperation = \"{operation}\",");
            AppendLine(
                "ActorRef = __accessUser is null ? null "
                + ": global::Pragmatic.Identity.CurrentUserExtensions.IdOrNull(__accessUser),");
            AppendLine($"TargetType = \"{_model.QueryEntityType?.Split('.').LastOrDefault() ?? string.Empty}\",");
            AppendLine("Outcome = global::Pragmatic.Audit.AuditOutcome.Success,");
            DecreaseIndent();
            AppendLine("}, ct).ConfigureAwait(false);");
        });
        AppendLine();
    }

    /// <summary>
    ///     Evaluates <c>[RequirePolicy&lt;T&gt;]</c> before the query runs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A query's invoker validates and checks the permission but does not run the action-filter
    ///         chain, so <c>PolicyEvaluationFilter</c> — an <c>IActionFilter</c> at Order 210 — never
    ///         runs for one. Without this check a policy declared on a query would be read by nobody,
    ///         and the endpoint would be as open as one that declared nothing.
    ///     </para>
    ///     <para>
    ///         No policy registry here, and none is needed: the registry maps a type to an instance for
    ///         callers that only know the type at run time, while this endpoint knows the policy type at
    ///         generation time. The instance is a <c>static readonly</c> field, which is also what
    ///         <c>[RequirePolicy&lt;T&gt;]</c> documents — created once, cached, parameterless.
    ///     </para>
    ///     <para>
    ///         In the RequestDelegate rather than in the handler: <c>httpContext</c> is a handler
    ///         parameter only for a single-result query, and the check has to happen for every shape.
    ///     </para>
    /// </remarks>
    private void RenderResourcePolicyCheck()
    {
        if (_model.DeclaredResourcePolicy is null)
            return;

        AppendLine(
            "var __user = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
            + ".GetService<global::Pragmatic.Identity.ICurrentUser>(httpContext.RequestServices);");
        AppendLine();
        AppendLine("if (__user is null || !__user.IsAuthenticated)");
        Block(() =>
        {
            AppendLine("httpContext.Response.StatusCode = 401;");
            AppendLine("return;");
        });
        AppendLine();
        AppendLine("if (!__policy.Evaluate(__user))");
        Block(() =>
        {
            AppendLine("httpContext.Response.StatusCode = 403;");
            AppendLine("return;");
        });
        AppendLine();
    }

    /// <summary>The policy instance, created once for the lifetime of the process.</summary>
    private void RenderResourcePolicyField()
    {
        if (_model.DeclaredResourcePolicy is null)
            return;

        AppendLine(
            $"private static readonly global::Pragmatic.Authorization.Policy.ResourcePolicy __policy = "
            + $"new {_model.DeclaredResourcePolicy}();");
        AppendLine();
    }

    // Collection query params bind from repeated query keys (?x=a&x=b). Two constraints from minimal APIs:
    //  1. they must be explicitly [FromQuery] — otherwise a complex type is inferred as [FromBody], which a GET
    //     rejects ("Body was inferred but the method does not allow inferred body parameters");
    //  2. the bound parameter must be an ARRAY (T[]) — List<T> is rejected ("no TryParse(string) for List<T>").
    // So we bind `element[]` and convert to the DTO's declared collection type in the object initializer.
    private static bool IsCollectionTypeName(string typeName)
    {
        var t = typeName.TrimEnd('?');
        return t.EndsWith("[]")
            || t.Contains("System.Collections.Generic.List<")
            || t.Contains("System.Collections.Generic.IList<")
            || t.Contains("System.Collections.Generic.ICollection<")
            || t.Contains("System.Collections.Generic.IEnumerable<")
            || t.Contains("System.Collections.Generic.IReadOnlyList<")
            || t.Contains("System.Collections.Generic.IReadOnlyCollection<");
    }

    private static string CollectionElementType(string typeName)
    {
        var t = typeName.TrimEnd('?');
        if (t.EndsWith("[]")) return t.Substring(0, t.Length - 2);
        var lt = t.IndexOf('<');
        var gt = t.LastIndexOf('>');
        return lt >= 0 && gt > lt ? t.Substring(lt + 1, gt - lt - 1) : t;
    }

    private static string FromQueryAttr(string typeName) =>
        IsCollectionTypeName(typeName) ? "[Microsoft.AspNetCore.Mvc.FromQuery] " : "";

    // The minimal-API-bindable parameter type: collections → element[]; everything else unchanged.
    private static string QueryParamBindType(string typeName) =>
        IsCollectionTypeName(typeName) ? CollectionElementType(typeName) + "[]" : typeName;

    // Convert the bound array back to the DTO's declared collection type in the object initializer.
    private static string CollectionInitSuffix(string typeName)
    {
        var t = typeName.TrimEnd('?');
        if (!IsCollectionTypeName(t)) return "";
        if (t.EndsWith("[]")) return "";                                               // DTO is array
        if (t.Contains(".IEnumerable<") || t.Contains(".IReadOnlyCollection<")) return ""; // array satisfies these
        return "?.ToList()";                                                            // List/IList/ICollection/IReadOnlyList
    }

    private void RenderQueryInstantiation()
    {
        var initProps = new List<string>();

        // Route parameters
        foreach (var param in _model.RouteParameters)
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // Required query string parameters
        foreach (var param in _model.QueryParameters.Where(p => p.IsRequired && !p.IsComplexFilter))
            initProps.Add($"{param.PropertyName} = {param.Name}{CollectionInitSuffix(param.TypeName)}");

        // Optional query parameters — included in the object initializer, because query properties use
        // init setters and cannot be assigned afterwards.
        //
        // The `?? default` is what keeps a declared initializer alive. An endpoint writes
        // `if (x is not null) endpoint.Prop = x;` and the initializer survives on its own; a query
        // cannot, so without it an absent parameter would assign null straight over it. `Outcome =
        // Pending` would then mean "every outcome", which is the opposite of what it says, and a listing
        // meant to show a review queue would show the whole history.
        foreach (var param in _model.QueryParameters.Where(p => !p.IsRequired && !p.IsComplexFilter))
        {
            var declared = param.DefaultValueExpression is { } expression ? $" ?? {expression}" : "";
            initProps.Add(
                $"{param.PropertyName} = {param.Name}{CollectionInitSuffix(param.TypeName)}{declared}");
        }

        // [ComplexFilter] parameters — deserialize from JSON query string (static options to avoid per-request allocation)
        foreach (var param in _model.QueryParameters.Where(p => p.IsComplexFilter))
        {
            var filterType = param.ComplexFilterTypeName!.TrimEnd('?');
            initProps.Add(
                $"{param.PropertyName} = {param.Name} is not null ? global::System.Text.Json.JsonSerializer.Deserialize("
                + $"{param.Name}, global::Pragmatic.Endpoints.Binding.RequestJson.TypeInfo<{filterType}>(httpContext)) : null");
        }

        // Required claims and cookies, and optional ones on init properties
        initProps.AddRange(ClaimBindingHelper.InitializerEntries(_model));
        initProps.AddRange(CookieBindingHelper.InitializerEntries(_model));

        // The canonical grid request, from the body
        if (_model.GridRequestPropertyName is { } gridProperty)
            initProps.Add($"{gridProperty} = grid");

        // Paging parameters
        if (_model.QueryIsPaged)
        {
            initProps.Add("Page = page");
            initProps.Add("PageSize = pageSize");
        }

        if (initProps.Count == 0)
        {
            AppendLine($"var query = new {_model.FullTypeName}();");
        }
        else if (initProps.Count <= 3)
        {
            var props = string.Join(", ", initProps);
            AppendLine($"var query = new {_model.FullTypeName} {{ {props} }};");
        }
        else
        {
            AppendLine($"var query = new {_model.FullTypeName}");
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

        foreach (var param in _model.QueryParameters.Where(p => p.IsRequired))
            parameters.Add(QueryParameter(param, required: true));

        // A grid request is not a scalar and does not fit a query string, so it arrives as the body —
        // the same shape a mutation's does, through the same renderer.
        if (_model.GridRequestPropertyName is not null)
            parameters.Add(new BoundParameter(
                "global::Pragmatic.Persistence.Query.Adapters.GridFilterRequest", "grid", BindingSource.Body));

        // No IQueryExecutor and no DbContext here. The read is the invoker's, and it resolves
        // both from the request's provider — binding them here would declare two dependencies this
        // handler does not use, and a keyed context whose key would then exist in two places.

        if (_model.HasFilterOverrides)
            parameters.Add(new BoundParameter(
                "global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle", "filterToggle", BindingSource.Service));

        // Unconditional. Added only for the cases that name it — claims, single results and complex
        // filters — anything else that needs the request services would fail to compile for every
        // list query. It is also what the resource authorizer below is resolved from, and that check
        // applies to every shape. An unused lambda
        // parameter costs nothing and does not warn.
        parameters.Add(new BoundParameter(
            "Microsoft.AspNetCore.Http.HttpContext", "httpContext", BindingSource.HttpContext));

        parameters.Add(new BoundParameter(
            "System.Threading.CancellationToken", "ct", BindingSource.CancellationToken));

        foreach (var param in _model.QueryParameters.Where(p => !p.IsRequired))
            parameters.Add(QueryParameter(param, required: false));

        // Paging defaults exist because the caller may omit both and still expect a first page. They
        // are the framework's only where the query says nothing: a query that writes `PageSize = 50` has
        // said something, and answering 20 would make its own declaration a decoration.
        if (_model.QueryIsPaged)
        {
            parameters.Add(new BoundParameter(
                "int", "page", BindingSource.Query, "page", BindKind.Parsable, IsRequired: false,
                DefaultValue: _model.PageDefault ?? "1"));
            parameters.Add(new BoundParameter(
                "int", "pageSize", BindingSource.Query, "pageSize", BindKind.Parsable, IsRequired: false,
                DefaultValue: _model.PageSizeDefault ?? "20"));
        }

        return parameters;
    }

    /// <summary>One query parameter, as an array when the target is a collection.</summary>
    private static BoundParameter QueryParameter(Models.QueryParameterModel param, bool required)
    {
        if (IsCollectionTypeName(param.TypeName))
        {
            var arrayType = CollectionElementType(param.TypeName) + "[]";
            return new BoundParameter(
                required ? arrayType : arrayType + "?", param.Name, BindingSource.QueryMany, param.Name,
                BindKind.Complex, required);
        }

        var typeName = required || param.TypeName.EndsWith("?") ? param.TypeName : param.TypeName + "?";
        return new BoundParameter(typeName, param.Name, BindingSource.Query, param.Name, param.BindKind, required);
    }
}
