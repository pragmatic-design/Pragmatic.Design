using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates <c>{Entity}.IncludeHistory(IQueryFilterToggle)</c>: a scope in which reads of this
///     entity see closed stretches as well as open ones.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>IncludeHistory()</c> was an extension on <c>IQueryable</c> that returned its argument
///         unchanged, under documentation promising the whole history. The generated
///         <c>TemporalFilter</c> narrows a read to what is active <em>now</em>, and it is applied when
///         the queryable is built — so nothing downstream of that can widen it again. The name promised
///         what its position made impossible.
///     </para>
///     <para>
///         The lever that does work is <c>IQueryFilterToggle.Disable&lt;T.TemporalFilter&gt;()</c>,
///         which is <c>IDisposable</c> and has to wrap the read. That cannot be hidden behind a method
///         returning a lazy <c>IQueryable</c> — the scope would close before the query ran — so the
///         shape changes and the name stays:
///     </para>
///     <code>
///         using var _ = StaffAssignment.IncludeHistory(filters);
///         var all = await repository.Query().ForProperty(id).ToListAsync(ct);
///     </code>
///     <para>
///         Narrower than <c>FilterMode.Raw</c> and than <c>QueryStrategy.Raw</c>, both of which drop
///         tenant isolation and soft-delete along with the temporal filter. Reading a history is not a
///         reason to stop being multi-tenant.
///     </para>
/// </remarks>
internal sealed class TemporalHistoryScopeTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TemporalHistoryScopeTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} history scope from {_model.Namespace}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "TemporalHistoryScope", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, IsTemporalRelation: true };

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Source-generated history scope for {_model.TypeName}.");

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        XmlSummary(
            $"Reads of {_model.TypeName} inside this scope see closed stretches as well as open ones.");
        XmlParam("filters", "The request's filter toggle.");
        XmlReturns("A scope; dispose it to narrow reads back to what is currently active.");

        var parameters = new List<MethodParameter>
        {
            new("global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle", "filters")
        };

        ExpressionMethod("IncludeHistory",
            $"filters.Disable<global::{_model.FullTypeName}.TemporalFilter>()",
            "global::System.IDisposable",
            parameters,
            accessModifier: AccessModifier.Public,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private static AccessModifier ParseAccessibility(string accessibility)
        => accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public,
        };
}
