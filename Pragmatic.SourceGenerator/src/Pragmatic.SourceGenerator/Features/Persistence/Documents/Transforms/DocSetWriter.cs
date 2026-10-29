using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The fields an update sets, rendered for both stores: on MongoDB the update document's <c>$set</c> (and an <c>$inc</c> of a version), or,
///     when a value is computed from the document's own fields, the <c>$set</c> stage of an update pipeline (the values as <c>$literal</c>, the
///     version moved with <c>$add</c>); on Cosmos DB a patch's operations, one document at a time, a computed value computed from the document as
///     it was read. Statements over <c>w</c> (the writer), <c>s</c> (the slots) and, for Cosmos DB, <c>e</c> (the document).
/// </summary>
internal sealed class DocSetWriter(DocModel entity)
{
    /// <summary>The operations Cosmos DB takes in one patch.</summary>
    private const int PatchOperations = 10;

    private readonly List<(string Path, DocValue? Value, DocComputed? Computed)> _sets = [];

    /// <summary>A field set to a value: a constant, or code over the slots.</summary>
    public void Value(string path, DocValue value) => _sets.Add((path, value, null));

    /// <summary>A field set to a value computed from the document's own fields.</summary>
    public void Computed(string path, DocComputed computed) => _sets.Add((path, null, computed));

    /// <summary>True when a value is computed from the document: MongoDB runs the update as a pipeline.</summary>
    public bool Pipeline => _sets.Any(s => s.Computed is not null);

    /// <summary>The update's elements on MongoDB: the <c>$set</c> (and the <c>$inc</c> of a version), or the pipeline's <c>$set</c> stage.</summary>
    public string Mongo()
    {
        var set = new StringBuilder("w.StartDocument(\"$set\"); ");
        foreach (var (path, value, computed) in _sets)
        {
            var name = DocTypeEmitter.Literal(path);
            if (computed is not null)
                set.Append(computed.Mongo(name));
            else if (value!.IsNull)
                set.Append(Pipeline ? $"w.StartDocument({name}); w.WriteNull(\"$literal\"); w.EndDocument(); " : $"w.WriteNull({name}); ");
            else if (Pipeline)
                set.Append($"w.StartDocument({name}); global::Pragmatic.Persistence.Documents.Conversion.DocValues.Write(w, \"$literal\", {value.Code}); w.EndDocument(); "); // a text starting with $ is a value, not a field
            else
                set.Append($"global::Pragmatic.Persistence.Documents.Conversion.DocValues.Write(w, {name}, {value.Code}); ");
        }

        if (entity.Version is not { } version)
            return set.Append("w.EndDocument(); ").ToString();
        var field = DocTypeEmitter.Literal(version.Name);
        var one = version.Kind == DocKind.Int32 ? "1" : "1L"; // the stored type kept: an Int32 plus an Int64 is an Int64
        return Pipeline
            ? set.Append($"w.StartDocument({field}); w.StartArray(\"$add\"); w.Write(0, {DocTypeEmitter.Literal("$" + version.Name)}); w.Write(1, {one}); w.EndArray(); w.EndDocument(); w.EndDocument(); ").ToString()
            : set.Append($"w.EndDocument(); w.StartDocument(\"$inc\"); w.Write({field}, 1); w.EndDocument(); ").ToString();
    }

    /// <summary>The same sets as Cosmos DB's patch operations, a version incremented; null when they are more than a patch takes.</summary>
    public string? Cosmos()
    {
        if (_sets.Count + (entity.Version is null ? 0 : 1) > PatchOperations)
            return null;
        var operations = new StringBuilder();
        foreach (var (path, value, computed) in _sets)
        {
            var target = DocTypeEmitter.Literal("/" + path.Replace('.', '/'));
            var written = computed is not null
                ? $"global::Pragmatic.Persistence.Documents.Conversion.DocJson.Write(w, \"value\", {computed.Code}); "
                : value!.IsNull ? "w.WriteNull(\"value\"); " : $"global::Pragmatic.Persistence.Documents.Conversion.DocJson.Write(w, \"value\", {value.Code}); ";
            operations.Append($"w.WriteStartObject(); w.WriteString(\"op\", \"set\"); w.WriteString(\"path\", {target}); {written}w.WriteEndObject(); ");
        }

        if (entity.Version is { } version)
            operations.Append($"w.WriteStartObject(); w.WriteString(\"op\", \"incr\"); w.WriteString(\"path\", {DocTypeEmitter.Literal("/" + DocModel.JsonName(version))}); w.WriteNumber(\"value\", 1); w.WriteEndObject(); ");
        return operations.ToString();
    }
}
