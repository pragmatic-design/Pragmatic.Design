// Pragmatic.SourceGenerator - Composition - Exposed Endpoint Handler Template

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Templates;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates a static endpoint handler class for an [ExposeEndpoint&lt;T&gt;] declaration.
///     Each handler exposes a MapEndpoint method that registers the endpoint with ASP.NET Core minimal APIs.
/// </summary>
internal sealed class ExposedEndpointHandlerTemplate : CSharpTemplate
{
    private readonly ActionKind _actionKind;
    private readonly string? _entityTypeFqn;
    private readonly ExposedEndpointModel _ep;
    private readonly string? _returnTypeFqn;
    private readonly string _rootNamespace;

    public ExposedEndpointHandlerTemplate(
        ExposedEndpointModel ep,
        ActionKind actionKind,
        string? returnTypeFqn,
        string? entityTypeFqn,
        string rootNamespace)
    {
        _ep = ep;
        _actionKind = actionKind;
        _returnTypeFqn = returnTypeFqn;
        _entityTypeFqn = entityTypeFqn;
        _rootNamespace = rootNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput()
    {
        var boundary = _ep.HostBoundaryName ?? "Host";
        return new Artifact($"Endpoint.{_ep.ActionSimpleName}.{boundary}.g.cs", ToSourceText());
    }

    public override void RenderFile()
    {
        AppendNamespace($"{_rootNamespace}.Endpoints");
        AppendLine();

        XmlSummary($"Generated endpoint handler for {_ep.ActionSimpleName} ({_ep.HttpVerb} {_ep.Route}).");

        Class($"{_ep.ActionSimpleName}EndpointHandler", RenderClassBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Maps this endpoint to the given route builder.");
        XmlParam("endpoints", "The endpoint route builder (or a MapGroup result).");
        XmlReturns("The route handler builder for further configuration.");

        var parameters = new List<MethodParameter>
        {
            new("global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "endpoints")
        };

        Method("MapEndpoint", RenderMapEndpointBody,
            "global::Microsoft.AspNetCore.Builder.IEndpointConventionBuilder",
            parameters,
            AccessModifier.Internal,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderMapEndpointBody()
    {
        // Escape once: author-controlled route flows into a Map{Verb}("…") literal in every body.
        var route = StringHelper.CSharpLiteral(_ep.Route);
        // Head/Options have no MapX extension → MapMethods (see MapInvocationHelper).
        var mapInvocation = MapInvocationHelper.Render(_ep.HttpVerb, route);

        switch (_actionKind)
        {
            case ActionKind.DomainAction:
                RenderDomainActionBody(mapInvocation);
                break;
            case ActionKind.VoidDomainAction:
                RenderVoidDomainActionBody(mapInvocation);
                break;
            case ActionKind.Mutation:
                RenderMutationBody(mapInvocation);
                break;
        }

        AppendLine();

        // Authorization: AllowAnonymous overrides everything
        if (_ep.AllowAnonymous)
        {
            AppendLine("builder.AllowAnonymous();");
        }
        else
        {
            // Combine action's own permissions + additional permissions from [ExposeEndpoint]
            var combinedPermissions = CombinePermissions(_ep.ActionPermissions.AsImmutableArray(), _ep.AdditionalPermissions.AsImmutableArray());
            if (combinedPermissions.Length > 0)
                RenderPermissionRequirement(combinedPermissions);
        }

        // Which package operation this is, whatever route the application gave it: a package that has to
        // recognise its own operation on the wire — the login rate limit — reads this, not the path.
        AppendLine($"builder.WithMetadata(new global::Pragmatic.Http.ExposedOperationMetadata(typeof({_ep.ActionTypeName})));");

        // Endpoint name
        if (_ep.Name is not null)
            AppendLine($"builder.WithName(\"{StringHelper.CSharpLiteral(_ep.Name)}\");");

        // OpenAPI tag derived from package assembly name (e.g. "Pragmatic.Identity.Local" → "Identity")
        var tagSegment = DeriveTagFromAssembly(_ep.ActionAssemblyName);
        if (tagSegment is not null)
            AppendLine($"builder.WithTags(\"{tagSegment}\");");

        AppendLine();
        AppendLine("return builder;");
    }

    private void RenderDomainActionBody(string mapInvocation)
        => RenderExposedBody(
            mapInvocation,
            $"global::Pragmatic.Actions.Invoker.IDomainActionInvoker<{_ep.ActionTypeName}, {_returnTypeFqn}>",
            "success => global::Microsoft.AspNetCore.Http.Results.Ok(success),");

    private void RenderVoidDomainActionBody(string mapInvocation)
        => RenderExposedBody(
            mapInvocation,
            $"global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<{_ep.ActionTypeName}>",
            "() => global::Microsoft.AspNetCore.Http.Results.NoContent(),");

    private void RenderMutationBody(string mapInvocation)
        => RenderExposedBody(
            mapInvocation,
            $"global::Pragmatic.Actions.Invoker.IMutationInvoker<{_ep.ActionTypeName}, {_entityTypeFqn}>",
            "success => global::Microsoft.AspNetCore.Http.Results.Ok(success),");

    /// <summary>
    ///     The handler, then the <c>RequestDelegate</c> that binds its arguments and calls it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The three exposed shapes differ only in the invoker and in what success becomes, so they
    ///         share this. They were three copies of the same twenty lines, and the copies would have
    ///         had to be converted three times.
    ///     </para>
    ///     <para>
    ///         A handler <c>Delegate</c> cannot be bound under AOT: ASP.NET's binder is reflection-based
    ///         and its compile-time replacement cannot see another generator's output.
    ///     </para>
    /// </remarks>
    private void RenderExposedBody(string mapInvocation, string invokerType, string successBranch)
    {
        // ⚠️ A verb without a body binds its inputs from the route and the query string. Reading the
        // action from the request body whatever the verb would answer 415 to an exposed GET after the
        // route mapped and authorization ran: the action would never run.
        var bodyless = Transforms.ModuleTransform.VerbCarriesNoBody(_ep.HttpVerb) && !_ep.Inputs.IsDefaultOrEmpty;

        var parameters = new List<BoundParameter>
        {
            new(_ep.ActionTypeName, "action", bodyless ? BindingSource.HttpContext : BindingSource.Body),
            new(invokerType, "invoker", BindingSource.Service),
            // HttpContext so the error path can reach an IErrorMessageResolver.
            new("global::Microsoft.AspNetCore.Http.HttpContext", "httpContext", BindingSource.HttpContext),
            new("global::System.Threading.CancellationToken", "ct", BindingSource.CancellationToken),
        };

        // What the binding block actually reads: the action's own inputs in place of the body.
        var bound = bodyless
            ? _ep.Inputs.Select(InputParameter).Concat(parameters.Skip(1)).ToList()
            : parameters;

        AppendLine("var handler = async (");
        RequestDelegateRenderer.RenderSignature(parameters, AppendLine);
        AppendLine(") =>");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var result = await invoker.InvokeAsync(action, ct).ConfigureAwait(false);");
        AppendLine("return result.Match(");
        IncreaseIndent();
        AppendLine(successBranch);
        AppendLine("error => global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(error, httpContext));");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        AppendLine($"var builder = endpoints.{mapInvocation}(global::Microsoft.AspNetCore.Http.RequestDelegate)(async httpContext =>");
        Block(() =>
        {
            RequestDelegateRenderer.RenderBinding(bound, AppendLine);

            if (bodyless)
            {
                // Built from what was bound, not deserialized, and named `action` because that is what
                // the body path's last line produces — the handler call below reads the same either way.
                //
                // ⚠️ Only the REQUIRED inputs go in the initializer. An optional one that the caller
                // omitted must leave the action's own default standing: GetConfigValues declares
                // `Limit = 500`, and assigning the unbound `null` would turn every call into a dump of
                // one entry — or not compile, for a value type.
                AppendLine();
                AppendLine($"var action = new {_ep.ActionTypeName}");
                Block(() =>
                {
                    foreach (var input in _ep.Inputs.Where(i => i.IsRequired && !i.IsNullable))
                        AppendLine($"{input.Name} = {ToCamelCase(input.Name)},");
                });
                AppendLine(";");

                foreach (var input in _ep.Inputs.Where(i => !i.IsRequired || i.IsNullable))
                {
                    var local = ToCamelCase(input.Name);
                    AppendLine($"if ({local} is {{ }} __bound_{local}) action.{input.Name} = __bound_{local};");
                }
            }

            AppendLine();
            AppendLine($"var __result = await handler({RequestDelegateRenderer.RenderArguments(parameters)}).ConfigureAwait(false);");
            AppendLine("await __result.ExecuteAsync(httpContext).ConfigureAwait(false);");
        });
        AppendLine("));");
        AppendLine(EndpointMetadataRenderer.RenderRequestDescription("builder", bound));
    }

    /// <summary>One of the action's inputs, as a query-string parameter of the generated handler.</summary>
    /// <remarks>
    ///     The property's own name is the key on the wire, which is what a caller of an exposed
    ///     package action has to guess from its documentation — and what the ordinary endpoint path
    ///     uses for a query parameter it was not given a name for.
    /// </remarks>
    private static BoundParameter InputParameter(ExposedInputModel input)
        => new(
            input.IsNullable ? input.TypeName : $"{input.TypeName}?",
            ToCamelCase(input.Name),
            BindingSource.Query,
            input.Name,
            Endpoints.Models.BindKindNames.FromTypeName(input.TypeName),
            IsRequired: input.IsRequired && !input.IsNullable);

    private static string ToCamelCase(string name)
        => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static ImmutableArray<string> CombinePermissions(
        ImmutableArray<string> actionPermissions,
        ImmutableArray<string> additionalPermissions)
    {
        var hasAction = !actionPermissions.IsDefaultOrEmpty;
        var hasAdditional = !additionalPermissions.IsDefaultOrEmpty;

        if (!hasAction && !hasAdditional)
            return ImmutableArray<string>.Empty;
        if (hasAction && !hasAdditional)
            return actionPermissions;
        if (!hasAction && hasAdditional)
            return additionalPermissions;

        // Merge both, deduplicated
        return actionPermissions
            .Concat(additionalPermissions)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Derives an OpenAPI tag from the package assembly name.
    ///     "Pragmatic.Identity.Local" → "Identity", "Pragmatic.Billing" → "Billing".
    /// </summary>
    private static string? DeriveTagFromAssembly(string assemblyName)
    {
        // Strip "Pragmatic." prefix and ".Local"/".Core"/".AspNetCore" suffixes
        var parts = assemblyName.Split('.');
        foreach (var part in parts)
        {
            if (part is "Pragmatic" or "Local" or "Core" or "AspNetCore" or "Host")
                continue;
            return part;
        }

        return null;
    }

    private void RenderPermissionRequirement(ImmutableArray<string> permissions)
    {
        var permArray = string.Join(", ", permissions.Select(p => $"\"{p}\""));

        AppendLine("builder.RequireAuthorization(policy => policy.AddRequirements(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        IncreaseIndent();
        AppendLine($"new[] {{ {permArray} }},");
        AppendLine("global::Pragmatic.Endpoints.Authorization.PermissionMode.All)));");
        DecreaseIndent();
        DecreaseIndent();
    }
}
