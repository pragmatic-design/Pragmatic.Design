using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a nested ScopedDataFilter class implementing IPermissionBasedFilter{T}
///     for entities with [HasAccessScopes] only (NOT combined with [HasOwner]).
///     When both are present, <see cref="DataAccessFilterTemplate"/> generates the combined OR filter.
/// </summary>
internal sealed class ScopedDataFilterTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public ScopedDataFilterTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"ScopedDataFilter for {_model.TypeName}";
    protected override string? TriggerInfo => $"[HasAccessScopes] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "ScopedDataFilter", _model.Namespace),
            ToSourceText());
    }

    // Only generate when ScopedEntity alone (not combined with OwnedEntity)
    protected override bool Validate() =>
        _model is { IsValid: true, IsScopedEntity: true, IsOwnedEntity: false };

    public override void RenderFile()
    {
        AddUsing("System.Linq");
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Filters");
        AddUsing("Pragmatic.Authorization");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";
        var bypassPermission = BuildBypassPermission();

        Class(_model.TypeName, () =>
        {
            XmlSummary(
                $"Auto-generated scoped data filter for {_model.TypeName}. " +
                $"Restricts queries to records whose AccessScopes overlap with the user's scopes. " +
                $"Users with \"{bypassPermission}\" permission bypass this filter. Priority 250.");

            // Primary constructor with IUserScopeResolver and ICurrentUser
            AppendLine($"public sealed class ScopedDataFilter(global::Pragmatic.Authorization.IUserScopeResolver scopeResolver, global::Pragmatic.Identity.ICurrentUser currentUser) : global::Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<{entityType}>, global::Pragmatic.Persistence.Query.Filters.IScopeVisibilityFilter");
            Block(() =>
            {
                ExpressionProperty("BypassPermission", "string", $"\"{bypassPermission}\"");
                AppendLine();

                ExpressionProperty("Priority", "int", "250");
                AppendLine();

                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                XmlSummary("Returns the scoped data filter expression.");
                AppendLine($"public global::System.Linq.Expressions.Expression<global::System.Func<{entityType}, bool>> GetFilter()");
                Block(() =>
                {
                    AppendLine("var userScopes = scopeResolver.ResolveAccessScopes(currentUser);");
                    AppendLine($"return entity => entity.AccessScopes.Any(s => userScopes.Contains(s));");
                });
            });
        },
        accessModifier: ParseAccessibility(_model.Accessibility),
        modifiers: new ClassModifiers { Partial = true });
    }

    private string BuildBypassPermission()
        => Core.PermissionNaming.ViewAllPermission(_model.BoundaryName, _model.TypeName);

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
