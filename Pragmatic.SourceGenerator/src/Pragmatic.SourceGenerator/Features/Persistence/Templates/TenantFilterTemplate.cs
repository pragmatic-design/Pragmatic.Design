using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a nested TenantFilter class implementing IQueryFilter{T}
///     for each entity implementing ITenantEntity. Applied automatically by the query pipeline.
/// </summary>
internal sealed class TenantFilterTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TenantFilterTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"TenantFilter for {_model.TypeName}";
    protected override string? TriggerInfo => $"ITenantEntity on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "TenantFilter", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model is { IsValid: true, IsTenantEntity: true };

    public override void RenderFile()
    {
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Filters");
        AddUsing("Pragmatic.MultiTenancy");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";

        // Nested inside partial entity class
        Class(_model.TypeName, () =>
        {
            XmlSummary($"Auto-generated tenant filter for {_model.TypeName}. Restricts queries to the current tenant. Priority 200 (after SoftDelete).");

            // Primary constructor with ITenantContext
            AppendLine($"public sealed class TenantFilter(global::Pragmatic.MultiTenancy.ITenantContext tenantContext) : global::Pragmatic.Persistence.Query.Filters.IQueryFilter<{entityType}>, global::Pragmatic.Persistence.Query.Filters.ITenantFilter");
            Block(() =>
            {
                // Priority property
                ExpressionProperty("Priority", "int", "200");
                AppendLine();

                // Scope property — tenant filter should apply everywhere
                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.All");
                AppendLine();

                // GetFilter method. Fail-closed: an unresolved tenant (TenantId == null) matches NO rows
                // instead of leaking every null-tenant row to a request with no tenant.
                XmlSummary("Returns the tenant isolation filter expression (fail-closed on an unresolved tenant).");
                ExpressionMethod("GetFilter",
                    "entity => tenantContext.TenantId != null && entity.TenantId == tenantContext.TenantId",
                    $"global::System.Linq.Expressions.Expression<global::System.Func<{entityType}, bool>>");
            });
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
