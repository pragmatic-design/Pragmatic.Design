using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     Plans the generated UTF-8 JSON writer of a type: the methods that write it and every object it reaches,
///     with the redaction mask at the paths it is given.
/// </summary>
/// <remarks>
///     <para>
///         <b>One shape, two writers.</b> A type is planned only when <see cref="JsonShapeExtractor" /> can
///         describe it — the same closure, member order and wire names the generated JSON context is written
///         from — so a writer and the context cannot disagree about what the type looks like on the wire. A
///         type it cannot describe has no writer, and whoever asked keeps the serializer's path.
///     </para>
///     <para>
///         <b>The mask follows the declared redactor's rules</b>, because that is the output a writer has to be
///         identical to: a path is <c>Member</c>, <c>Member.Inner</c>, <c>Member[]</c> or <c>Member[].Inner</c>,
///         its segments match the wire name case-insensitively, a masked member becomes the mask even when it
///         is null, an element of a masked collection becomes the mask unless it is null, and a path whose
///         segment meets the wrong kind of value (<c>.Inner</c> on an array) masks nothing.
///     </para>
///     <para>
///         Not covered, so not planned: a polymorphic or abstract type, a dictionary, a member without a
///         public getter, a type the generated code cannot name.
///     </para>
/// </remarks>
internal sealed class JsonWriterPlanner
{
    private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Dictionary<string, JsonObjectModel> _shapes;
    private readonly Dictionary<string, string> _planned = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, int> _variants = new(System.StringComparer.Ordinal);
    private readonly List<JsonWriterMethodModel> _methods = [];
    private bool _failed;

    private JsonWriterPlanner(Dictionary<string, JsonObjectModel> shapes) => _shapes = shapes;

    /// <summary>
    ///     The writer of <paramref name="root" />: the name of the method that writes it, and every method
    ///     the plan needs. False when the type is not covered.
    /// </summary>
    /// <param name="root">The type to write.</param>
    /// <param name="maskPaths">The paths, relative to <paramref name="root" />, written as the mask.</param>
    /// <param name="entryMethod">The method that writes <paramref name="root" />.</param>
    /// <param name="methods">Every method the plan needs, the entry one included.</param>
    public static bool TryPlan(
        INamedTypeSymbol root,
        IEnumerable<string> maskPaths,
        out string entryMethod,
        out ImmutableArray<JsonWriterMethodModel> methods)
    {
        entryMethod = "";
        methods = ImmutableArray<JsonWriterMethodModel>.Empty;

        if (!JsonShapeExtractor.TryExtractClosure(root, out var objects, out _, out var collections))
            return false;

        if (objects.Any(o => o.IsAbstract || o.DiscriminatorName is not null)
            || collections.Any(c => c.TypeExpr.StartsWith("global::System.Collections.Generic.Dictionary<", System.StringComparison.Ordinal)))
            return false;

        var planner = new JsonWriterPlanner(objects.ToDictionary(o => o.TypeExpr, System.StringComparer.Ordinal));
        var name = planner.Plan(root, Normalize(maskPaths), isEntryPoint: true);
        if (planner._failed || name is null)
            return false;

        entryMethod = name;
        methods = planner._methods.ToImmutableArray();
        return true;
    }

    private string? Plan(INamedTypeSymbol type, ImmutableArray<string> masks, bool isEntryPoint)
    {
        var typeExpr = type.ToDisplayString(Fq);
        var key = typeExpr + "|" + string.Join(";", masks);
        if (_planned.TryGetValue(key, out var existing))
            return existing;

        if (!_shapes.TryGetValue(typeExpr, out var shape) || !IsNameable(type))
        {
            _failed = true;
            return null;
        }

        var token = typeExpr.Replace("global::", "")
            .Replace('.', '_').Replace('+', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');
        var name = "Write_" + token;
        if (masks.Length > 0)
        {
            _variants.TryGetValue(token, out var variant);
            _variants[token] = variant + 1;
            name += "_Masked" + variant;
        }

        // Registered before the members are planned, so a type that reaches itself (A → B → A) ends.
        _planned[key] = name;

        var symbols = PropertyAnalyzer.GetAllProperties(type);
        var members = ImmutableArray.CreateBuilder<JsonWriterMemberModel>();
        foreach (var property in shape.Properties)
        {
            var symbol = symbols.FirstOrDefault(p => p.Name == property.ClrName);
            if (symbol?.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
            {
                _failed = true;
                return null;
            }

            var value = Member(symbol.Type, property.JsonName, masks);
            if (value is null)
            {
                _failed = true;
                return null;
            }

            members.Add(new JsonWriterMemberModel(property.ClrName, property.JsonName, value));
        }

        _methods.Add(new JsonWriterMethodModel(name, typeExpr, members.ToImmutable(), isEntryPoint));
        return name;
    }

    /// <summary>A member's value, with the masks that apply below it.</summary>
    private JsonWriterValueModel? Member(ITypeSymbol type, string jsonName, ImmutableArray<string> masks)
    {
        var whole = false;
        var elementsWhole = false;
        var below = ImmutableArray.CreateBuilder<string>();
        var belowElements = ImmutableArray.CreateBuilder<string>();

        foreach (var mask in masks)
        {
            var separator = mask.IndexOf('.');
            var segment = separator < 0 ? mask : mask.Substring(0, separator);
            var rest = separator < 0 ? null : mask.Substring(separator + 1);

            var overElements = segment.EndsWith("[]", System.StringComparison.Ordinal);
            if (overElements)
                segment = segment.Substring(0, segment.Length - 2);

            if (!string.Equals(segment, jsonName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            switch (overElements, rest)
            {
                case (false, null): whole = true; break;
                case (true, null): elementsWhole = true; break;
                case (false, { } inner): below.Add(inner); break;
                case (true, { } inner): belowElements.Add(inner); break;
            }
        }

        // The declared redactor replaces the node whatever it holds, a JSON null included.
        if (whole)
            return new JsonWriterValueModel(JsonWriterValueKind.Mask, "", "", null, IsNullableValueType: false, CanBeNull: false);

        return Value(type, Normalize(below), elementsWhole, Normalize(belowElements));
    }

    private JsonWriterValueModel? Value(
        ITypeSymbol type, ImmutableArray<string> objectMasks, bool elementsWhole, ImmutableArray<string> elementMasks)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var underlying = Leaf(nullable.TypeArguments[0]);
            return underlying is null ? null : underlying with { IsNullableValueType = true, CanBeNull = true };
        }

        if (Leaf(type) is { } leaf)
            return leaf;

        var element = type switch
        {
            IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
            INamedTypeSymbol { IsGenericType: true } list
                when list.OriginalDefinition.ToDisplayString(Fq) == "global::System.Collections.Generic.List<T>" => list.TypeArguments[0],
            _ => null,
        };

        if (element is not null)
        {
            var elementValue = Value(element, elementMasks, elementsWhole: false, ImmutableArray<string>.Empty);
            if (elementValue is null)
                return null;

            // An element of a masked collection is the mask; a null element stays null, because the
            // redactor's replacement needs a node and a JSON null is none.
            if (elementsWhole)
                elementValue = new JsonWriterValueModel(JsonWriterValueKind.Mask, "", "", null, false, CanBeNull: elementValue.CanBeNull);

            return new JsonWriterValueModel(JsonWriterValueKind.Collection, "", "", elementValue, false, CanBeNull: true);
        }

        if (type is INamedTypeSymbol named && type.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            var method = Plan(named, objectMasks, isEntryPoint: false);
            return method is null
                ? null
                : new JsonWriterValueModel(JsonWriterValueKind.Object, "", method, null, false, CanBeNull: !type.IsValueType);
        }

        return null;
    }

    private static JsonWriterValueModel? Leaf(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
        {
            var cast = underlying.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16
                or SpecialType.System_UInt32 or SpecialType.System_UInt64
                ? "ulong"
                : "long";
            return new JsonWriterValueModel(JsonWriterValueKind.Enum, cast, "", null, false, false);
        }

        (JsonWriterValueKind Kind, string Cast)? leaf = type.SpecialType switch
        {
            SpecialType.System_String => (JsonWriterValueKind.String, ""),
            SpecialType.System_Boolean => (JsonWriterValueKind.Boolean, ""),
            SpecialType.System_Char => (JsonWriterValueKind.Char, ""),
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_SByte => (JsonWriterValueKind.Number, "int"),
            SpecialType.System_UInt32 or SpecialType.System_UInt16 or SpecialType.System_Byte => (JsonWriterValueKind.Number, "uint"),
            SpecialType.System_Int64 => (JsonWriterValueKind.Number, "long"),
            SpecialType.System_UInt64 => (JsonWriterValueKind.Number, "ulong"),
            SpecialType.System_Single => (JsonWriterValueKind.Number, "float"),
            SpecialType.System_Double => (JsonWriterValueKind.Number, "double"),
            SpecialType.System_Decimal => (JsonWriterValueKind.Number, "decimal"),
            SpecialType.System_DateTime => (JsonWriterValueKind.DateTime, ""),
            _ => type.ToDisplayString(Fq) switch
            {
                "global::System.Guid" => (JsonWriterValueKind.StringValue, ""),
                "global::System.DateTimeOffset" => (JsonWriterValueKind.DateTimeOffset, ""),
                "global::System.TimeSpan" => (JsonWriterValueKind.TimeSpan, ""),
                _ => null,
            },
        };

        return leaf is { } found
            ? new JsonWriterValueModel(found.Kind, found.Cast, "", null, false, CanBeNull: found.Kind == JsonWriterValueKind.String)
            : null;
    }

    /// <summary>Whether the generated writer class, in the type's assembly, can name it.</summary>
    private static bool IsNameable(INamedTypeSymbol type)
    {
        for (ISymbol? current = type; current is INamedTypeSymbol named; current = named.ContainingType)
            if (named.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                return false;

        return true;
    }

    /// <summary>Distinct, ordered, so two plans of the same type under the same masks are one method.</summary>
    private static ImmutableArray<string> Normalize(IEnumerable<string> masks)
        => masks.Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, System.StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
}
