using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     Extracts the JSON shape closure of a boundary type: the root plus every nested DTO, plus the
///     leaf (primitive/enum) and collection (List/array/Dictionary) types their properties reference.
///     A property whose type is not a supported leaf, collection, or object makes the whole root defer
///     (returns false) so the emitted context never has partial/wrong metadata.
/// </summary>
internal static class JsonShapeExtractor
{
    private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

    public static bool TryExtractClosure(
        INamedTypeSymbol root,
        out ImmutableArray<JsonObjectModel> objects,
        out ImmutableArray<JsonLeafModel> leaves,
        out ImmutableArray<JsonCollectionModel> collections)
    {
        var objectMap = new Dictionary<string, JsonObjectModel>(System.StringComparer.Ordinal);
        var leafMap = new Dictionary<string, JsonLeafModel>(System.StringComparer.Ordinal);
        var collMap = new Dictionary<string, JsonCollectionModel>(System.StringComparer.Ordinal);
        var visited = new HashSet<string>(System.StringComparer.Ordinal);
        var queue = new Queue<INamedTypeSymbol>();
        queue.Enqueue(root);

        if (!Drain(queue, visited, objectMap, leafMap, collMap))
        {
            objects = default; leaves = default; collections = default;
            return false;
        }

        objects = objectMap.Values.ToImmutableArray();
        leaves = leafMap.Values.ToImmutableArray();
        collections = collMap.Values.ToImmutableArray();
        return objectMap.Count > 0;
    }

    /// <summary>
    ///     The closure of a type this generator emits, described by the properties it will carry.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A generated type has no symbol — the compilation being analysed does not contain it — so
    ///         its shape has to be stated rather than read. The property <em>types</em> are real symbols,
    ///         which is what makes the nested objects, enums and collections come out right.
    ///     </para>
    ///     <para>
    ///         Deliberately not <see cref="TryExtractClosure" /> over the declaring type: the source of
    ///         these properties may carry others that are none of the projection's business. A single
    ///         get-only or non-serializable property there would defer the whole root, and the generated
    ///         type would drop out of the context without a word.
    ///     </para>
    /// </remarks>
    /// <param name="typeExpr">Fully-qualified expression for the generated type.</param>
    /// <param name="properties">The properties it carries — name and type — in declaration order.</param>
    /// <param name="needsUnsafeConstructor">
    ///     Whether it must be constructed through an <c>[UnsafeAccessor]</c> ctor — true when it declares
    ///     <c>required</c> members, which make <c>new T()</c> a compile error.
    /// </param>
    /// <param name="objects">The generated type plus every nested object its properties reach.</param>
    /// <param name="leaves">The leaf types reached.</param>
    /// <param name="collections">The collection types reached.</param>
    public static bool TryExtractProjection(
        string typeExpr,
        IEnumerable<(string Name, string WireName, ITypeSymbol Type)> properties,
        bool needsUnsafeConstructor,
        out ImmutableArray<JsonObjectModel> objects,
        out ImmutableArray<JsonLeafModel> leaves,
        out ImmutableArray<JsonCollectionModel> collections)
    {
        objects = default; leaves = default; collections = default;

        var objectMap = new Dictionary<string, JsonObjectModel>(System.StringComparer.Ordinal);
        var leafMap = new Dictionary<string, JsonLeafModel>(System.StringComparer.Ordinal);
        var collMap = new Dictionary<string, JsonCollectionModel>(System.StringComparer.Ordinal);
        var visited = new HashSet<string>(System.StringComparer.Ordinal) { typeExpr };
        var queue = new Queue<INamedTypeSymbol>();

        var props = ImmutableArray.CreateBuilder<JsonPropertyModel>();
        foreach (var (name, wireName, type) in properties)
        {
            if (!ProcessType(type, leafMap, collMap, queue))
                return false;

            // Every property of a generated type is declared by it and written through its init setter.
            // The wire name arrives from the caller rather than being camel-cased here: a generated body
            // record reproduces the [JsonPropertyName] its operation declared, and deriving the name
            // again would contradict the attribute the same generator just wrote onto the record.
            props.Add(new JsonPropertyModel(
                name, wireName, type.ToDisplayString(Fq),
                type.IsValueType, IsInitOnly: true, DeclaringTypeExpr: typeExpr));
        }

        if (props.Count == 0)
            return false;

        objectMap[typeExpr] = new JsonObjectModel(
            typeExpr, TokenFor(typeExpr), props.ToImmutable(),
            NeedsUnsafeConstructor: needsUnsafeConstructor);

        if (!Drain(queue, visited, objectMap, leafMap, collMap))
            return false;

        objects = objectMap.Values.ToImmutableArray();
        leaves = leafMap.Values.ToImmutableArray();
        collections = collMap.Values.ToImmutableArray();
        return true;
    }

    /// <summary>Extracts every queued type, enqueuing what they reach, until nothing is left.</summary>
    private static bool Drain(
        Queue<INamedTypeSymbol> queue,
        HashSet<string> visited,
        Dictionary<string, JsonObjectModel> objectMap,
        Dictionary<string, JsonLeafModel> leafMap,
        Dictionary<string, JsonCollectionModel> collMap)
    {
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            var expr = type.ToDisplayString(Fq);
            if (!visited.Add(expr))
                continue;

            if (!TryExtractObject(type, expr, leafMap, collMap, queue, out var obj))
                return false;

            objectMap[expr] = obj;
        }

        return true;
    }

    /// <summary>The method-name token derived from a type expression.</summary>
    private static string TokenFor(string typeExpr)
        // Every `global::`, not just a leading one: a closed generic carries them on its arguments too,
        // and a scope operator in a method name does not compile.
        => typeExpr
            .Replace("global::", "")
            .Replace('.', '_').Replace('+', '_').Replace('<', '_').Replace('>', '_')
            .Replace(',', '_').Replace(' ', '_');

    private static bool TryExtractObject(
        INamedTypeSymbol type,
        string typeExpr,
        Dictionary<string, JsonLeafModel> leafMap,
        Dictionary<string, JsonCollectionModel> collMap,
        Queue<INamedTypeSymbol> queue,
        out JsonObjectModel objectModel)
    {
        objectModel = null!;

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return false;

        var isPolymorphic = TryDetectPolymorphism(type, out var discriminator, out var derivedTypes, out var derivedSymbols);

        // Abstract types are only coverable as a polymorphic base (constructed via derived types).
        if (type.IsAbstract && !isPolymorphic)
            return false;

        // Construction. Prefer a public parameterless ctor (new T()). Otherwise fall back to an
        // [UnsafeAccessor] constructor (positional records) — reference types only, since a boxed struct
        // can't be mutated in place through the Set/ctor accessors.
        var needsUnsafeCtor = false;
        var ctorParamTypes = ImmutableArray<string>.Empty;
        if (!type.IsAbstract)
        {
            var hasParameterless = type.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

            // `new T()` is a compile error (CS9035) when T has required members, even though every one
            // of them is about to be set by the deserializer. The accessor calls the same parameterless
            // ctor without the language check — which is exactly what the caller means here.
            if (hasParameterless && !type.IsValueType && HasRequiredMembers(type))
            {
                needsUnsafeCtor = true;
            }
            else if (!hasParameterless)
            {
                if (type.IsValueType || !TrySelectConstructor(type, out var ctor))
                    return false;
                needsUnsafeCtor = true;
                ctorParamTypes = ctor.Parameters.Select(p => p.Type.ToDisplayString(Fq)).ToImmutableArray();
            }
        }

        if (isPolymorphic)
            foreach (var derived in derivedSymbols)
                queue.Enqueue(derived);

        var props = ImmutableArray.CreateBuilder<JsonPropertyModel>();

        foreach (var prop in PropertyAnalyzer.GetAllProperties(type))
        {
            // Require a public setter (a regular one, or an init accessor — init-only props are assigned
            // through an [UnsafeAccessor] setter). Init-only on a value type isn't supported (boxed copy).
            var isInitOnly = PropertyAnalyzer.IsInitOnly(prop);
            if (prop.SetMethod is null
                || prop.SetMethod.DeclaredAccessibility != Accessibility.Public
                || (isInitOnly && type.IsValueType))
                return false;

            if (!ProcessType(prop.Type, leafMap, collMap, queue))
                return false;

            // The init setter lives on the declaring type (a base class for inherited properties), so the
            // [UnsafeAccessor] setter must target it — not the derived object type.
            var declaringType = (prop.SetMethod?.ContainingType ?? prop.ContainingType).ToDisplayString(Fq);
            props.Add(new JsonPropertyModel(
                prop.Name, WireName(prop), prop.Type.ToDisplayString(Fq), prop.Type.IsValueType, isInitOnly, declaringType));
        }

        // An abstract polymorphic base may legitimately have no serializable properties of its own.
        if (props.Count == 0 && !isPolymorphic)
            return false;

        objectModel = new JsonObjectModel(
            typeExpr, TokenFor(typeExpr), props.ToImmutable(),
            IsAbstract: type.IsAbstract,
            DiscriminatorName: isPolymorphic ? discriminator : null,
            DerivedTypes: derivedTypes,
            NeedsUnsafeConstructor: needsUnsafeCtor,
            ConstructorParamTypes: ctorParamTypes);
        return true;
    }

    /// <summary>
    ///     Whether the type or any of its bases declares a <c>required</c> member.
    /// </summary>
    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                    return true;

        return false;
    }

    /// <summary>
    ///     Selects the constructor to invoke via <c>[UnsafeAccessor]</c> for a type without a public
    ///     parameterless ctor: the accessible instance ctor with the most parameters, excluding the
    ///     record copy-constructor (a single parameter of the declaring type itself).
    /// </summary>
    private static bool TrySelectConstructor(INamedTypeSymbol type, out IMethodSymbol constructor)
    {
        constructor = null!;
        IMethodSymbol? best = null;
        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.Parameters.Length == 0)
                continue;
            // Skip the synthesized copy-constructor: record R(R original).
            if (ctor.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, type))
                continue;
            if (best is null || ctor.Parameters.Length > best.Parameters.Length)
                best = ctor;
        }

        if (best is null)
            return false;

        constructor = best;
        return true;
    }

    private static bool TryDetectPolymorphism(
        INamedTypeSymbol type,
        out string discriminatorName,
        out ImmutableArray<JsonDerivedTypeModel> derived,
        out System.Collections.Generic.List<INamedTypeSymbol> derivedSymbols)
    {
        discriminatorName = "$type";
        derived = ImmutableArray<JsonDerivedTypeModel>.Empty;
        derivedSymbols = new System.Collections.Generic.List<INamedTypeSymbol>();

        var polyAttr = type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonPolymorphicAttribute");
        if (polyAttr is null)
            return false;

        foreach (var na in polyAttr.NamedArguments)
            if (na.Key == "TypeDiscriminatorPropertyName" && na.Value.Value is string s)
                discriminatorName = s;

        var builder = ImmutableArray.CreateBuilder<JsonDerivedTypeModel>();
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "System.Text.Json.Serialization.JsonDerivedTypeAttribute"
                || attr.ConstructorArguments.Length < 1
                || attr.ConstructorArguments[0].Value is not INamedTypeSymbol dt)
                continue;

            var discArg = attr.ConstructorArguments.Length >= 2
                ? attr.ConstructorArguments[1].Value switch
                {
                    string ds => $"\"{ds.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
                    null => null,
                    var other => other.ToString(),
                }
                : null;

            if (discArg is null)
                continue; // a derived type without a discriminator can't be emitted; skip it

            builder.Add(new JsonDerivedTypeModel(dt.ToDisplayString(Fq), discArg));
            derivedSymbols.Add(dt);
        }

        derived = builder.ToImmutable();
        return builder.Count > 0;
    }

    /// <summary>Registers a referenced type as a leaf/collection, or enqueues it as a nested object. Returns false if unsupported.</summary>
    private static bool ProcessType(
        ITypeSymbol type,
        Dictionary<string, JsonLeafModel> leafMap,
        Dictionary<string, JsonCollectionModel> collMap,
        Queue<INamedTypeSymbol> queue)
    {
        var expr = type.ToDisplayString(Fq);

        // Nullable value type (int?, MyEnum?) — must precede the object check (Nullable<T> is a struct).
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && nullable.TypeArguments.Length == 1)
        {
            var underlying = nullable.TypeArguments[0];
            if (!TryLeaf(underlying, out _))
                return false; // only nullable of a supported leaf
            var underlyingExpr = underlying.ToDisplayString(Fq);
            leafMap[expr] = new JsonLeafModel(expr,
                $"global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GetNullableConverter<{underlyingExpr}>(options)");
            return ProcessType(underlying, leafMap, collMap, queue); // also register the underlying leaf
        }

        if (TryLeaf(type, out var leaf))
        {
            leafMap[expr] = leaf;
            return true;
        }

        // Array T[]
        if (type is IArrayTypeSymbol { Rank: 1 } arr)
        {
            var elemExpr = arr.ElementType.ToDisplayString(Fq);
            collMap[expr] = new JsonCollectionModel(expr,
                $"global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateArrayInfo<{elemExpr}>(options, new global::System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<{expr}>())");
            return ProcessType(arr.ElementType, leafMap, collMap, queue);
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.OriginalDefinition.ToDisplayString(Fq);
            if (def == "global::System.Collections.Generic.List<T>" && named.TypeArguments.Length == 1)
            {
                var elemExpr = named.TypeArguments[0].ToDisplayString(Fq);
                collMap[expr] = new JsonCollectionModel(expr,
                    $"global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateListInfo<{expr}, {elemExpr}>(options, new global::System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<{expr}> {{ ObjectCreator = static () => new {expr}() }})");
                return ProcessType(named.TypeArguments[0], leafMap, collMap, queue);
            }

            if (def == "global::System.Collections.Generic.Dictionary<TKey, TValue>" && named.TypeArguments.Length == 2)
            {
                var keyExpr = named.TypeArguments[0].ToDisplayString(Fq);
                var valExpr = named.TypeArguments[1].ToDisplayString(Fq);
                collMap[expr] = new JsonCollectionModel(expr,
                    $"global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateDictionaryInfo<{expr}, {keyExpr}, {valExpr}>(options, new global::System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<{expr}> {{ ObjectCreator = static () => new {expr}() }})");
                return ProcessType(named.TypeArguments[0], leafMap, collMap, queue)
                    && ProcessType(named.TypeArguments[1], leafMap, collMap, queue);
            }
        }

        // Nested object → enqueue for extraction.
        if (IsSupportedObject(type, out var nestedNamed))
        {
            queue.Enqueue(nestedNamed);
            return true;
        }

        return false;
    }

    private static bool IsSupportedObject(ITypeSymbol type, out INamedTypeSymbol named)
    {
        named = null!;
        if (type is not INamedTypeSymbol n || type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract)
            return false;
        if (type.SpecialType != SpecialType.None)
            return false;
        if (type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
            return false; // unsupported collection shape → defer
        // Construction (parameterless ctor vs [UnsafeAccessor] ctor for records) is decided when the
        // type is dequeued in TryExtractObject; enqueue any object-shaped named type.
        named = n;
        return true;
    }

    private static bool TryLeaf(ITypeSymbol type, out JsonLeafModel leaf)
    {
        leaf = null!;
        var expr = type.ToDisplayString(Fq);

        if (type.TypeKind == TypeKind.Enum)
        {
            // Read the configured converters directly, and do not bake in the numeric one.
            // GetEnumConverter always yields numeric, so a host registering JsonStringEnumConverter —
            // as the generated entry point does — produced "Role" through the reflection path and 0
            // through this one: turning on <PublishAot> silently changed the wire format of every enum
            // in the API.
            //
            // ⚠️ Not options.GetConverter(type): that goes through GetTypeInfo, which calls this
            // resolver, which calls it again — a stack overflow at the first request, not a wrong
            // value. EnumConverter walks options.Converters itself and never re-enters the pipeline.
            leaf = new JsonLeafModel(expr, $"EnumConverter<{expr}>(options)");
            return true;
        }

        var converter = type.SpecialType switch
        {
            SpecialType.System_String => "StringConverter",
            SpecialType.System_Boolean => "BooleanConverter",
            SpecialType.System_Int32 => "Int32Converter",
            SpecialType.System_Int64 => "Int64Converter",
            SpecialType.System_Int16 => "Int16Converter",
            SpecialType.System_Byte => "ByteConverter",
            SpecialType.System_SByte => "SByteConverter",
            SpecialType.System_UInt32 => "UInt32Converter",
            SpecialType.System_UInt64 => "UInt64Converter",
            SpecialType.System_UInt16 => "UInt16Converter",
            SpecialType.System_Double => "DoubleConverter",
            SpecialType.System_Single => "SingleConverter",
            SpecialType.System_Decimal => "DecimalConverter",
            SpecialType.System_Char => "CharConverter",
            _ => null,
        };

        if (converter is null)
        {
            converter = expr switch
            {
                "global::System.Guid" => "GuidConverter",
                "global::System.DateTime" => "DateTimeConverter",
                "global::System.DateTimeOffset" => "DateTimeOffsetConverter",
                "global::System.TimeSpan" => "TimeSpanConverter",
                _ => null,
            };
        }

        if (converter is null)
            return false;

        leaf = new JsonLeafModel(expr, $"global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.{converter}");
        return true;
    }

    /// <summary>
    ///     The name the property carries on the wire: what <c>[JsonPropertyName]</c> says, or the
    ///     camel-cased property name.
    /// </summary>
    /// <remarks>
    ///     Ignoring the attribute here would give one declaration two behaviours: the reflection
    ///     resolver honours it, so a context that hardcoded the camel-cased property name would make the
    ///     same type serialise differently depending on whether the application is published AOT.
    ///     Nothing would report the difference — the payload would simply not be the one the author
    ///     asked for, on the half of the deployments that matter most.
    /// </remarks>
    private static string WireName(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "JsonPropertyNameAttribute" } declaration)
                continue;
            if (declaration.ContainingNamespace?.ToDisplayString() != "System.Text.Json.Serialization")
                continue;
            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string name
                && !string.IsNullOrWhiteSpace(name))
                return name;
        }

        return CamelCase(property.Name);
    }

    private static string CamelCase(string name)
        => string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
