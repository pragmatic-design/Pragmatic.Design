using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates an autocomplete endpoint for a property marked with [Autocomplete].
///     Uses IReadRepository to ensure query filters (soft-delete, tenant, temporal) are applied.
/// </summary>
internal sealed class AutocompleteEndpointTemplate : CSharpTemplate
{
    private readonly AutocompleteModel _model;

    public AutocompleteEndpointTemplate(AutocompleteModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.EndpointClassName} from {_model.EntityNamespace}";
    protected override string? TriggerInfo => $"[Autocomplete] on {_model.EntityTypeName}.{_model.PropertyName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.EndpointClassName, "Autocomplete", _model.EntityNamespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Http");
        AddUsing("Microsoft.AspNetCore.Routing");
        AddUsing("System.Linq");

        AppendNamespace(_model.EntityNamespace);
        AppendLine();

        XmlSummary(
            $"Auto-generated autocomplete endpoint for <c>{_model.EntityTypeName}.{_model.PropertyName}</c>.");

        Class(_model.EndpointClassName, RenderClassBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Maps this autocomplete endpoint to the route builder.");
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
            // Repository.Query() applies all filters (soft-delete, tenant, temporal)
            AppendLine("var query = repository.Query();");
            AppendLine();

            // Apply search filter
            AppendLine("if (!string.IsNullOrEmpty(search))");
            Block(() =>
            {
                AppendLine(
                    $"query = query.Where(e => e.{_model.PropertyName}.Contains(search));");
            });
            AppendLine();

            if (_model.HasCustomDto)
                RenderDtoProjection();
            else
                RenderDefaultProjection();

            AppendLine();
            AppendLine("return Microsoft.AspNetCore.Http.Results.Ok(results);");
        });

        AppendLine(";");
        AppendLine();

        AppendLine($"var builder = endpoints.MapGet(\"{StringHelper.CSharpLiteral(_model.Route)}\", (Microsoft.AspNetCore.Http.RequestDelegate)(async httpContext =>");
        Block(() =>
        {
            RequestDelegateRenderer.RenderBinding(parameters, AppendLine);
            AppendLine();
            AppendLine($"var __result = await handler({RequestDelegateRenderer.RenderArguments(parameters)}).ConfigureAwait(false);");
            AppendLine("await __result.ExecuteAsync(httpContext).ConfigureAwait(false);");
        });
        AppendLine("));");
        AppendLine(EndpointMetadataRenderer.RenderRequestDescription("builder", parameters));
        AppendLine();

        // Configure the endpoint
        RenderEndpointConfiguration();

        AppendLine();
        AppendLine("return builder;");
    }

    /// <summary>
    ///     Default mode: project to AutocompleteItem&lt;TKey&gt; with typed Id.
    /// </summary>
    private void RenderDefaultProjection()
    {
        var keyFqn = _model.KeyPropertyTypeName;
        AppendLine($"var results = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        IncreaseIndent();
        AppendLine($"query.Take(limit ?? {_model.DefaultLimit})");
        AppendLine(
            $"    .Select(e => new global::Pragmatic.Endpoints.Responses.AutocompleteItem<{keyFqn}>(e.{_model.KeyPropertyName}, e.{_model.PropertyName})),");
        AppendLine("ct);");
        DecreaseIndent();
    }

    /// <summary>
    ///     DTO mode: materialize filtered entities, then map via FromEntity.
    ///     Capped by Take(limit) so in-memory mapping is negligible cost.
    /// </summary>
    private void RenderDtoProjection()
    {
        AppendLine($"var entities = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        IncreaseIndent();
        AppendLine($"query.Take(limit ?? {_model.DefaultLimit}),");
        AppendLine("ct);");
        DecreaseIndent();
        AppendLine();
        AppendLine($"var results = entities.Select({_model.DtoTypeFullName}.FromEntity).ToList();");
    }

    private void RenderEndpointConfiguration()
    {
        AppendLine($"builder.WithName(\"{StringHelper.CSharpLiteral(_model.EndpointName)}\");");

        // Default tag from entity type name (consistent PascalCase with mutation/query tags)
        var defaultTag = EndpointTagHelper.DeriveTagFromRoute(_model.Route, _model.EntityTypeName);
        if (defaultTag is not null)
            AppendLine($"builder.WithTags(\"{defaultTag}\");");

        // The read permission is ENFORCED, in the same shape mutations, queries and domain actions use.
        // WithMetadata(RequirePermissionAttribute) would not enforce it: that attribute is not
        // IAuthorizeData and nothing reads it off the route metadata, so the endpoint would declare a
        // permission it does not require, and any authenticated caller could autocomplete any exposed
        // entity.
        // The permission is derived, not declared, so an application cannot opt out of it: in a host
        // with no authentication the route answers 500 (authorization metadata, no middleware).
        // Enforcement is kept — dropping it would hand every authenticated caller every exposed
        // entity — and the host reports the situation at build time (PRAG1695, PRAG1692) instead of
        // leaving it as a runtime mystery.
        if (_model.EntityReadPermission is not null)
            RenderPermissionRequirement(_model.EntityReadPermission);

        if (_model.HasCustomDto)
        {
            AppendLine(
                EndpointMetadataRenderer.RenderProduces("builder", $"System.Collections.Generic.List<{_model.DtoTypeFullName}>", 200));
        }
        else
        {
            var keyFqn = _model.KeyPropertyTypeName;
            AppendLine(
                EndpointMetadataRenderer.RenderProduces("builder", $"System.Collections.Generic.List<global::Pragmatic.Endpoints.Responses.AutocompleteItem<{keyFqn}>>", 200));
        }
    }

    /// <summary>
    ///     The read permission, in the one enforcement shape there is — the same
    ///     <c>DomainActionHandlerTemplate</c>, <c>MutationHandlerTemplate</c> and
    ///     <c>QueryHandlerTemplate</c> emit, so an autocomplete route is gated exactly like the others.
    /// </summary>
    private void RenderPermissionRequirement(string permission)
    {
        var permArray = $"\"{permission}\"";

        AppendLine("builder.RequireAuthorization(policy => policy.AddRequirements(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        IncreaseIndent();
        AppendLine($"new[] {{ {permArray} }},");
        AppendLine("global::Pragmatic.Endpoints.Authorization.PermissionMode.All)));");
        DecreaseIndent();
        DecreaseIndent();
    }

    /// <summary>The handler's parameters, and where each value comes from.</summary>
    private List<BoundParameter> BuildBoundParameters()
    {
        var entityFqn = $"global::{_model.EntityTypeFullName}";
        var repoType = $"global::Pragmatic.Persistence.Repository.IRepository<{entityFqn}>";

        return
        [
            new BoundParameter("string?", "search", BindingSource.Query, "search", BindKind.String, IsRequired: false),
            new BoundParameter("int?", "limit", BindingSource.Query, "limit", BindKind.Parsable, IsRequired: false),
            // Repos are registered non-keyed (IRepository<T>), so no service key here.
            new BoundParameter(repoType, "repository", BindingSource.Service),
            new BoundParameter("System.Threading.CancellationToken", "ct", BindingSource.CancellationToken),
        ];
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
