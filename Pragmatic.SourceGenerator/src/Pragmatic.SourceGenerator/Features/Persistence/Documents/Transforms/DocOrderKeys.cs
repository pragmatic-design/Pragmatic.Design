using System.Collections.Generic;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>An ordering key of the documents engine: the field's MongoDB path, its Cosmos DB path and its segment, the texts of a <c>DocOrderKey</c>.</summary>
internal static class DocOrderKeys
{
    /// <summary>The key's texts for the entity's property <paramref name="property" />; null when it is not a scalar field of the document.</summary>
    public static IReadOnlyList<string>? Texts(DocModel model, string property)
    {
        if (property.Contains('.') || model.Field(property) is not { } field || field.Kind is DocKind.Embedded or DocKind.List)
            return null;
        return Named(field.Name);
    }

    /// <summary>The key's texts for a stored field's name (<c>_id</c> is Cosmos DB's <c>id</c>).</summary>
    public static IReadOnlyList<string> Named(string name)
    {
        var cosmos = name == "_id" ? "id" : name;
        return [name, $"c[\"{cosmos}\"]", cosmos];
    }
}
