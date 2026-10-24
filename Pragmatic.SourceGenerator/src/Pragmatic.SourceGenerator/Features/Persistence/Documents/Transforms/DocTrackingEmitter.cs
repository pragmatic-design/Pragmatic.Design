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

    /// <summary>The model, or null (reported) when the entity has more fields than a mask holds.</summary>
    public static DocumentTrackingModel? Model(DocModel model, List<Diagnostic> diagnostics)
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
            tracked.Select(f => new TrackedFieldModel(Value(f, name), DocTypeEmitter.WriteField(f, "e." + f.Property.Name))).ToEquatableArray());
    }

    /// <summary>The field's value as the snapshot keeps it.</summary>
    private static string Value(DocField field, string typeName)
        => field.Kind is DocKind.Embedded or DocKind.List
            ? $"{Runtime}.DocTracking.Bytes<{typeName}>(e, static (w, x) => {DocTypeEmitter.WriteField(field, "x." + field.Property.Name)})"
            : $"(object?)e.{field.Property.Name}";
}
