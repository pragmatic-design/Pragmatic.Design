using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates virtual navigation extension methods on parent entities
///     for typed temporal relations ([TemporalRelation&lt;TParent&gt;]).
///     Produces QueryActive{X}s(repo), Query{X}History(repo), and batch methods.
/// </summary>
internal sealed class TemporalVirtualNavigationTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TemporalVirtualNavigationTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/VirtualNavigation";
    protected override string? SourceInfo => $"TemporalVirtualNav for {_model.TemporalParentTypeName} → {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation<{_model.TemporalParentTypeName}>] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        // Early return to avoid null-dereference on TemporalParentTypeName in hint name
        if (!_model.IsTypedTemporalRelation || _model.TemporalParentFkProperty is null)
            return new Artifact("_invalid", ToSourceText());

        return new(
            VirtualFolderHints.ForType(_model.TemporalParentTypeName!,
                NamingHelper.AppendSuffix(_model.TypeName, "Navigation"), _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() =>
        _model is { IsValid: true, IsTypedTemporalRelation: true, TemporalParentFkProperty: not null };

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");

        // Use the temporal entity's namespace (parent may be in different assembly)
        AppendNamespace(_model.Namespace);
        AppendLine();

        var className = $"{_model.TemporalParentTypeName}{_model.TypeName}NavigationExtensions";
        XmlSummary($"Virtual navigation extensions for loading {_model.TypeName} temporal relations from {_model.TemporalParentTypeName}.");

        Class(className, RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethods()
    {
        var entityType = $"global::{_model.FullTypeName}";
        var parentType = $"global::{_model.TemporalParentTypeFullName}";
        var parentFk = _model.TemporalParentFkProperty!;
        var repoType = $"global::Pragmatic.Persistence.Repository.IReadRepository<{entityType}>";
        var queryableType = $"global::System.Linq.IQueryable<{entityType}>";

        // Detect parent Id type from the FK property (ensure global:: prefix)
        var fkProp = _model.Properties.FirstOrDefault(p => p.Name == parentFk);
        var rawIdType = fkProp?.TypeName ?? "System.Guid";
        var parentIdType = rawIdType.StartsWith("global::") ? rawIdType : $"global::{rawIdType}";

        var pluralName = NamingHelper.AppendSuffix(_model.TypeName, "s");

        // QueryActive{X}s — returns IQueryable of currently active relations for this parent
        XmlSummary($"Returns a queryable of currently active {_model.TypeName} records for this {_model.TemporalParentTypeName}.");
        XmlParam("parent", $"The {_model.TemporalParentTypeName} instance.");
        XmlParam("repository", $"The {_model.TypeName} repository.");

        XmlParam("timeProvider", "The clock to read \"now\" from. Defaults to the system clock.");

        var activeParams = new List<MethodParameter>
        {
            new($"this {parentType}", "parent"),
            new(repoType, "repository"),
            new("global::System.TimeProvider?", "timeProvider") { DefaultValue = "null" }
        };

        // UtcNow captured app-side (parameterized) to avoid app↔DB clock skew — see TemporalFilterTemplate.
        // The clock is a parameter so "what is active" can be asked about an instant instead of only
        // about the moment the caller runs (PRAG0900).
        Method($"QueryActive{pluralName}", () =>
        {
            AppendLine("var now = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow;");
            AppendLine($"return repository.Query().Where(e => e.{parentFk} == parent.Id).Where(e => e.ValidFrom <= now && (e.ValidTo == null || e.ValidTo > now));");
        },
            queryableType,
            activeParams,
            modifiers: new MethodModifiers { IsStatic = true });

        AppendLine();

        // Query{X}History — returns IQueryable of ALL relations (including expired)
        XmlSummary($"Returns a queryable of all {_model.TypeName} records (including history) for this {_model.TemporalParentTypeName}.");
        XmlParam("parent", $"The {_model.TemporalParentTypeName} instance.");
        XmlParam("repository", $"The {_model.TypeName} repository.");

        var historyParams = new List<MethodParameter>
        {
            new($"this {parentType}", "parent"),
            new(repoType, "repository")
        };

        ExpressionMethod($"Query{_model.TypeName}History",
            $"repository.Query().Where(e => e.{parentFk} == parent.Id)",
            queryableType,
            historyParams,
            modifiers: new MethodModifiers { IsStatic = true });

        AppendLine();

        // Batch: LoadActive{X}sBatchAsync
        XmlSummary($"Loads currently active {_model.TypeName} records for multiple {_model.TemporalParentTypeName} IDs.");
        XmlParam("repository", $"The {_model.TypeName} repository.");
        XmlParam("parentIds", $"The {_model.TemporalParentTypeName} IDs.");
        XmlParam("ct", "Cancellation token.");
        XmlParam("timeProvider", "The clock to read \"now\" from. Defaults to the system clock.");

        // The clock goes after ct, not before it: ct is passed positionally by callers that already
        // exist, and inserting a parameter ahead of it would move their argument silently.
        var batchParams = new List<MethodParameter>
        {
            new(repoType, "repository"),
            new($"global::System.Collections.Generic.IEnumerable<{parentIdType}>", "parentIds"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" },
            new("global::System.TimeProvider?", "timeProvider") { DefaultValue = "null" }
        };

        Method($"LoadActive{pluralName}BatchAsync", RenderBatchBody,
            $"async global::System.Threading.Tasks.Task<global::System.Linq.ILookup<{parentIdType}, {entityType}>>",
            batchParams,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderBatchBody()
    {
        var parentFk = _model.TemporalParentFkProperty!;
        var fkProp = _model.Properties.FirstOrDefault(p => p.Name == parentFk);
        var rawIdType = fkProp?.TypeName ?? "System.Guid";
        var parentIdType = rawIdType.StartsWith("global::") ? rawIdType : $"global::{rawIdType}";

        AppendLine("var idList = parentIds.ToList();");
        // UtcNow captured app-side (parameterized) to avoid app↔DB clock skew — see TemporalFilterTemplate.
        AppendLine("var now = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow;");
        AppendLine($"var items = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        IncreaseIndent();
        AppendLine($"repository.Query()");
        AppendLine($"    .Where(e => idList.Contains(e.{parentFk}))");
        AppendLine($"    .Where(e => e.ValidFrom <= now && (e.ValidTo == null || e.ValidTo > now)),");
        AppendLine("ct);");
        DecreaseIndent();
        AppendLine($"return items.ToLookup(e => e.{parentFk});");
    }
}
