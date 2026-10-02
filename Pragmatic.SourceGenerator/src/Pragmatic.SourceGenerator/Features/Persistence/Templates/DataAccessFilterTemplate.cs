using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a combined DataAccessFilter for entities with BOTH [HasOwner] and [HasAccessScopes].
///     Uses OR logic: the user sees the record if they own it OR if their scopes overlap.
///     Replaces separate OwnershipFilter and ScopedDataFilter when both traits are present.
/// </summary>
internal sealed class DataAccessFilterTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public DataAccessFilterTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"DataAccessFilter for {_model.TypeName}";
    protected override string? TriggerInfo => $"[HasOwner]+[HasAccessScopes] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "DataAccessFilter", _model.Namespace),
            ToSourceText());
    }

    // Only generate when BOTH OwnedEntity and ScopedEntity are present
    protected override bool Validate() =>
        _model is { IsValid: true, IsOwnedEntity: true, IsScopedEntity: true };

    public override void RenderFile()
    {
        AddUsing("System.Linq");
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Filters");
        AddUsing("Pragmatic.Authorization");
        AddUsing("Pragmatic.Identity");

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
                $"Auto-generated combined data access filter for {_model.TypeName}. " +
                $"Allows access if the user owns the record OR their scopes overlap. " +
                $"Users with \"{bypassPermission}\" permission bypass this filter. Priority 200.");

            AppendLine($"public sealed class DataAccessFilter(global::Pragmatic.Authorization.IUserScopeResolver scopeResolver, global::Pragmatic.Identity.ICurrentUser currentUser) : global::Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<{entityType}>, global::Pragmatic.Persistence.Query.Filters.IScopeVisibilityFilter");
            Block(() =>
            {
                ExpressionProperty("BypassPermission", "string", $"\"{bypassPermission}\"");
                AppendLine();

                ExpressionProperty("Priority", "int", "200");
                AppendLine();

                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                XmlSummary("Returns the combined ownership + scoped data filter expression (OR logic).");
                AppendLine($"public global::System.Linq.Expressions.Expression<global::System.Func<{entityType}, bool>> GetFilter()");
                Block(() =>
                {
                    AppendLine("var userScopes = scopeResolver.ResolveAccessScopes(currentUser);");
                    AppendLine("var userId = currentUser.Id;");
                    AppendLine($"return entity => entity.OwnerId == userId || entity.AccessScopes.Any(s => userScopes.Contains(s));");
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
