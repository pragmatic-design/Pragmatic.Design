using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates temporal query extension methods for entities with [TemporalRelation].
///     Produces: Active(), ActiveAt(date), IncludeHistory() extension methods on IQueryable{T}.
/// </summary>
internal sealed class TemporalQueryExtensionsTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;
    private readonly string _extensionsClassName;

    public TemporalQueryExtensionsTemplate(EntityMetadataModel model)
    {
        _model = model;
        _extensionsClassName = NamingHelper.AppendSuffix(_model.TypeName, "TemporalExtensions");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"TemporalQueryExtensions for {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "TemporalExtensions", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model is { IsValid: true, IsTemporalRelation: true };

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";

        XmlSummary($"Auto-generated temporal query extensions for {_model.TypeName}.");

        Class(_extensionsClassName, () => RenderExtensionMethods(entityType),
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderExtensionMethods(string entityType)
    {
        // Active() — currently active records
        XmlSummary($"Filters to only include currently active {_model.TypeName} records (ValidFrom &lt;= now and ValidTo is null or &gt; now).");
        XmlParam("query", "The queryable source.");
        XmlParam("timeProvider", "The clock to read \"now\" from. Defaults to the system clock.");
        var activeParams = new List<MethodParameter>
        {
            new($"this global::System.Linq.IQueryable<{entityType}>", "query"),
            new("global::System.TimeProvider?", "timeProvider") { DefaultValue = "null" }
        };
        // UtcNow captured app-side (parameterized) rather than translated to the DB now(): ValidFrom/ValidTo
        // are written with the app clock, so comparing against an app-clock "now" avoids app↔DB clock skew.
        // The clock is a parameter for the same reason the framework tells its users to inject one
        // (PRAG0900): read from the wall clock, "what is active" can only be measured by waiting.
        Method("Active", () =>
        {
            AppendLine("var now = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow;");
            AppendLine("return query.Where(e => e.ValidFrom <= now && (e.ValidTo == null || e.ValidTo > now));");
        },
            $"global::System.Linq.IQueryable<{entityType}>",
            activeParams,
            modifiers: new MethodModifiers { IsStatic = true });

        AppendLine();

        // ActiveAt(date) — active at specific date
        XmlSummary($"Filters to only include {_model.TypeName} records active at the specified date.");
        XmlParam("query", "The queryable source.");
        XmlParam("date", "The point in time to check activity against.");
        var activeAtParams = new List<MethodParameter>
        {
            new($"this global::System.Linq.IQueryable<{entityType}>", "query"),
            new("global::System.DateTimeOffset", "date")
        };
        ExpressionMethod("ActiveAt",
            "query.Where(e => e.ValidFrom <= date && (e.ValidTo == null || e.ValidTo > date))",
            $"global::System.Linq.IQueryable<{entityType}>",
            activeAtParams,
            modifiers: new MethodModifiers { IsStatic = true });

        AppendLine();

        // ⚠️ IncludeHistory() is not generated here. As a query extension it could only be a no-op
        // returning `query`: the nested TemporalFilter narrows the read before this method ever sees
        // it, and a method downstream of the query cannot remove what is already in the tree — a term
        // with three stewards would come back with one row, silently.
        //
        // It lives on the entity as a scope — TemporalHistoryScopeTemplate — because lifting the
        // filter has to happen BEFORE the query is built, and the only precise lever
        // (IQueryFilterToggle) is disposable.

        // Typed parent/child extensions (only for [TemporalRelation<TParent, TChild>])
        if (_model.IsTypedTemporalRelation)
        {
            RenderTypedExtensions(entityType);
        }
    }

    private void RenderTypedExtensions(string entityType)
    {
        var queryableType = $"global::System.Linq.IQueryable<{entityType}>";

        // ForParent — if parent FK exists
        if (_model.TemporalParentFkProperty is not null)
        {
            var parentIdType = GetFkPropertyType(_model.TemporalParentFkProperty);

            AppendLine();
            XmlSummary($"Filters to {_model.TypeName} records for the specified {_model.TemporalParentTypeName}.");
            XmlParam("query", "The queryable source.");
            XmlParam("parentId", $"The {_model.TemporalParentTypeName} ID.");
            var forParentParams = new List<MethodParameter>
            {
                new($"this {queryableType}", "query"),
                new(parentIdType, "parentId")
            };
            ExpressionMethod($"For{_model.TemporalParentTypeName}",
                $"query.Where(e => e.{_model.TemporalParentFkProperty} == parentId)",
                queryableType,
                forParentParams,
                modifiers: new MethodModifiers { IsStatic = true });

            AppendLine();
            XmlSummary($"Filters to currently active {_model.TypeName} records for the specified {_model.TemporalParentTypeName}.");
            var activeForParentParams = new List<MethodParameter>
            {
                new($"this {queryableType}", "query"),
                new(parentIdType, "parentId")
            };
            ExpressionMethod($"ActiveFor{_model.TemporalParentTypeName}",
                $"query.For{_model.TemporalParentTypeName}(parentId).Active()",
                queryableType,
                activeForParentParams,
                modifiers: new MethodModifiers { IsStatic = true });
        }

        // ForChild — if child FK exists
        if (_model.TemporalChildFkProperty is not null)
        {
            var childIdType = GetFkPropertyType(_model.TemporalChildFkProperty);

            AppendLine();
            XmlSummary($"Filters to {_model.TypeName} records for the specified {_model.TemporalChildTypeName}.");
            XmlParam("query", "The queryable source.");
            XmlParam("childId", $"The {_model.TemporalChildTypeName} ID.");
            var forChildParams = new List<MethodParameter>
            {
                new($"this {queryableType}", "query"),
                new(childIdType, "childId")
            };
            ExpressionMethod($"For{_model.TemporalChildTypeName}",
                $"query.Where(e => e.{_model.TemporalChildFkProperty} == childId)",
                queryableType,
                forChildParams,
                modifiers: new MethodModifiers { IsStatic = true });

            AppendLine();
            XmlSummary($"Filters to currently active {_model.TypeName} records for the specified {_model.TemporalChildTypeName}.");
            var activeForChildParams = new List<MethodParameter>
            {
                new($"this {queryableType}", "query"),
                new(childIdType, "childId")
            };
            ExpressionMethod($"ActiveFor{_model.TemporalChildTypeName}",
                $"query.For{_model.TemporalChildTypeName}(childId).Active()",
                queryableType,
                activeForChildParams,
                modifiers: new MethodModifiers { IsStatic = true });
        }
    }

    private string GetFkPropertyType(string fkPropertyName)
    {
        // The key is generated from the declared relation, so it is among the generated
        // properties, not the source ones.
        var generated = _model.GeneratedRelationProperties.AsImmutableArray()
            .FirstOrDefault(p => p.Kind == RelationPropertyKind.ForeignKey && p.Name == fkPropertyName);
        if (generated is not null)
            return generated.TypeName;

        var prop = _model.Properties.FirstOrDefault(p => p.Name == fkPropertyName);
        return prop?.TypeName ?? "global::System.Guid";
    }
}
