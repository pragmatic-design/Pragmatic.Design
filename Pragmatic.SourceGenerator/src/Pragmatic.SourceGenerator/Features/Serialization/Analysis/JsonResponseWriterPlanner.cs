using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     Plans the generated UTF-8 writer of an endpoint's response type: the bytes System.Text.Json writes for it
///     under the options the generated entry point gives the host, without the serializer.
/// </summary>
/// <remarks>
///     <para>
///         <b>The host's options, fixed at compile time.</b> camelCase names, nulls left out, enums by name, cycles
///         ignored, infrastructure members stripped where the host has persistence. A writer is used only where the
///         host's options are still those (<c>GeneratedJsonDefaults</c>); anywhere else the response keeps the
///         serializer.
///     </para>
///     <para>
///         <b>Reflection's rules, and the generated context's where it could answer.</b> A type is written by the
///         generated JSON context when its module opted in and the context covers it, by reflection otherwise. The two
///         agree on names and <c>[JsonIgnore]</c> (<c>TheContextWritesWhatReflectionWritesTests</c>), not on everything:
///         the context does not read <c>[JsonPropertyOrder]</c>. The planner follows reflection, and refuses an object
///         type the context could also describe and would describe differently: which of the two answers is decided at
///         run time by what the host registered, and a writer that matched one would be wrong under the other.
///     </para>
///     <para>
///         <b>A member typed <c>object</c> is the serializer's, one value at a time.</b> The writer leaves it out when
///         it is null and hands what it holds to the serializer, under the options the response is answered with
///         (<c>Utf8JsonValues.WriteUntyped</c>), so the rest of the type keeps its writer. The type is named in the
///         shape, so a converter the host registers for <c>object</c> sends the whole response back to the serializer.
///     </para>
///     <para>
///         <b>A cycle is refused, not cut.</b> <c>IgnoreCycles</c> writes a reference to an object already being
///         written as <c>null</c>; a writer generated for an acyclic type graph never meets one, and a cyclic
///         graph is left to the serializer.
///     </para>
/// </remarks>
internal sealed class JsonResponseWriterPlanner
{
    private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly string[] ChangeTrackingInterfaces =
    [
        "Pragmatic.Persistence.Entity.IChangeTracking",
        "Pragmatic.Events.IHasDomainEvents",
    ];

    private readonly Compilation _compilation;
    private readonly Dictionary<string, string> _planned = new(System.StringComparer.Ordinal);
    private readonly HashSet<string> _onThePath = new(System.StringComparer.Ordinal);
    private readonly List<JsonWriterMethodModel> _methods = [];

    // What the host's converters are asked about before the writer is used: every type it writes itself, and the
    // enums it writes by name. Ordered, so the emitted shape does not move between builds.
    private readonly SortedSet<string> _types = new(System.StringComparer.Ordinal);
    private readonly SortedDictionary<string, string?> _enums = new(System.StringComparer.Ordinal);
    private string? _rejection;
    private bool _located;
    private bool _needsExclusion;

    private JsonResponseWriterPlanner(Compilation compilation) => _compilation = compilation;

    /// <summary>The writer of <paramref name="root" />, or null with the reason it is left to the serializer.</summary>
    public static JsonResponseWriterPlan? TryPlan(ITypeSymbol root, Compilation compilation, out string? rejection)
    {
        var planner = new JsonResponseWriterPlanner(compilation);
        var typeExpr = root.ToDisplayString(Fq);

        string? entry;
        if (IsObject(root, out var named))
        {
            entry = planner.PlanObject(named, isEntryPoint: true);
        }
        else
        {
            var value = planner.Value(root);
            entry = value is null ? null : "Write_" + Token(typeExpr);
            if (entry is not null)
                planner._methods.Add(new JsonWriterMethodModel(entry, typeExpr, ImmutableArray<JsonWriterMemberModel>.Empty, IsEntryPoint: true)
                {
                    Root = value,
                });
        }

        rejection = planner._rejection;
        if (entry is null || rejection is not null)
        {
            rejection ??= $"{root.Name} cannot be written";
            return null;
        }

        var shape = new JsonWriterShapeModel(
            planner._types.ToImmutableArray(),
            planner._enums.Select(e => new JsonEnumConverterModel(e.Key, e.Value)).ToImmutableArray(),
            planner._needsExclusion);
        var methods = planner._methods
            .Select(m => m.Name == entry && m.IsEntryPoint ? m with { Shape = shape } : m)
            .ToImmutableArray();

        return new JsonResponseWriterPlan(entry, typeExpr, methods, planner._needsExclusion);
    }

    private string? PlanObject(INamedTypeSymbol type, bool isEntryPoint)
    {
        var typeExpr = type.ToDisplayString(Fq);
        _types.Add(typeExpr);
        if (_planned.TryGetValue(typeExpr, out var existing))
            return existing;

        if (_onThePath.Contains(typeExpr))
            return Reject($"{type.Name} reaches itself, and the host ignores cycles");

        if (!_compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly))
            return Reject($"{type.Name} is not accessible from this assembly");

        if (JsonResponseMemberReader.Read(type, out var why) is not { } members)
            return Reject(why ?? $"{type.Name} cannot be described");

        if (TheContextWouldDiffer(type, members))
            return Reject($"the generated JSON context and the reflection resolver would write {type.Name} differently");

        _onThePath.Add(typeExpr);

        var tracksChanges = type.AllInterfaces.Any(i => ChangeTrackingInterfaces.Contains(i.ToDisplayString()));
        var written = ImmutableArray.CreateBuilder<JsonWriterMemberModel>();
        foreach (var member in members)
        {
            // The host's modifier matches the wire name, whichever case it is in.
            if (global::Pragmatic.Contracts.ReservedWireNames.IsAlwaysStripped(member.WireName)
                || (tracksChanges && global::Pragmatic.Contracts.ReservedWireNames.IsChangeTracking(member.WireName)))
            {
                _needsExclusion = true;
                continue;
            }

            var value = Value(member.Property.Type);
            if (value is null)
            {
                // Named where it was met, once: the innermost member is the one to change.
                if (!_located)
                {
                    _located = true;
                    _rejection = $"{type.Name}.{member.Property.Name}: {_rejection}";
                }

                return null;
            }

            written.Add(new JsonWriterMemberModel(member.Property.Name, member.WireName, value)
            {
                Skip = member.Skip,
                TypeExpr = member.Property.Type.ToDisplayString(Fq),
            });
        }

        _onThePath.Remove(typeExpr);

        var name = "Write_" + Token(typeExpr);
        _planned[typeExpr] = name;
        _methods.Add(new JsonWriterMethodModel(name, typeExpr, written.ToImmutable(), isEntryPoint));
        return name;
    }

    private JsonWriterValueModel? Value(ITypeSymbol type)
    {
        if (_rejection is not null)
            return null;

        if (type.TypeKind != TypeKind.Enum)
            _types.Add(type.ToDisplayString(Fq));

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return Value(nullable.TypeArguments[0]) is { } underlying
                ? underlying with { IsNullableValueType = true, CanBeNull = true }
                : null;

        if (type.SpecialType == SpecialType.System_Object)
            return new JsonWriterValueModel(JsonWriterValueKind.Untyped, "", "", null, false, CanBeNull: true);

        if (Leaf(type) is { } leaf)
            return leaf;

        if (type.TypeKind == TypeKind.Enum)
            return EnumByName((INamedTypeSymbol)type);

        if (Sequence(type, out var key, out var element))
        {
            // A dictionary's key is written as a property name: a string as it is, an integer as its digits.
            string? keyCast = key?.SpecialType switch
            {
                null or SpecialType.System_String => "",
                SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_SByte => "long",
                SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Byte => "ulong",
                _ => null,
            };
            if (keyCast is null)
                return RejectValue($"a dictionary keyed by {key!.Name}");

            return Value(element!) is { } elementValue
                ? new JsonWriterValueModel(
                    key is null ? JsonWriterValueKind.Collection : JsonWriterValueKind.Dictionary,
                    keyCast, "", elementValue, false, CanBeNull: !type.IsValueType)
                : null;
        }

        if (IsObject(type, out var named))
            return PlanObject(named, isEntryPoint: false) is { } method
                ? new JsonWriterValueModel(JsonWriterValueKind.Object, "", method, null, false, CanBeNull: !type.IsValueType)
                : null;

        return RejectValue($"a member of type {type.ToDisplayString()}");
    }

    /// <summary>
    ///     An enum as <c>JsonStringEnumConverter</c> writes it, and as the generated <c>[FastEnum]</c> converter
    ///     writes it too: a declared member by name, anything else as its number.
    /// </summary>
    private JsonWriterValueModel? EnumByName(INamedTypeSymbol type)
    {
        if (!_compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly))
            return RejectValue($"{type.Name} is not accessible from this assembly");

        if (type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is "System.FlagsAttribute"
                                              or "System.Text.Json.Serialization.JsonConverterAttribute"))
            return RejectValue($"{type.Name} is a flags enum or declares a converter");

        var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => f is { IsConst: true, HasConstantValue: true }).ToList();
        if (fields.Any(f => f.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonStringEnumMemberNameAttribute")))
            return RejectValue($"{type.Name} renames a member on the wire");

        // Two names for one value: which one the converter writes is the runtime's choice, not the declaration's.
        if (fields.GroupBy(f => System.Convert.ToString(f.ConstantValue, System.Globalization.CultureInfo.InvariantCulture)).Any(g => g.Count() > 1))
            return RejectValue($"{type.Name} gives two names to one value");

        var unsigned = type.EnumUnderlyingType?.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16
            or SpecialType.System_UInt32 or SpecialType.System_UInt64;

        _enums[type.ToDisplayString(Fq)] = FastEnumConverter(type);

        return new JsonWriterValueModel(JsonWriterValueKind.EnumName, unsigned ? "ulong" : "long", "", null, false, false)
        {
            EnumType = type.ToDisplayString(Fq),
            EnumNames = fields.Select(f => new JsonEnumNameModel(f.Name, f.Name)).ToImmutableArray(),
        };
    }

    /// <summary>
    ///     The converter the generator emits for a <c>[FastEnum]</c> enum (<c>{Enum}JsonConverter</c>, beside it), which
    ///     a host registers ahead of <c>JsonStringEnumConverter</c> and which writes what it writes; null otherwise.
    /// </summary>
    /// <remarks>
    ///     Named here because this generator writes it, so the name cannot drift from what is emitted. A nested enum's
    ///     converter is not named: the host's check then sees a converter it does not expect, and keeps the serializer.
    /// </remarks>
    private static string? FastEnumConverter(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null
            || !type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.FastEnumAttribute"))
            return null;

        return type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? $"global::{ns.ToDisplayString()}.{type.Name}JsonConverter"
            : $"global::{type.Name}JsonConverter";
    }

    /// <summary>
    ///     Whether the generated JSON context could describe <paramref name="type" /> and would give it other
    ///     members, names or order than <paramref name="members" />.
    /// </summary>
    private static bool TheContextWouldDiffer(INamedTypeSymbol type, ImmutableArray<JsonResponseMemberReader.Member> members)
    {
        if (!JsonShapeExtractor.TryExtractClosure(type, out var objects, out _, out _))
            return false;

        var typeExpr = type.ToDisplayString(Fq);
        var shape = objects.FirstOrDefault(o => o.TypeExpr == typeExpr);
        if (shape is null)
            return false;

        return !shape.Properties.Select(p => (p.ClrName, p.JsonName))
            .SequenceEqual(members.Select(m => (m.Property.Name, m.WireName)));
    }

    private static JsonWriterValueModel? Leaf(ITypeSymbol type)
    {
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
                "global::System.DateOnly" => (JsonWriterValueKind.DateOnly, ""),
                "global::System.TimeOnly" => (JsonWriterValueKind.TimeOnly, ""),
                "global::System.Uri" => (JsonWriterValueKind.Uri, ""),
                "byte[]" => (JsonWriterValueKind.Base64, ""),
                _ => null,
            },
        };

        return leaf is { } found
            ? new JsonWriterValueModel(found.Kind, found.Cast, "", null, false, CanBeNull: !type.IsValueType)
            : null;
    }

    /// <summary>
    ///     Whether the type is written as a JSON array or a JSON object of keys, as the serializer decides it: a
    ///     dictionary first, then any <c>IEnumerable&lt;T&gt;</c>.
    /// </summary>
    private static bool Sequence(ITypeSymbol type, out ITypeSymbol? key, out ITypeSymbol? element)
    {
        key = null;
        element = null;
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type is IArrayTypeSymbol array)
        {
            element = array.Rank == 1 ? array.ElementType : null;
            return element is not null;
        }

        INamedTypeSymbol[] candidates = type is INamedTypeSymbol self ? [self, .. type.AllInterfaces] : [.. type.AllInterfaces];

        foreach (var candidate in candidates)
            if (candidate.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                key = candidate.TypeArguments[0];
                element = candidate.TypeArguments[1];
                return true;
            }

        foreach (var candidate in candidates)
            if (candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                element = candidate.TypeArguments[0];
                return true;
            }

        return false;
    }

    private static bool IsObject(ITypeSymbol type, out INamedTypeSymbol named)
    {
        named = null!;
        if (type is not INamedTypeSymbol candidate
            || candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct)
            || candidate.SpecialType != SpecialType.None
            || candidate.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            || candidate.ContainingNamespace?.ToDisplayString() is "System" or "System.Text.Json" or "System.Text.Json.Nodes"
            || Leaf(candidate) is not null
            || Sequence(candidate, out _, out _)
            || candidate.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
            return false;

        named = candidate;
        return true;
    }

    private string? Reject(string reason)
    {
        _rejection ??= reason;
        return null;
    }

    private JsonWriterValueModel? RejectValue(string reason)
    {
        _rejection ??= reason;
        return null;
    }

    private static string Token(string typeExpr)
        => typeExpr.Replace("global::", "")
            .Replace('.', '_').Replace('+', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_')
            .Replace('[', '_').Replace(']', '_').Replace('?', '_');
}
