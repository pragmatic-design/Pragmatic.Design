using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The model of a Pragmatic entity's change detection as a document (<see cref="Templates.DocumentTrackingTemplate" />): every field but the
///     key and the version, a bit each in declaration order, up to 64, as the Sql engine's column mask.
/// </summary>
internal static class DocTrackingEmitter
{
    private const string Runtime = "global::Pragmatic.Persistence.Documents";
    private const int MaxTracked = 64;

    /// <summary>The fields tracked, in the order of their bits.</summary>
    public static List<DocField> Tracked(DocModel model) => model.Fields.Where(f => f != model.Key && f != model.Version).ToList();

    /// <summary>
    ///     The model, or null (reported) when the entity has more fields than a mask holds. <paramref name="softDeleteRules" />: the runtime has
    ///     <c>DocSoftDeleteRules</c> and the entity its repository, which the rules read the referrers through.
    /// </summary>
    public static DocumentTrackingModel? Model(DocModel model, List<Diagnostic> diagnostics, bool softDeleteRules = false)
    {
        var type = model.Type;
        var tracked = Tracked(model);
        if (tracked.Count > MaxTracked)
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.TooManyTracked, type.Locations.FirstOrDefault() ?? Location.None, type.Name, tracked.Count));
            return null;
        }

        var name = model.FullName;
        return new DocumentTrackingModel(
            VirtualFolderHints.ForType(type.Name, "DocumentTracking", type.ContainingNamespace.ToDisplayString()),
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            name,
            tracked.Select(f => new TrackedFieldModel(Value(f, name), DocTypeEmitter.WriteField(f, "e." + f.Property.Name))).ToEquatableArray(),
            Sql.Transforms.PragmaticReadRepository.DerivesFromAnEntity(type),
            softDeleteRules ? SoftDeleteRulesOf(model).ToEquatableArray() : default);
    }

    /// <summary>
    ///     The relations of this entity, as the referrer, that declare <c>OnSoftDelete</c> towards a to-one target: what the unit of work does to
    ///     this entity's documents when it soft-deletes the target (<c>DocSoftDeleteRules</c>). The navigations are the shaped entity's members,
    ///     the key the field named after them (the relation's <c>ForeignKey</c> when it names one), as <c>PragmaticDocSchema</c> indexes them. A
    ///     <c>Guid</c> key only: the principal's key is its <c>PersistenceId</c>.
    /// </summary>
    private static IEnumerable<DocSoftDeleteRuleModel> SoftDeleteRulesOf(DocModel model)
    {
        var type = model.Type;
        if (!type.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity"))
            yield break;
        var declared = Sql.Transforms.PragmaticRelations.References(type, "ManyToOne", "OneToOne").ToList();
        foreach (var navigation in type.GetMembers().OfType<IPropertySymbol>().Where(p => DocModel.IsPragmatic(p.Type)).OrderBy(p => p.Name, System.StringComparer.Ordinal))
        {
            var target = (INamedTypeSymbol)navigation.Type;
            if (Sql.Transforms.PragmaticRelations.PrincipalKey(type, navigation.Name, target) is not null
                || Sql.Transforms.PragmaticRelations.OnSoftDelete(type, navigation.Name, target) is not { } behavior)
                continue;
            var keyName = declared.FirstOrDefault(r => r.Navigation == navigation.Name).ForeignKey ?? navigation.Name + "Id";
            if (model.Field(keyName) is not { } key || key.ScalarName != "global::System.Guid")
                continue;
            yield return new DocSoftDeleteRuleModel(
                target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                behavior,
                key.Name,
                DocModel.JsonName(key),
                key.Property.Name,
                behavior is "SetNull" or "ClientSetNull");
        }
    }

    /// <summary>The field's value as the snapshot keeps it.</summary>
    /// <remarks>
    ///     A value that can change in place is kept by what it holds, never by the instance: the snapshot would be the field itself, the two
    ///     would always agree, and the save would write nothing. An embedded type or a list as its written bytes; a <c>LocalizedString</c>, which
    ///     <c>Set</c> changes in place, as its stored JSON text (#883). A <c>ProtectedValue</c> is replaced whole, never changed in place.
    /// </remarks>
    private static string Value(DocField field, string typeName) => field.Kind switch
    {
        DocKind.Embedded or DocKind.List
            => $"{Runtime}.Writing.DocTracking.Bytes<{typeName}>(e, static (w, x) => {DocTypeEmitter.WriteField(field, "x." + field.Property.Name)})",
        DocKind.Localized => $"(object?){DocTypeEmitter.LocalizedText(field, "e." + field.Property.Name)}",
        _ => $"(object?)e.{field.Property.Name}",
    };
}
