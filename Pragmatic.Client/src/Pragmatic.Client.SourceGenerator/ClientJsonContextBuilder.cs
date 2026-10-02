using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Client.SourceGenerator.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator;

/// <summary>
///     Builds the JSON metadata model for a generated client, from the DTOs the generator itself emits.
/// </summary>
/// <remarks>
///     <para>
///         The client cannot declare a <c>[JsonSerializable]</c> context: that attribute is read by
///         System.Text.Json's own source generator, which never sees another generator's output — tried,
///         and the partial class was left unimplemented. So the metadata is written out longhand, by the
///         same template the module generator uses, from the same manifest this generator already reads
///         to produce the DTOs.
///     </para>
///     <para>
///         Everything here is decided from type <em>text</em>, because that is all a manifest carries.
///         A type it cannot place is left out rather than guessed at: the context then has no entry for
///         it and the failure is a clear exception, not a payload with missing fields.
///     </para>
/// </remarks>
internal static class ClientJsonContextBuilder
{
    /// <summary>One DTO the client generated: its name and the properties it declares.</summary>
    internal sealed record Dto(string Name, IReadOnlyList<ClientDtoProperty> Properties);

    /// <summary>
    ///     The model covering every DTO, plus the closed <c>PagedResult&lt;T&gt;</c> the endpoints return.
    /// </summary>
    /// <param name="ns">The client's namespace.</param>
    /// <param name="dtos">The DTOs the generator emitted.</param>
    /// <param name="pagedItemTypes">Item types of paged responses, for the closed PagedResult entries.</param>
    /// <param name="wireTypes">
    ///     What the endpoints actually send and read. Not derivable from the DTOs alone: an endpoint
    ///     returning <c>XDto[]</c> needs a collection entry that no DTO property mentions — the first
    ///     version missed exactly that and the client threw "jsonTypeInfo cannot be null" at run time.
    /// </param>
    public static JsonContextModel Build(
        string ns,
        IReadOnlyList<Dto> dtos,
        IEnumerable<string> pagedItemTypes,
        IEnumerable<string> wireTypes)
    {
        var objects = new Dictionary<string, JsonObjectModel>(System.StringComparer.Ordinal);
        var leaves = new Dictionary<string, JsonLeafModel>(System.StringComparer.Ordinal);
        var collections = new Dictionary<string, JsonCollectionModel>(System.StringComparer.Ordinal);

        foreach (var dto in dtos)
        {
            var typeExpr = $"global::{ns}.{dto.Name}";
            var properties = ImmutableArray.CreateBuilder<JsonPropertyModel>();

            foreach (var property in dto.Properties)
            {
                var propertyType = Qualify(ns, property.Type, dtos);
                Register(propertyType, leaves, collections, ns, dtos);

                properties.Add(new JsonPropertyModel(
                    property.Name,
                    CamelCase(property.Name),
                    propertyType,
                    IsValueType: IsValueTypeName(property.Type),
                    IsInitOnly: true,
                    DeclaringTypeExpr: typeExpr));
            }

            if (properties.Count == 0)
                continue;

            objects[typeExpr] = new JsonObjectModel(typeExpr, Token(typeExpr), properties.ToImmutable());
        }

        foreach (var item in pagedItemTypes.Distinct(System.StringComparer.Ordinal))
            AddPagedResult(ns, item, dtos, objects, leaves, collections);

        foreach (var wire in wireTypes.Distinct(System.StringComparer.Ordinal))
            Register(Qualify(ns, wire, dtos), leaves, collections, ns, dtos);

        return new JsonContextModel(
            Namespace: ns,
            Objects: objects.Values.OrderBy(static o => o.TypeExpr, System.StringComparer.Ordinal).ToImmutableArray(),
            Leaves: leaves.Values.OrderBy(static l => l.TypeExpr, System.StringComparer.Ordinal).ToImmutableArray(),
            Collections: collections.Values.OrderBy(static c => c.TypeExpr, System.StringComparer.Ordinal).ToImmutableArray());
    }

    /// <summary>
    ///     <c>PagedResult&lt;T&gt;</c> is one generic class in the generated client, so the context needs
    ///     an entry per closed instantiation the endpoints actually return.
    /// </summary>
    private static void AddPagedResult(
        string ns,
        string itemType,
        IReadOnlyList<Dto> dtos,
        Dictionary<string, JsonObjectModel> objects,
        Dictionary<string, JsonLeafModel> leaves,
        Dictionary<string, JsonCollectionModel> collections)
    {
        var item = Qualify(ns, itemType, dtos);
        var typeExpr = $"global::{ns}.PagedResult<{item}>";
        var arrayExpr = $"{item}[]";

        Register(arrayExpr, leaves, collections, ns, dtos);
        Register("int", leaves, collections, ns, dtos);

        objects[typeExpr] = new JsonObjectModel(typeExpr, Token(typeExpr),
        [
            new JsonPropertyModel("Items", "items", arrayExpr, false, false, typeExpr),
            new JsonPropertyModel("TotalCount", "totalCount", "int", true, false, typeExpr),
            new JsonPropertyModel("Page", "page", "int", true, false, typeExpr),
            new JsonPropertyModel("PageSize", "pageSize", "int", true, false, typeExpr),
        ]);
    }

    /// <summary>Records the leaf or collection entries a property type needs.</summary>
    private static void Register(
        string typeExpr,
        Dictionary<string, JsonLeafModel> leaves,
        Dictionary<string, JsonCollectionModel> collections,
        string ns,
        IReadOnlyList<Dto> dtos)
    {
        if (leaves.ContainsKey(typeExpr) || collections.ContainsKey(typeExpr))
            return;

        if (ElementOf(typeExpr) is { } element)
        {
            var qualified = Qualify(ns, element, dtos);
            collections[typeExpr] = new JsonCollectionModel(typeExpr,
                "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateArrayInfo<" +
                $"{qualified}>(options, new global::System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<{typeExpr}>())");

            Register(qualified, leaves, collections, ns, dtos);
            return;
        }

        if (typeExpr.EndsWith("?", System.StringComparison.Ordinal))
        {
            var underlying = typeExpr.Substring(0, typeExpr.Length - 1);
            if (LeafConverter(underlying) is not null)
            {
                leaves[typeExpr] = new JsonLeafModel(typeExpr,
                    "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GetNullableConverter<" +
                    $"{underlying}>(options)");
                Register(underlying, leaves, collections, ns, dtos);
            }

            return;
        }

        if (LeafConverter(typeExpr) is { } converter)
            leaves[typeExpr] = new JsonLeafModel(typeExpr, converter);
    }

    /// <summary>The element type of an array or list expression, or <c>null</c>.</summary>
    private static string? ElementOf(string typeExpr)
    {
        var bare = typeExpr.TrimEnd('?');
        if (bare.EndsWith("[]", System.StringComparison.Ordinal))
            return bare.Substring(0, bare.Length - 2);

        foreach (var wrapper in new[] { "List<", "IReadOnlyList<", "IList<", "ICollection<", "IEnumerable<" })
        {
            var open = bare.IndexOf(wrapper, System.StringComparison.Ordinal);
            if (open >= 0 && bare.EndsWith(">", System.StringComparison.Ordinal))
                return bare.Substring(open + wrapper.Length, bare.Length - open - wrapper.Length - 1);
        }

        return null;
    }

    /// <summary>
    ///     The <c>JsonMetadataServices</c> converter for a scalar, mirroring what the symbol-driven
    ///     extractor picks. Enums go to the shared helper, never to the numeric converter — baking that
    ///     in would change the wire format the moment a host registers the string one.
    /// </summary>
    private static string? LeafConverter(string typeExpr)
    {
        var name = typeExpr.TrimEnd('?');
        if (name.StartsWith("global::", System.StringComparison.Ordinal))
            name = name.Substring(8);

        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            name = name.Substring(lastDot + 1);

        var converter = name switch
        {
            "string" or "String" => "StringConverter",
            "bool" or "Boolean" => "BooleanConverter",
            "int" or "Int32" => "Int32Converter",
            "long" or "Int64" => "Int64Converter",
            "short" or "Int16" => "Int16Converter",
            "byte" or "Byte" => "ByteConverter",
            "sbyte" or "SByte" => "SByteConverter",
            "uint" or "UInt32" => "UInt32Converter",
            "ulong" or "UInt64" => "UInt64Converter",
            "ushort" or "UInt16" => "UInt16Converter",
            "double" or "Double" => "DoubleConverter",
            "float" or "Single" => "SingleConverter",
            "decimal" or "Decimal" => "DecimalConverter",
            "char" or "Char" => "CharConverter",
            "Guid" => "GuidConverter",
            "DateTime" => "DateTimeConverter",
            "DateTimeOffset" => "DateTimeOffsetConverter",
            "TimeSpan" => "TimeSpanConverter",
            _ => null,
        };

        return converter is null
            ? null
            : $"global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.{converter}";
    }

    /// <summary>
    ///     Qualifies a bare DTO name with the client namespace, and drops the nullable annotation from
    ///     reference types.
    /// </summary>
    /// <remarks>
    ///     <c>typeof(string?)</c> does not compile — the annotation is not part of the type. On a value
    ///     type it is: <c>int?</c> is <c>Nullable&lt;int&gt;</c>, a different type with its own metadata,
    ///     so there it stays.
    /// </remarks>
    private static string Qualify(string ns, string typeName, IReadOnlyList<Dto> dtos)
    {
        var nullable = typeName.EndsWith("?", System.StringComparison.Ordinal);
        var bare = nullable ? typeName.Substring(0, typeName.Length - 1) : typeName;
        var keepAnnotation = nullable && IsValueTypeName(bare);

        if (bare.StartsWith("global::", System.StringComparison.Ordinal) || ElementOf(bare) is not null)
            return keepAnnotation ? bare + "?" : bare;

        if (dtos.Any(d => string.Equals(d.Name, bare, System.StringComparison.Ordinal)))
            return $"global::{ns}.{bare}";

        return keepAnnotation ? bare + "?" : bare;
    }

    private static bool IsValueTypeName(string typeName)
        => LeafConverter(typeName) is { } converter && !converter.EndsWith("StringConverter", System.StringComparison.Ordinal);

    /// <summary>
    ///     The method-name token for a type expression.
    /// </summary>
    /// <remarks>
    ///     Every <c>global::</c> is dropped, not just a leading one: a closed generic carries them on its
    ///     arguments too, and the first version produced
    ///     <c>Create_…_PagedResult_global::Showcase_…</c> — a method name with a scope operator in it.
    /// </remarks>
    private static string Token(string typeExpr)
        => typeExpr
            .Replace("global::", "")
            .Replace('.', '_').Replace('+', '_').Replace('<', '_').Replace('>', '_')
            .Replace(',', '_').Replace(' ', '_').Replace("[]", "Array").Replace('?', '_');

    private static string CamelCase(string name)
        => string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
