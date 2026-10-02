using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a nested SoftDeleteFilter class implementing IQueryFilter{T}
///     for each entity with [SoftDelete]. Applied automatically by the query pipeline.
/// </summary>
internal sealed class SoftDeleteFilterTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public SoftDeleteFilterTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"SoftDeleteFilter for {_model.TypeName}";
    protected override string? TriggerInfo => $"[SoftDelete] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "SoftDeleteFilter", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model is { IsValid: true, IsSoftDelete: true };

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Filters");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";

        // Nested inside partial entity class
        Class(_model.TypeName, () =>
        {
            XmlSummary($"Auto-generated soft-delete filter for {_model.TypeName}. Excludes entities where IsDeleted is true. Priority 100 (applied first).");

            Class("SoftDeleteFilter", () =>
            {
                // Priority property
                ExpressionProperty("Priority", "int", "100");
                AppendLine();

                // Scope property
                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                // GetFilter method
                XmlSummary("Returns the soft-delete filter expression.");
                ExpressionMethod("GetFilter",
                    $"entity => !entity.IsDeleted",
                    $"global::System.Linq.Expressions.Expression<global::System.Func<{entityType}, bool>>");
            },
            interfaces: [$"global::Pragmatic.Persistence.Query.Filters.IQueryFilter<{entityType}>"],
            modifiers: new ClassModifiers { Sealed = true });
        },
        accessModifier: ParseAccessibility(_model.Accessibility),
        modifiers: new ClassModifiers { Partial = true });
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
