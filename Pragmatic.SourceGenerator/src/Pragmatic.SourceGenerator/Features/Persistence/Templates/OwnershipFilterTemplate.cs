using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a nested OwnershipFilter class implementing IPermissionBasedFilter{T}
///     for each entity with [HasOwner]. Users with the bypass permission see all records.
/// </summary>
internal sealed class OwnershipFilterTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public OwnershipFilterTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"OwnershipFilter for {_model.TypeName}";
    protected override string? TriggerInfo => $"[HasOwner] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "OwnershipFilter", _model.Namespace),
            ToSourceText());
    }

    // Only generate when OwnedEntity alone (not combined with ScopedEntity — uses DataAccessFilter then)
    protected override bool Validate() => _model is { IsValid: true, IsOwnedEntity: true, IsScopedEntity: false };

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Filters");
        AddUsing("Pragmatic.Identity");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";
        var bypassPermission = BuildBypassPermission();

        // Nested inside partial entity class
        Class(_model.TypeName, () =>
        {
            XmlSummary(
                $"Auto-generated ownership filter for {_model.TypeName}. " +
                $"Restricts queries to records owned by the current user. " +
                $"Users with \"{bypassPermission}\" permission bypass this filter. Priority 200.");

            // Primary constructor with ICurrentUser
            AppendLine($"public sealed class OwnershipFilter(global::Pragmatic.Identity.ICurrentUser currentUser) : global::Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<{entityType}>");
            Block(() =>
            {
                // BypassPermission property
                ExpressionProperty("BypassPermission", "string", $"\"{bypassPermission}\"");
                AppendLine();

                // Priority property
                ExpressionProperty("Priority", "int", "200");
                AppendLine();

                // Scope property
                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                // GetFilter method
                XmlSummary("Returns the ownership filter expression.");
                ExpressionMethod("GetFilter",
                    "entity => entity.OwnerId == currentUser.Id",
                    $"global::System.Linq.Expressions.Expression<global::System.Func<{entityType}, bool>>");
            });
        },
        accessModifier: ParseAccessibility(_model.Accessibility),
        modifiers: new ClassModifiers { Partial = true });
    }

    /// <summary>
    ///     Builds the bypass permission string: "{boundary}.{entity-kebab}.view-all".
    ///     If no boundary, uses just "{entity-kebab}.view-all".
    /// </summary>
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
