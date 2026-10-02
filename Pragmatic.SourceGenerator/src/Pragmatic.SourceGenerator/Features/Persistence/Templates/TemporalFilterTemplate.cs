using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a nested TemporalFilter class implementing IQueryFilter{T}
///     for each entity with [TemporalRelation]. Filters to currently active records by default.
/// </summary>
internal sealed class TemporalFilterTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TemporalFilterTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/QueryPipeline";
    protected override string? SourceInfo => $"TemporalFilter for {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "TemporalFilter", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model is { IsValid: true, IsTemporalRelation: true };

    public override void RenderFile()
    {
        AddUsing("System");
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
            XmlSummary($"Auto-generated temporal filter for {_model.TypeName}. Filters to currently active records (ValidFrom <= now and ValidTo is null or > now). Priority 200 (applied after soft-delete).");

            Class("TemporalFilter", () =>
            {
                // The clock, injected. The same shape the generated repository uses: optional with a
                // fallback to the system clock, because nothing in the framework registers a
                // TimeProvider and a required parameter would stop every application that does not.
                // An application that registers one gets it, and "what is active" becomes an input.
                AppendLine("private readonly global::System.TimeProvider _timeProvider;");
                AppendLine();

                XmlSummary($"Creates the temporal filter for {_model.TypeName}.");
                XmlParam("timeProvider", "The clock to read \"now\" from. Defaults to the system clock.");
                Constructor("TemporalFilter",
                    () => AppendLine("_timeProvider = timeProvider ?? global::System.TimeProvider.System;"),
                    [new MethodParameter("global::System.TimeProvider?", "timeProvider") { DefaultValue = "null" }]);
                AppendLine();

                // Priority property — after soft-delete (100)
                ExpressionProperty("Priority", "int", "200");
                AppendLine();

                // Scope property
                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                // GetFilter method. UtcNow is captured into a local so EF Core parameterizes it (evaluated
                // app-side) instead of translating to the database's now(): ValidFrom/ValidTo are written
                // with the app clock, so comparing against an app-clock "now" avoids app↔DB clock skew
                // (e.g. a Testcontainers/Docker VM clock lagging the host) silently dropping just-active rows.
                XmlSummary("Returns the temporal filter expression (active records only).");
                Method("GetFilter", () =>
                {
                    AppendLine("var now = _timeProvider.GetUtcNow();");
                    AppendLine("return entity => entity.ValidFrom <= now && (entity.ValidTo == null || entity.ValidTo > now);");
                },
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
